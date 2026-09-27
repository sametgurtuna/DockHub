<#
.SYNOPSIS
    Measures DockHub while it sits idle: CPU, memory, garbage collector and start-up time.

.DESCRIPTION
    Records the .NET runtime counters of the running DockHub process with dotnet-counters for the given time, then
    prints averages and the change in memory, next to the targets of plans/faz-9-performans-ve-kod-sagligi.md
    (idle CPU below 0.1%, memory growth below 10%). The start-up time comes from the last "First dock frame" line
    of %AppData%\DockHub\log.txt.

    Leave the computer alone while it runs. For wake-ups per second, record a trace with Windows Performance
    Recorder (CPU usage, precise) or run "powercfg /energy" as an administrator.

.PARAMETER Minutes
    How long to record (default 10).

.PARAMETER Output
    Where to keep the raw counters (CSV). Default: a file in the temp folder.

.EXAMPLE
    dotnet tool install --global dotnet-counters
    .\tools\measure-idle.ps1 -Minutes 10
#>
param(
    [int]$Minutes = 10,
    [string]$Output = (Join-Path $env:TEMP "dockhub-counters-$(Get-Date -Format yyyyMMdd-HHmmss).csv")
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet-counters -ErrorAction SilentlyContinue)) {
    throw "dotnet-counters was not found. Install it with: dotnet tool install --global dotnet-counters"
}

$process = Get-Process -Name DockHub -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { throw "DockHub is not running." }

$startWorkingSet = $process.WorkingSet64
Write-Host "Recording DockHub (PID $($process.Id)) for $Minutes minute(s). Don't use the computer until it finishes..."
$duration = [TimeSpan]::FromMinutes($Minutes).ToString('hh\:mm\:ss')
& dotnet-counters collect --process-id $process.Id --counters System.Runtime --format csv --output $Output --duration $duration | Out-Null

$process.Refresh()
$endWorkingSet = $process.WorkingSet64
$rows = Import-Csv $Output

function Average($pattern) {
    $values = $rows | Where-Object { $_.'Counter Name' -like $pattern } | ForEach-Object { [double]$_.'Mean/Increment' }
    if ($values) { ($values | Measure-Object -Average).Average } else { $null }
}

$cpu = Average '*CPU Usage*'
$heap = Average '*GC Heap Size*'
$gen0 = Average '*Gen 0 GC Count*'
$growth = if ($startWorkingSet -gt 0) { ($endWorkingSet - $startWorkingSet) * 100.0 / $startWorkingSet } else { 0 }

$logFile = Join-Path $env:APPDATA 'DockHub\log.txt'
$startup = if (Test-Path $logFile) {
    Select-String -Path $logFile -Pattern 'First dock frame (\d+) ms' | Select-Object -Last 1 |
        ForEach-Object { [int]$_.Matches[0].Groups[1].Value }
}

function Mark($ok) { if ($ok) { 'ok' } else { 'above target' } }

Write-Host ""
Write-Host "DockHub idle measurement ($Minutes min)"
Write-Host ("  CPU (average)          {0,8:N3} %    target < 0.1 %   {1}" -f $cpu, (Mark ($cpu -lt 0.1)))
Write-Host ("  Working set            {0,8:N1} MB -> {1:N1} MB ({2:+0.0;-0.0} %)   target growth < 10 %   {3}" -f ($startWorkingSet / 1MB), ($endWorkingSet / 1MB), $growth, (Mark ($growth -lt 10)))
if ($heap -ne $null) { Write-Host ("  GC heap (average)      {0,8:N1} MB" -f $heap) }
if ($gen0 -ne $null) { Write-Host ("  Gen 0 GCs per second   {0,8:N2}" -f $gen0) }
if ($startup) { Write-Host ("  Start-up               {0,8} ms to the first dock frame (from log.txt)" -f $startup) }
Write-Host ""
Write-Host "Raw counters: $Output"
