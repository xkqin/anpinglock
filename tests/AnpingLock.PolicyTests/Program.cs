using System;
using AnpingLock;

static PresencePolicy NewPolicy() => new(TimeSpan.FromSeconds(60));
static TimeSpan At(int seconds) => TimeSpan.FromSeconds(seconds);
static PresenceDecision Observe(PresencePolicy policy, bool? connected, int seconds, int? idle = 3600)
    => policy.Observe(connected, At(seconds), idle is int value ? At(value) : null);
static bool? TryLock(PresencePolicy policy, bool? connected, int seconds, int? idle = 3600, bool success = true)
    => policy.TryLock(connected, At(seconds), idle is int value ? At(value) : null, () => success);
int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine("PASS " + description);
}

var policy = NewPolicy();
Check(!Observe(policy, false, 0).Armed && !Observe(policy, false, 3600).ShouldLock, "Starting without a phone connection stays disarmed");
Check(Observe(policy, true, 3601).Armed, "An observed connection arms auto-lock");
Check(Observe(policy, false, 3602).SecondsRemaining == 60, "First disconnection begins the full grace period");
Check(!Observe(policy, false, 3661).ShouldLock, "A 59-second disconnection keeps the grace period active");
Check(Observe(policy, false, 3662).ShouldLock && TryLock(policy, false, 3662) == true, "A continuous 60-second disconnection and idle period requests a lock");
Check(!Observe(policy, false, 4000).ShouldLock && TryLock(policy, false, 4000) == null, "The same absence produces one lock request");

policy = NewPolicy();
Observe(policy, true, 0);
Observe(policy, false, 10);
Observe(policy, true, 65);
Check(Observe(policy, false, 70).SecondsRemaining == 60, "Reconnection clears the previous absence timer");
policy.SetSessionLocked(true);
Check(!Observe(policy, true, 80).Armed, "A locked session stays disarmed even when the phone connects");
policy.SetSessionLocked(false);
Check(!Observe(policy, false, 1000).ShouldLock, "Unlocking requires a new connection before another lock");
Observe(policy, true, 1001);
Observe(policy, false, 1002);
Check(Observe(policy, false, 1062).ShouldLock, "A new connection after unlock starts a new protected session");
Check(TryLock(policy, false, 1062, success: false) == false, "Windows lock failure is reported");
Check(!Observe(policy, false, 1063).ShouldLock && Observe(policy, false, 1063).SecondsRemaining == 59, "A failed Windows lock request starts a new grace period");

policy = NewPolicy();
policy.RestoreArmed(15);
Check(Observe(policy, false, 0).SecondsRemaining == 15, "A recovered background agent preserves the remaining grace period");
Check(Observe(policy, false, 15).ShouldLock, "Background recovery retains an armed absence");
policy = NewPolicy();
policy.SetSessionLocked(true);
policy.RestoreArmed(0);
Check(!Observe(policy, false, 100).ShouldLock, "Recovery stays disarmed while the Windows session is locked");

policy = NewPolicy();
Observe(policy, true, 0);
Observe(policy, false, 1, 0);
Check(!Observe(policy, false, 300, 0).ShouldLock && Observe(policy, false, 300, 0).SecondsRemaining == 60, "Continuous keyboard/mouse use prevents locking despite prolonged Bluetooth disconnection");
Check(!Observe(policy, false, 359, 59).ShouldLock, "Stopping input requires a full 60-second idle period");
Check(Observe(policy, false, 360, 60).ShouldLock, "A disconnected phone locks after the user has been idle for 60 seconds");
int lockCalls = 0;
bool? InputBeforeCommit(int? idle) => policy.TryLock(false, At(360), idle is int value ? At(value) : null, () => { lockCalls++; return true; });
Check(InputBeforeCommit(0) == null && lockCalls == 0, "Input arriving just before the lock request cancels it");
Check(Observe(policy, false, 361, 1).SecondsRemaining == 59, "Final-check input restarts the absence timer");
Check(InputBeforeCommit(null) == null && lockCalls == 0, "An unavailable input reading cannot issue a Windows lock");

policy = NewPolicy();
Observe(policy, true, 0);
Observe(policy, false, 1);
Check(!Observe(policy, null, 61).ShouldLock && Observe(policy, null, 61).SecondsRemaining == null, "Bluetooth query errors never count as confirmed absence");
Check(Observe(policy, false, 62).SecondsRemaining == 60, "Recovery from a query error starts a fresh confirmation period");
Check(TryLock(policy, true, 122) == null && !Observe(policy, false, 123).ShouldLock, "Reconnection at the final check cancels a pending lock");
Check(!Observe(policy, false, 300, null).ShouldLock, "Missing input information cancels an ongoing confirmation");
Check(Observe(policy, false, 301).SecondsRemaining == 60, "Input-query recovery starts a fresh confirmation period");
Check(Observe(policy, false, 361).ShouldLock, "Confirmed absence still works after an unknown-state interval");
Check(TryLock(policy, null, 361) == null, "A Bluetooth error at the final check cancels a pending lock");

policy = NewPolicy();
policy.RestoreArmed(0);
Check(!Observe(policy, false, 0, 5).ShouldLock && Observe(policy, false, 0, 5).SecondsRemaining == 55, "Agent recovery cannot lock over recent keyboard/mouse input");
Check(Observe(policy, false, 55, 60).ShouldLock, "Recovered absence waits for the remaining idle interval");
policy.SetSessionLocked(true);
policy.SetSessionLocked(false);
Check(TryLock(policy, false, 55, 60) == null, "A session lock/unlock between observation and commit cancels the pending lock");
Console.WriteLine($"All {checks} presence-policy checks passed.");
if (args.Length == 2 && args[0] == "--export-vectors") PolicyVectors.Write(args[1]);
