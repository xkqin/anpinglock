using System;

namespace AnpingLock;

public sealed record PresenceDecision(bool Armed, bool ShouldLock, int? SecondsRemaining);

public sealed class PresencePolicy
{
    private readonly object gate = new();
    private readonly TimeSpan grace;
    private bool armed;
    private bool locked;
    private bool requestIssued;
    private TimeSpan? missingSince;

    public PresencePolicy(TimeSpan gracePeriod) => grace = gracePeriod;

    public void RestoreArmed(int? remainingSeconds)
    {
        lock (gate)
        {
            if (locked) return;
            armed = true;
            requestIssued = false;
            missingSince = remainingSeconds is int seconds
                ? TimeSpan.FromSeconds(Math.Clamp(seconds, 0, (int)grace.TotalSeconds)) - grace
                : null;
        }
    }

    public void SetSessionLocked(bool value)
    {
        lock (gate)
        {
            if (locked == value) return;
            locked = value;
            armed = false;
            requestIssued = false;
            missingSince = null;
        }
    }

    public PresenceDecision Observe(bool? connected, TimeSpan elapsed, TimeSpan? idleTime)
    {
        lock (gate)
        {
            if (locked) return new PresenceDecision(false, false, null);
            if (connected == true)
            {
                armed = true;
                requestIssued = false;
                missingSince = null;
                return new PresenceDecision(true, false, null);
            }
            if (!armed || requestIssued) return new PresenceDecision(armed, false, null);
            // A failed query is not evidence of absence. Require a new confirmed interval.
            if (connected == null || idleTime == null || idleTime < TimeSpan.Zero)
            {
                missingSince = null;
                return new PresenceDecision(true, false, null);
            }
            missingSince ??= elapsed;
            // Input after the first missing observation restarts the confirmation period.
            TimeSpan lastInput = elapsed - idleTime.Value;
            if (lastInput > missingSince.Value) missingSince = lastInput;
            TimeSpan remaining = grace - (elapsed - missingSince.Value);
            bool shouldLock = remaining <= TimeSpan.Zero;
            return new PresenceDecision(true, shouldLock, Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds)));
        }
    }

    // Recheck fresh connection/input observations at the point of issuing the request.
    // Session events share this gate, so an unlock also cancels a pending decision.
    public bool? TryLock(bool? connected, TimeSpan elapsed, TimeSpan? idleTime, Func<bool> requestLock)
    {
        lock (gate)
        {
            if (!Observe(connected, elapsed, idleTime).ShouldLock) return null;
            requestIssued = true;
            bool succeeded = requestLock();
            if (!succeeded) ReportLockFailure(elapsed);
            return succeeded;
        }
    }

    public void ReportLockFailure(TimeSpan elapsed)
    {
        lock (gate)
        {
            requestIssued = false;
            missingSince = elapsed;
        }
    }
}
