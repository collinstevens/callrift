param(
    [ValidateSet('Fast', 'Integration', 'E2E', 'Scenarios', 'Workspaces', 'Cases')]
    [string]$Suite = 'Fast',
    [string]$Filter,
    [ValidateSet('A', 'B')]
    [string]$Shard,
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
if ($Shard -and $Suite -notin @('Scenarios', 'Workspaces', 'Cases')) {
    throw 'Shards are available only for Scenarios, Workspaces and Cases.'
}

$shardFilter = $null
if ($Shard) {
    $classes = @((Get-Content -Raw (Join-Path $PSScriptRoot 'Test-ShardA.json') | ConvertFrom-Json -AsHashtable)[$Suite])
    if ($classes.Count -eq 0 -or $classes.Where({ [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) {
        throw "No shard classes are configured for $Suite."
    }
    $shardFilter = if ($Shard -eq 'A') {
        ($classes | ForEach-Object { "FullyQualifiedName~$_." }) -join '|'
    }
    else {
        ($classes | ForEach-Object { "FullyQualifiedName!~$_." }) -join '&'
    }
}

$repository = Split-Path $PSScriptRoot -Parent
Push-Location $repository
try {
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the revision under test.' }
    $worktree = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine whether the tested worktree is modified.' }
    if ($worktree) {
        $revision += ' (working tree modified)'
        Write-Host ("Working tree changes:`n" + ($worktree -join "`n"))
    }
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
        if ($shardFilter) {
            $selection = if ($selection) { "($selection)&($shardFilter)" } else { $shardFilter }
        }
        $results = Join-Path $repository "tests/$project/TestResults"
        $resultLabel = if ($Shard) { "$Suite-$Shard" } else { $Suite }
        $fileName = "$resultLabel-$([Guid]::NewGuid().ToString('N')).trx"
        $arguments = @('test', "tests/$project/$project.csproj", '--logger', "trx;LogFileName=$fileName", '--results-directory', $results)
        if ($selection) { $arguments += @('--filter', $selection) }
        if ($NoBuild) { $arguments += '--no-build' }
        $reproduce = "mise exec -- pwsh -NoProfile -File scripts/Run-Tests.ps1 -Suite $Suite"
        if ($Filter) { $reproduce += " -Filter '$($Filter.Replace("'", "''"))'" }
        if ($Shard) { $reproduce += " -Shard $Shard" }
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
        $displayProject = if ($Shard) { "$project shard $Shard" } else { $project }
        $summary = "$displayProject at ${revision}: $executed executed, $failed failed, ${elapsed}s including dotnet startup and any build/restore."
        Write-Host $summary
        if ($Suite -ne 'Fast') {
            $classes = @{}
            foreach ($definition in $document.SelectNodes("//*[local-name()='UnitTest']")) {
                $method = $definition.SelectSingleNode("*[local-name()='TestMethod']")
                if ($null -ne $method) { $classes[$definition.GetAttribute('id')] = $method.GetAttribute('className') }
            }
            $timings = foreach ($result in $document.SelectNodes("//*[local-name()='UnitTestResult']")) {
                $duration = [TimeSpan]::Zero
                if (-not [TimeSpan]::TryParse($result.GetAttribute('duration'), [Globalization.CultureInfo]::InvariantCulture, [ref]$duration)) { continue }
                [pscustomobject]@{
                    Name = $result.GetAttribute('testName')
                    Class = $classes[$result.GetAttribute('testId')]
                    Seconds = $duration.TotalSeconds
                }
            }
            $classWork = $timings | Group-Object Class | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Seconds = ($_.Group | Measure-Object Seconds -Sum).Sum }
            }
            foreach ($group in ($classWork | Sort-Object Seconds -Descending | Select-Object -First 5)) {
                $seconds = $group.Seconds.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)
                Write-Host "Class accumulated case time: ${seconds}s $($group.Name)"
            }
            foreach ($case in ($timings | Sort-Object Seconds -Descending | Select-Object -First 5)) {
                $seconds = $case.Seconds.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)
                Write-Host "Slow case: ${seconds}s $($case.Name)"
            }
        }
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
