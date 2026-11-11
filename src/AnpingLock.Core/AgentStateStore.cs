using System;
using System.IO;
using System.Text.Json;

namespace AnpingLock;

public sealed record AgentState(int ProcessId, ulong DeviceAddress, DateTimeOffset Heartbeat, string LogonSession, long SystemUptime, ConnectionSnapshot Snapshot);

public static class AgentStateStore
{
    private static string previousEvent = "";
    public static string FilePath => Path.Combine(SettingsStore.DirectoryPath, "agent-state.json");

    public static AgentState? Read()
    {
        try
        {
            AgentState? state = JsonSerializer.Deserialize<AgentState>(File.ReadAllText(FilePath));
            return state?.Snapshot == null ? null : state;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Write(AgentState state)
    {
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, FilePath, true);
        string signature = state.Snapshot.State + "|" + state.Snapshot.Error;
        if (signature == previousEvent) return;
        previousEvent = signature;
        try
        {
            string log = Path.Combine(SettingsStore.DirectoryPath, "agent-events.jsonl");
            if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024) File.Move(log, log + ".previous", true);
            File.AppendAllText(log, JsonSerializer.Serialize(new
            {
                At = state.Heartbeat, state.Snapshot.State, state.Snapshot.Armed,
                state.Snapshot.IdleSeconds, state.Snapshot.LockSecondsRemaining, state.Snapshot.Error
            }) + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
