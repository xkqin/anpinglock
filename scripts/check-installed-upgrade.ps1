param([Parameter(Mandatory)][string]$ResultPath)
$ErrorActionPreference = 'Stop'
trap {
    [pscustomobject]@{ Passed=$false; Error=$_.Exception.ToString(); At=$_.ScriptStackTrace } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultPath -Encoding utf8
    exit 1
}
$base = [Environment]::GetFolderPath('LocalApplicationData')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$old = Get-Content -LiteralPath (Join-Path $base 'AwayLock\settings.json') -Raw | ConvertFrom-Json
$current = Get-Content -LiteralPath (Join-Path $base 'AnpingLock\settings.json') -Raw | ConvertFrom-Json
$agent = Get-Process -Name AnpingLock.Agent -ErrorAction SilentlyContinue
$oldTask = Get-ScheduledTask | Where-Object TaskName -eq ('AwayLock-'+$sid)
$task = Get-ScheduledTask -TaskName ('AnpingLock-'+$sid) -ErrorAction Stop
$installed = Join-Path $base 'Programs\AnpingLock'
$entry = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{99ECBCBF-473D-45F6-B32D-37CF35305836}_is1'
$checks = [ordered]@{
    DeviceAddressPreserved = $old.DeviceAddress -eq $current.DeviceAddress
    DeviceNamePreserved = $old.DeviceName -eq $current.DeviceName
    EnabledPreserved = $old.Enabled -eq $current.Enabled
    LanguagePreserved = $old.Language -eq $current.Language
    OldTaskRemoved = $null -eq $oldTask
    OldProcessesRemoved = @(Get-Process -Name AwayLock,AwayLock.Agent -ErrorAction SilentlyContinue).Count -eq 0
    OneNativeAgent = @($agent).Count -eq 1
    TaskRunning = [string]$task.State -eq 'Running'
    InstalledUiVersion = (Get-Item -LiteralPath (Join-Path $installed 'AnpingLock.exe')).VersionInfo.FileVersion
    InstalledAgentVersion = (Get-Item -LiteralPath (Join-Path $installed 'AnpingLock.Agent.exe')).VersionInfo.FileVersion
    InstalledDisplayName = $entry.DisplayName
    NoResidentInterface = @(Get-Process -Name AnpingLock -ErrorAction SilentlyContinue).Count -eq 0
}
$passed = $checks.DeviceAddressPreserved -and $checks.DeviceNamePreserved -and $checks.EnabledPreserved -and $checks.LanguagePreserved -and $checks.OldTaskRemoved -and $checks.OldProcessesRemoved -and $checks.OneNativeAgent -and $checks.TaskRunning -and $checks.InstalledUiVersion -eq '1.1.0.0' -and $checks.InstalledAgentVersion -eq '1.1.0.0' -and $checks.InstalledDisplayName.Contains('AnpingLock') -and $checks.NoResidentInterface
[pscustomobject]@{ Passed=$passed; Checks=$checks; DynamicLockEnabled=(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon').EnableGoodbye } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultPath -Encoding utf8
if (-not $passed) { exit 1 }
