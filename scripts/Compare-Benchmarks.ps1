param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Current,
    [double]$TimeRatio = 2.0,
    [double]$AllocationRatio = 1.5
)
$ErrorActionPreference = 'Stop'
if (-not [double]::IsFinite($TimeRatio) -or $TimeRatio -le 0 -or -not [double]::IsFinite($AllocationRatio) -or $AllocationRatio -le 0) {
    throw 'Review thresholds must be finite and positive.'
}
$previousReport = Get-Content $Baseline -Raw | ConvertFrom-Json
$currentReport = Get-Content $Current -Raw | ConvertFrom-Json
$previous = @($previousReport.Benchmarks)
$results = @($currentReport.Benchmarks)
foreach ($report in @($previousReport, $currentReport)) {
    if (@($report.Benchmarks).Count -eq 0 -or $null -eq $report.Benchmarks) { throw 'Benchmark report is empty.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($result in $report.Benchmarks) {
        if ([string]::IsNullOrWhiteSpace($result.FullName) -or -not $names.Add($result.FullName)) { throw 'Benchmark names are missing or duplicated.' }
        if ($null -eq $result.Statistics -or $result.Statistics.N -lt 1 -or $null -eq $result.Statistics.Mean -or -not [double]::IsFinite($result.Statistics.Mean) -or $result.Statistics.Mean -le 0) {
            throw ('Benchmark has no valid measurements: ' + $result.FullName)
        }
        if ($null -eq $result.Memory -or $null -eq $result.Memory.BytesAllocatedPerOperation -or -not [double]::IsFinite($result.Memory.BytesAllocatedPerOperation) -or $result.Memory.BytesAllocatedPerOperation -lt 0) {
            throw ('Benchmark has no valid allocation measurements: ' + $result.FullName)
        }
    }
}
$matches = @($results | Where-Object { $_.FullName -cin $previous.FullName })
if ($matches.Count -eq 0) { throw 'Benchmark reports have no matching workloads.' }
$environmentFields = @('BenchmarkDotNetVersion', 'OsVersion', 'ProcessorName', 'PhysicalProcessorCount', 'PhysicalCoreCount', 'LogicalCoreCount', 'RuntimeVersion', 'Architecture', 'HasAttachedDebugger', 'HasRyuJit', 'Configuration', 'DotNetCliVersion')
$differences = @($environmentFields | Where-Object {
    $null -eq $previousReport.HostEnvironmentInfo.$_ -or $null -eq $currentReport.HostEnvironmentInfo.$_ -or $previousReport.HostEnvironmentInfo.$_ -cne $currentReport.HostEnvironmentInfo.$_
})
if ($differences.Count -gt 0) {
    Write-Output ('::warning::Benchmark environments differ or lack metadata in: ' + ($differences -join ', ') + '. Ratios were not calculated; capture a comparable baseline before assessing regressions.')
    return
}
$compared = 0
foreach ($result in $results) {
    $match = $previous | Where-Object { $_.FullName -ceq $result.FullName -and $_.Parameters -ceq $result.Parameters }
    if ($null -eq $match) {
        Write-Output ('No baseline for ' + $result.FullName)
        continue
    }
    if ($result.DisplayInfo -cne $match.DisplayInfo -or $result.HardwareIntrinsics -cne $match.HardwareIntrinsics) {
        Write-Output ('::warning::Benchmark job or hardware instructions differ for ' + $result.FullName + '; ratios were not calculated.')
        continue
    }
    $compared++
    $time = $result.Statistics.Mean / $match.Statistics.Mean
    $allocation = if ($match.Memory.BytesAllocatedPerOperation -gt 0) { $result.Memory.BytesAllocatedPerOperation / $match.Memory.BytesAllocatedPerOperation }
    elseif ($result.Memory.BytesAllocatedPerOperation -eq 0) { 1 }
    else { [double]::PositiveInfinity }
    $message = '{0}: time {1:N2}x, allocations {2:N2}x' -f $result.FullName, $time, $allocation
    Write-Output $message
    if ($time -gt $TimeRatio -or $allocation -gt $AllocationRatio) {
        Write-Output "::warning::$message exceeds the review threshold; compare on the same machine before claiming a regression."
    }
}
Write-Output ('Compared {0} of {1} current workloads. Ratios are review signals; confirm identical workloads and controlled machine conditions before claiming a regression.' -f $compared, $results.Count)
