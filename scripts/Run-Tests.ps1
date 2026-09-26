param(
    [ValidateSet('Fast', 'Integration', 'E2E', 'Scenarios', 'Workspaces', 'Cases')]
    [string]$Suite = 'Fast',
    [string]$Filter,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSBoundParameters.ContainsKey('Filter') -and [string]::IsNullOrWhiteSpace($Filter)) {
    throw 'The test filter must not be empty.'
}
if ($Suite -in @('Integration', 'Scenarios') -and [string]::IsNullOrWhiteSpace($Filter)) {
    throw "$Suite requires an explicit test filter. Use Fast for routine feedback or E2E for the full slow selection."
}

$repository = Split-Path $PSScriptRoot -Parent
Push-Location $repository
try {
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the revision under test.' }
    $worktree = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine whether the tested worktree is modified.' }
    if ($worktree) { $revision += ' (working tree modified)' }
    $projects = switch ($Suite) {
        'Workspaces' { 'Callrift.Workspaces' }
        'Cases' { 'Callrift.RealWorldCases' }
        'E2E' { 'Callrift.Scenarios'; 'Callrift.Workspaces'; 'Callrift.RealWorldCases' }
        default { 'Callrift.Scenarios' }
    }
    foreach ($project in $projects) {
        $selection = switch ($Suite) {
            'Fast' { 'Layer=Fast' }
            'Integration' { 'Layer!=Fast' }
            'E2E' { if ($project -eq 'Callrift.Scenarios') { 'Layer!=Fast' } }
        }
        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $selection = if ($selection) { "($selection)&($Filter)" } else { $Filter }
        }
        $results = Join-Path $repository "tests/$project/TestResults"
        $fileName = "$Suite-$([Guid]::NewGuid().ToString('N')).trx"
        $arguments = @('test', "tests/$project/$project.csproj", '--logger', "trx;LogFileName=$fileName", '--results-directory', $results)
        if ($selection) { $arguments += @('--filter', $selection) }
        if ($NoBuild) { $arguments += '--no-build' }
        $reproduce = "mise exec -- pwsh -NoProfile -File scripts/Run-Tests.ps1 -Suite $Suite"
        if ($Filter) { $reproduce += " -Filter '$($Filter.Replace("'", "''"))'" }
        Write-Host "Revision: $revision; project: $project; selection: $selection"
        Write-Host "Reproduce: $reproduce"
        $timer = [Diagnostics.Stopwatch]::StartNew()
        & dotnet @arguments
        $testExitCode = $LASTEXITCODE
        $timer.Stop()
        $resultPath = Join-Path $results $fileName
        $elapsed = $timer.Elapsed.TotalSeconds.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)
        if (-not (Test-Path $resultPath)) {
            throw "$project did not produce test results (exit $testExitCode, ${elapsed}s). Reproduce: $reproduce"
        }
        [xml]$document = Get-Content -Raw $resultPath
        $counters = $document.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters) { throw "$project produced no test counters: $resultPath" }
        $executed = [int]$counters.GetAttribute('executed')
        $failed = [int]$counters.GetAttribute('failed')
        $summary = "$project at ${revision}: $executed executed, $failed failed, ${elapsed}s including dotnet startup and any build/restore."
        Write-Host $summary
        if ($env:GITHUB_STEP_SUMMARY) {
            Add-Content $env:GITHUB_STEP_SUMMARY "$summary`n`nReproduce: ``$reproduce```n"
        }
        foreach ($failure in $document.SelectNodes("//*[local-name()='UnitTestResult'][@outcome='Failed']")) {
            Write-Host "Failed case: $($failure.GetAttribute('testName'))"
        }
        if ($executed -eq 0) { throw "No tests executed for '$selection' in $project. Check the filter. Reproduce: $reproduce" }
        if ($testExitCode -ne 0 -or $failed -ne 0) { throw "$project failed (exit $testExitCode). Reproduce: $reproduce" }
    }
}
finally {
    Pop-Location
}
