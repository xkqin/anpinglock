# Implementation / 实现说明

## Components

| Component | Responsibility |
| --- | --- |
| `src/AnpingLock` | WPF dashboard, tray controls, settings, scheduled-task registration |
| `src/AnpingLock.NativeAgent` | Native C++ user-session agent, Bluetooth lifetime, heartbeat, session events |
| `src/AnpingLock.Core/BluetoothMonitor.cs` | Paired-device enumeration for the UI; retained managed monitor reference |
| `src/AnpingLock.NativeAgent/PresencePolicy.h` | Production arming, monotonic countdown, reconnect reset, idle and lock/unlock rules |
| `src/AnpingLock.Core/PresencePolicy.cs` | Managed reference policy used for native parity checks |
| `src/AnpingLock.Core/AgentStateStore.cs` | Atomic local heartbeat checkpoint |
| `src/AnpingLock/StartupTask.cs` | Sign-in, unlock and one-minute recovery triggers |
| `installer/AnpingLock.iss` | Per-user installation, update preparation and uninstall cleanup |

## Persistent protection

The user's saved `Enabled` flag expresses the protection intent. The dashboard registers a task whose executable is `AnpingLock.Agent.exe`, saves the enable flag, and starts that task immediately. The UI process can then terminate completely.

The native agent caches settings. `SettingsStore.Save` signals a per-user named event, waking it immediately for changes; a one-minute fallback also reads externally edited settings. It writes a heartbeat at most every 30 seconds while stable, and immediately on meaningful state changes, with its process ID, device, Windows logon-session identifier, uptime and snapshot. The dashboard accepts a live heartbeat up to 75 seconds old and validates the agent process. Closing the dashboard releases its entire .NET/WPF process; the agent has no CLR or forms dependency. The former `src/AnpingLock.Agent` is retained as a legacy implementation and is not packaged.

The recovery trigger repeats indefinitely every minute. Task Scheduler ignores overlapping starts. The agent also owns a per-user mutex, which covers an additional manual start. Stopping protection writes `Enabled=false` before stopping and deleting the task. The agent checks that flag again on the path that requests a Windows lock.

## Connection lifetime

Paired devices are enumerated through `DeviceInformation.FindAllAsync` and `BluetoothDevice.GetDeviceSelectorFromPairingState(true)`. The selected address is stored in the user's profile. The agent creates its own `BluetoothDevice` and keeps returned `RfcommDeviceService` objects alive between checks.

An uncached `GetRfcommServicesAsync` request can encourage Windows and the phone to establish an active Bluetooth session. The status shown by AnpingLock comes from `BluetoothDevice.ConnectionStatus`. A completed service query and a connected state are evaluated separately. `ConnectionStatusChanged` wakes the worker without a polling loop. Stable connected states otherwise wake every 30 seconds; absence checks run every five seconds. Session lock/unlock notifications come through a hidden native Win32 window. Service discovery is suspended while Windows is locked.

Each Bluetooth request has a 20-second cancellation timeout. Explicit service-discovery attempts are limited to one per minute. Request latency affects the time at which a changed state is observed. The worker uses blocking OS waits between events, not a busy loop. Normal Bluetooth fluctuations do not append activity logs; lock requests and errors remain recorded with a 1 MiB rotation limit.

## Arming and recovery

The presence policy arms after an observed connection. Continuous confirmed absence then consumes a 60-second grace period. `GetLastInputInfo` also requires 60 seconds without keyboard/mouse input; new input moves the confirmation baseline forward. Reconnecting clears it. A Bluetooth query error or unavailable idle reading resets confirmation, requiring a fresh interval. `tryLock` reevaluates fresh connection and idle observations and serializes the request with session events. Windows lock and unlock events clear the armed baseline, and a new observed connection arms the next unlocked session. A generation counter also invalidates a baseline when an entire lock/unlock cycle happens during an asynchronous Bluetooth call. Recovery uses the same logon-session ID and a monotonic uptime gap below 100 seconds.

After an unexpected agent restart, an armed checkpoint is eligible for recovery when its device matches, its Windows logon-session identifier matches, its uptime belongs to the current boot, and its heartbeat is recent. An ongoing absence resumes with the remaining time adjusted for the recovery gap. A locked session stays disarmed.

`LockWorkStation` requests a lock of the current interactive Windows session. Authentication afterward is handled by Windows. Windows Dynamic Lock has its own detection and locking policy and can operate alongside AnpingLock.

## 界面退出与后台持续运行

用户点击开始后，界面保存 `Enabled=true`，将 `AnpingLock.Agent.exe` 注册到当前用户的计划任务中并立即启动。界面与守护进程分别持有自己的互斥锁，因此退出窗口和托盘会结束界面进程，后台守护进程继续运行。

计划任务包含登录、解锁、失败恢复和每分钟重复的触发器。重复触发时使用 `IgnoreNew`，守护进程另有互斥锁避免重复实例。“停止保护”先写入停用状态，再停止和删除任务。后台即使遇到任务触发竞态，也会读到停用状态并结束。

原生守护进程稳定状态下每 30 秒更新心跳，状态变化时立即更新。连接事件、登录会话事件和设置变更会唤醒后台，其余时间通过系统等待休眠；断开确认阶段每 5 秒检查空闲状态。锁屏期间不发起蓝牙服务发现。界面重新打开后读取心跳，并检查对应进程是否仍然存在。退出界面后只保留原生后台。

## 锁屏策略

策略以 Windows 报告的连接状态和 `GetLastInputInfo` 空闲时间为依据。首次连接后进入保护状态，持续确认断开 60 秒、且键鼠连续空闲满 60 秒才请求锁屏；操作键鼠会重置确认，中途重连清空计时。蓝牙查询失败、取消配对或无法读取空闲时间时清空确认，恢复后重新计时。发出请求前再读取连接和空闲状态，并在策略锁内重新检查。正常倒计时使用单调时钟。解锁后重新等待一次手机连接。

意外重启时，近期、同一 Windows 登录会话的心跳可以用于恢复保护状态。恢复正在进行的断开确认时，会扣除进程恢复所消耗的时间。锁屏前再次读取启用状态和目标设备，防止已经停止或切换设备后仍按旧目标发起锁屏。

## Upgrade and console behavior

The installer keeps the original AppId, extracts the new GUI helper for cleanup, migrates preferences only when the new settings file is absent, and starts the new native agent. Both the UI and the agent also migrate once at startup so the actual interactive-user data view is handled correctly. Old countdown checkpoints are not imported. A compatibility mutex prevents a legacy agent running concurrently. Missing-task cleanup is idempotent.

The installed scheduled task executes AnpingLock.Agent.exe directly. Both the native agent (WIN32 subsystem) and the on-demand WPF helper (WinExe) run without a console; PowerShell is not part of the installed background launch path.
