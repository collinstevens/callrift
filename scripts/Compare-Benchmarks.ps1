param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Current,
    [double]$TimeRatio = 2.0,
    [double]$AllocationRatio = 1.5
)
$ErrorActionPreference = 'Stop'
$previous = (Get-Content $Baseline -Raw | ConvertFrom-Json).Benchmarks
$results = (Get-Content $Current -Raw | ConvertFrom-Json).Benchmarks
foreach ($result in $results) {
    if ($null -eq $result.Statistics) { throw ('Benchmark has no measurements: ' + $result.FullName) }
    $match = $previous | Where-Object { $_.FullName -eq $result.FullName -and $_.Parameters -eq $result.Parameters }
    if ($null -eq $match) { continue }
    $time = $result.Statistics.Mean / $match.Statistics.Mean
    $allocation = if ($match.Memory.BytesAllocatedPerOperation -gt 0) { $result.Memory.BytesAllocatedPerOperation / $match.Memory.BytesAllocatedPerOperation } else { 1 }
    $message = '{0}: time {1:N2}x, allocations {2:N2}x' -f $result.FullName, $time, $allocation
    Write-Output $message
    if ($time -gt $TimeRatio -or $allocation -gt $AllocationRatio) {
        Write-Output "::warning::$message exceeds the review threshold; compare on the same machine before claiming a regression."
    }
}
