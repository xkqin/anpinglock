using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace AwayLock;

public sealed class AgentStatusClient : IDisposable
{
    private readonly Func<AppSettings> settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    public ConnectionSnapshot Snapshot { get; private set; } = new("choose", "", false, null, 0);
    public event Action<ConnectionSnapshot>? Changed;

    public AgentStatusClient(Func<AppSettings> getSettings)
    {
        settings = getSettings;
        timer.Tick += (_, _) => Refresh();
    }

    public void Start() { Refresh(); timer.Start(); }

    public void Refresh()
    {
        ConnectionSnapshot previous = Snapshot;
        AppSettings current = settings();
        AgentState? state = AgentStateStore.Read();
        if (!current.Enabled)
        {
            Snapshot = new ConnectionSnapshot(current.DeviceAddress == null ? "choose" : "stopped", current.DeviceName, false, state?.Snapshot.CheckedAt, state?.Snapshot.Requests ?? 0);
        }
        else if (state != null && state.DeviceAddress == current.DeviceAddress && DateTimeOffset.UtcNow - state.Heartbeat < TimeSpan.FromSeconds(75) && IsAgentAlive(state.ProcessId)) Snapshot = state.Snapshot;
        else Snapshot = new ConnectionSnapshot("starting", current.DeviceName, true, state?.Snapshot.CheckedAt, 0);
        if (Snapshot != previous) Changed?.Invoke(Snapshot);
    }

    private static bool IsAgentAlive(int processId)
    {
        try { using Process process = Process.GetProcessById(processId); return !process.HasExited && process.ProcessName == "AwayLock.Agent"; }
        catch (ArgumentException) { return false; }
    }

    public void Dispose() => timer.Stop();
}
