param([string]$Version = '0.1.0-preview.1')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$originalPackages = $env:NUGET_PACKAGES
try {
    $feed = Join-Path $root 'artifacts/packages'
    $installation = Join-Path $root ('artifacts/smoke-' + [guid]::NewGuid().ToString('N'))
    dotnet pack src/Callrift.Core -c Release -o $feed "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Library packing failed.' }
    dotnet pack src/Callrift.Cli -c Release -o $feed "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Tool packing failed.' }
    dotnet tool install callrift --version $Version --tool-path $installation --source $feed
    if ($LASTEXITCODE -ne 0) { throw 'Local tool installation failed.' }
    $command = Join-Path $installation 'callrift'
    & $command --help
    if ($LASTEXITCODE -ne 0) { throw 'Installed tool startup failed.' }
    $source = & $command tree HEAD --entry CallQueries.RunAsync --format json 2> (Join-Path $installation 'source.stderr')
    if ($LASTEXITCODE -ne 0) { throw 'Installed source-only analysis failed.' }
    $sourceGraph = ($source -join "`n") | ConvertFrom-Json
    if ($sourceGraph.analysis.mode -ne 'source' -or $sourceGraph.trees.Count -eq 0) { throw 'Source-only smoke returned no tree.' }
    $workspace = & $command tree HEAD --entry CallQueries.RunAsync --project src/Callrift.Core/Callrift.Core.csproj --format json 2> (Join-Path $installation 'workspace.stderr')
    if ($LASTEXITCODE -ne 0) { throw ('Installed workspace analysis failed: ' + (Get-Content (Join-Path $installation 'workspace.stderr') -Raw)) }
    $workspaceGraph = ($workspace -join "`n") | ConvertFrom-Json
    if ($workspaceGraph.analysis.mode -ne 'msbuild' -or $workspaceGraph.trees.Count -eq 0) { throw 'Workspace smoke returned no tree.' }
    $consumer = Join-Path $installation 'consumer'
    New-Item -ItemType Directory $consumer | Out-Null
    $project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include="Callrift.Core" Version="' + $Version + '" /></ItemGroup></Project>'
    [IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), $project)
    [IO.File]::WriteAllText((Join-Path $consumer 'Program.cs'), 'using Callrift.Core; var result = await new CallQueries().RunAsync(new QueryRequest(args[0], "HEAD") { Options = new DiffOptions { Entries = ["CallQueries.RunAsync"] } }); if (result.Trees.Count == 0) throw new System.InvalidOperationException("Library returned no tree.");')
    $configuration = '<configuration><packageSources><clear /><add key="local" value="' + [System.Security.SecurityElement]::Escape($feed) + '" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>'
    [IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), $configuration)
    dotnet restore (Join-Path $consumer 'Consumer.csproj') --configfile (Join-Path $consumer 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Library consumer restore failed.' }
    dotnet run --project (Join-Path $consumer 'Consumer.csproj') --no-restore -- $root
    if ($LASTEXITCODE -ne 0) { throw 'Library consumer failed.' }
    $env:NUGET_PACKAGES = Join-Path $installation 'dnx-packages'
    dnx "callrift@$Version" --source $feed --yes -- --help
    if ($LASTEXITCODE -ne 0) { throw 'One-shot tool execution failed.' }
}
finally {
    $env:NUGET_PACKAGES = $originalPackages
    Pop-Location
}
