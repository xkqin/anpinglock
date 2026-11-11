param([int]$Seconds = 60, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (-not ('AwayLockResourceIo' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AwayLockResourceIo {
    [StructLayout(LayoutKind.Sequential)]
    public struct Counts { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool GetProcessIoCounters(IntPtr process, out Counts counts);
}
'@
}
$agent = Get-Process -Name AwayLock.Agent
$started = [DateTimeOffset]::Now
$cpu = $agent.TotalProcessorTime.TotalMilliseconds
$initialIo = New-Object AwayLockResourceIo+Counts
if (-not [AwayLockResourceIo]::GetProcessIoCounters($agent.Handle, [ref]$initialIo)) { throw 'Cannot read process IO counters' }
$samples = @()
$statePath = Join-Path $env:LOCALAPPDATA 'AwayLock\agent-state.json'
do {
    $agent.Refresh()
    if ($agent.HasExited) { throw 'Agent exited during measurement' }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $samples += [pscustomobject]@{
        At = [DateTimeOffset]::Now.ToString('o')
        WorkingSetMiB = $agent.WorkingSet64 / 1MB
        PrivateMiB = $agent.PrivateMemorySize64 / 1MB
        Threads = $agent.Threads.Count
        State = $state.Snapshot.State
    }
    Start-Sleep -Seconds 2
} while (([DateTimeOffset]::Now - $started).TotalSeconds -lt $Seconds)
$agent.Refresh()
$finalIo = New-Object AwayLockResourceIo+Counts
if (-not [AwayLockResourceIo]::GetProcessIoCounters($agent.Handle, [ref]$finalIo)) { throw 'Cannot read process IO counters' }
$elapsed = ([DateTimeOffset]::Now - $started).TotalSeconds
$cpuMilliseconds = $agent.TotalProcessorTime.TotalMilliseconds - $cpu
$result = [pscustomobject]@{
    At = [DateTimeOffset]::Now.ToString('o')
    ProcessId = $agent.Id
    DurationSeconds = $elapsed
    CpuMilliseconds = $cpuMilliseconds
    CpuPercentOneCore = $cpuMilliseconds / ($elapsed * 10)
    WorkingSetMiB = $agent.WorkingSet64 / 1MB
    PeakSampledWorkingSetMiB = ($samples | Measure-Object WorkingSetMiB -Maximum).Maximum
    PrivateMiB = $agent.PrivateMemorySize64 / 1MB
    Threads = $agent.Threads.Count
    Handles = $agent.HandleCount
    ReadOperations = $finalIo.ReadOperations - $initialIo.ReadOperations
    WriteOperations = $finalIo.WriteOperations - $initialIo.WriteOperations
    WriteBytes = $finalIo.WriteBytes - $initialIo.WriteBytes
    States = @($samples.State | Sort-Object -Unique)
    LoadedClr = @($agent.Modules | Where-Object { $_.ModuleName -in 'coreclr.dll','clr.dll' }).Count -gt 0
    Samples = $samples
}
$json = $result | ConvertTo-Json -Depth 5
if ($OutputPath) { $json | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
$result | Select-Object * -ExcludeProperty Samples | ConvertTo-Json
