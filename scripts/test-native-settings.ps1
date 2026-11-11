param([Parameter(Mandatory)][string]$ResultPath)
$ErrorActionPreference = 'Stop'
$data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AnpingLock'
$path = Join-Path $data 'settings.json'
$original = [IO.File]::ReadAllText($path)
$settings = $original | ConvertFrom-Json
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$result = [ordered]@{ SettingsPath=$path; OriginalSettings=($original | ConvertFrom-Json); Passed=$false }
try {
    $agent = Get-Process -Name AnpingLock.Agent
    $settings.Enabled = $false
    [IO.File]::WriteAllText($path+'.test-tmp', ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($path+'.test-tmp', $path, $true)
    $event = [Threading.EventWaitHandle]::OpenExisting('Local\AnpingLock.Settings.'+$sid)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $null = $event.Set()
    $event.Dispose()
    $result.Passed = $agent.WaitForExit(10000)
    $result.Milliseconds = $watch.ElapsedMilliseconds
    $result.FinalState = Get-Content -LiteralPath (Join-Path $data 'agent-state.json') -Raw | ConvertFrom-Json
} catch { $result.Error = $_.Exception.ToString() }
finally {
    [IO.File]::WriteAllText($path+'.test-tmp', $original, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($path+'.test-tmp', $path, $true)
    if (($original | ConvertFrom-Json).Enabled) { Start-ScheduledTask -TaskName ('AnpingLock-'+$sid) }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultPath -Encoding utf8
}
