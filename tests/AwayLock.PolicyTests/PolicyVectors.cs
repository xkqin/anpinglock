using System;
using System.IO;
using AwayLock;

static class PolicyVectors
{
    public static void Write(string path)
    {
        using var output = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        var random = new Random(184239);
        var policy = new PresencePolicy(TimeSpan.FromSeconds(60));
        long now = 0;
        for (int index = 0; index < 10000; index++)
        {
            int command = random.Next(20);
            if (command == 0) { policy = new PresencePolicy(TimeSpan.FromSeconds(60)); now = 0; output.WriteLine("reset"); continue; }
            if (command == 1) { bool locked = random.Next(2) != 0; policy.SetSessionLocked(locked); output.WriteLine($"session {(locked ? 1 : 0)}"); continue; }
            if (command == 2) { int? remaining = random.Next(5) == 0 ? null : random.Next(61); policy.RestoreArmed(remaining); output.WriteLine($"restore {remaining ?? -1}"); continue; }
            now += random.Next(40001);
            int connectedValue = random.Next(-1, 2);
            bool? connected = connectedValue < 0 ? null : connectedValue == 1;
            long idleValue = random.Next(10) == 0 ? -1 : random.Next(120001);
            TimeSpan? idle = idleValue < 0 ? null : TimeSpan.FromMilliseconds(idleValue);
            if (command < 8)
            {
                bool success = random.Next(5) != 0;
                bool? result = policy.TryLock(connected, TimeSpan.FromMilliseconds(now), idle, () => success);
                output.WriteLine($"lock {connectedValue} {now} {idleValue} {(success ? 1 : 0)} {(result == null ? -1 : result == true ? 1 : 0)}");
            }
            else
            {
                var decision = policy.Observe(connected, TimeSpan.FromMilliseconds(now), idle);
                output.WriteLine($"observe {connectedValue} {now} {idleValue} {(decision.Armed ? 1 : 0)} {(decision.ShouldLock ? 1 : 0)} {decision.SecondsRemaining ?? -1}");
            }
        }
    }
}
