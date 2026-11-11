# Local validation — 2026-11-11 (Asia/Shanghai)

## Version 1.0.2: native low-resource background agent

Replaced the packaged .NET/Windows Forms background agent with a native C++/WinRT executable. The WPF interface remains on demand and exits completely when closed. Native connection/session/settings events wake the worker; stable states otherwise sleep for 30 seconds, absence confirmation checks idle time every five seconds, and locked sessions skip service discovery. Stable heartbeats changed from two to 30 seconds. Settings are cached and signaled, with a one-minute fallback. Normal Bluetooth fluctuations no longer append logs.

Measured on this computer without trimming the working set or forcing collection:

| Measurement | Previous managed agent | Native agent |
| --- | --- | --- |
| Executable size | 78,001,632 bytes | approximately 388 KB |
| Resident working set | 112.83 MiB in the 30-second baseline | 13.75 MiB in a 60-second locked-idle sample |
| Private committed memory | 52.91 MiB | 2.13 MiB |
| Sample CPU time | 31.25 ms / 30.02 s | 0–15.625 ms / approximately 60.54 s across two samples |
| Process write operations / payload | 16 / 5,467 bytes in 30 s | 2 / 686 bytes in 60 s |
| Loaded CLR | yes | no |

These CPU/IO samples describe the observed conditions, not a hard limit. The new production-agent samples were taken while Windows was locked. A separate hardware probe using the same production WinRT/file helpers successfully queried the selected paired phone, retained its Bluetooth objects/services for 30 seconds, and measured 14.23 MiB peak sampled working set and 2.34 MiB private memory. It issued no lock requests. A foreground WPF window has additional temporary memory use; the background-only figures do not describe an open dashboard.

Validation completed:

- 31 managed policy checks passed. Native production policy matched 8,583 generated reference decisions, including actual callback counts, connection errors, input near the lock boundary, recovery and session transitions.
- Native build and the full Windows package build passed without compiler warnings. Both installed executables report 1.0.2.0 and match packaged outputs by SHA-256.
- Manually starting another agent left one instance and the original PID.
- Settings-event shutdown was tested from a normal interactive-user scheduled process, matching the agent's launch context; it responded in approximately 215 ms in the initial successful run. Original settings were restored and the protection task restarted. Final confirmation is in `artifacts/native-settings-final-validation.json`.
- Windows Dynamic Lock remains enabled as requested. Temporary diagnostic tasks were removed. Protection remains registered, and the interface is not resident.

Evidence: `artifacts/resources-before.json`, `artifacts/resources-after.json`, `artifacts/resources-after-final.json`, `artifacts/native-bluetooth-validation.json`, `artifacts/native-final-state.json`, `artifacts/build-1.0.2-final.log`, and `artifacts/install-1.0.2-final.log`. Resource sampling can be repeated with `scripts/measure-agent.ps1`.

These checks were performed locally before publishing v1.0.2. Native API/hardware queries and policy parity have been checked; a deliberate physical walk-away, return and real locking cycle was not performed. Public release assets include a device-free validation summary; raw local diagnostics are not published.

## Version 1.0.1: prevent locks during PC use

The previous policy armed after a Bluetooth connection and requested a lock after 60 seconds of disconnection, regardless of keyboard/mouse activity. Bluetooth request failures could also consume the absence interval. Closing the interface intentionally leaves the independent agent running.

The updated policy requires both confirmed disconnection and 60 seconds without keyboard/mouse input. Input moves the confirmation baseline forward. Unknown Bluetooth/input information clears the countdown. Connection, idle time, session state, cancellation and saved settings are checked again before issuing a lock request.

## Checks performed

- All 31 automated presence-policy checks passed, including active use throughout a prolonged disconnection, input immediately before issuing a lock, query errors, unavailable idle readings, reconnects, session events, failed lock requests, duplicate suppression and agent recovery.
- Both Windows projects built without warnings or errors. `scripts/build.ps1` produced the self-contained executables and `artifacts/AwayLock-Setup-1.0.1-win-x64.exe`. Runtime/SDK license metadata is included in the installer; download dependencies are now collected as well as ordinary package libraries.
- At 14:50:02, the actual `GetLastInputInfo` result was 0.313 seconds idle. Feeding that real reading into a simulated 120-second Bluetooth absence returned `ShouldLock=false`. This check did not call `LockWorkStation`. Evidence: `artifacts/native-idle-validation.json`.
- The local installer completed with exit code 0. Both installed executables report version 1.0.1.0 and match the build outputs by SHA-256. Original selected-device settings and enabled intent were retained; the old settings, heartbeat and executables were backed up under `%LOCALAPPDATA%\AwayLock\backup\idle-fix-20261111-144142`.
- At 14:51:54, exiting the interface left exactly one agent, with the same PID and an advancing heartbeat. Its scheduled task remained running. The interface was subsequently reopened. Evidence: `artifacts/runtime-validation.json` and `artifacts/install-1.0.1.log`.
- A later live heartbeat at 14:53:13 showed `State=active`, `Armed=true`, and `IdleSeconds=10`, with one background agent and no interface process. The phone had connected and protection had rearmed; a subsequent Bluetooth disconnection was being postponed because of real keyboard/mouse activity. A follow-up snapshot is saved in `artifacts/runtime-followup.json`.

## Independent Windows locking and remaining limits

Windows Dynamic Lock was enabled (`EnableGoodbye=1`) before the update and was deliberately retained at the user's request. The new agent began at 14:50:42, reported waiting for its first connection at 14:51:01, and observed a session lock at 14:51:35 while unarmed. The new AwayLock policy did not request that lock. Windows Dynamic Lock or another Windows locking source can act independently; no matching Security 4800 audit event was available to identify the initiating source.

This was a local update, not a GitHub release. No deliberate physical walk-away/reconnection cycle or real `LockWorkStation` request was performed as part of validation. Windows can report a nearby phone disconnected because of phone power management or Bluetooth profile behavior; the update guards against locking during active keyboard/mouse use and does not turn Bluetooth connection state into a physical distance measurement. After an unlock or a fresh start, a phone connection must be observed before protection arms.
