using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AnpingLock;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string userId = WindowsIdentity.GetCurrent().User!.Value;
        using var mutex = new Mutex(true, "Local\\AnpingLock.Agent." + userId, out bool owner);
        if (!owner) return;
        using var context = new Forms.ApplicationContext();
        using var dispatch = new Forms.Control();
        _ = dispatch.Handle;
        var monitor = new BluetoothMonitor();
        monitor.SetSessionLocked(Workstation.IsSessionLocked());
        SessionSwitchEventHandler sessionHandler = (_, args) =>
        {
            if (args.Reason == SessionSwitchReason.SessionLock) monitor.SetSessionLocked(true);
            if (args.Reason == SessionSwitchReason.SessionUnlock) monitor.SetSessionLocked(false);
        };
        SystemEvents.SessionSwitch += sessionHandler;
        _ = RunAsync(monitor).ContinueWith(task =>
        {
            if (task.Exception != null)
            {
                Environment.ExitCode = 1;
                try
                {
                    Directory.CreateDirectory(SettingsStore.DirectoryPath);
                    File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "agent-error.log"), DateTimeOffset.Now.ToString("o") + "\n" + task.Exception.GetBaseException());
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            dispatch.BeginInvoke((Action)context.ExitThread);
        }, TaskScheduler.Default);
        Forms.Application.Run(context);
        SystemEvents.SessionSwitch -= sessionHandler;
        mutex.ReleaseMutex();
    }

    private static async Task RunAsync(BluetoothMonitor monitor)
    {
        string logon = Workstation.LogonSessionId();
        ulong? selected = null;
        AgentState? previous = AgentStateStore.Read();
        try
        {
            while (true)
            {
                AppSettings settings = SettingsStore.Load();
                if (!settings.Enabled || settings.DeviceAddress is not ulong address) break;
                if (monitor.Completion.IsFaulted) await monitor.Completion.ConfigureAwait(false);
                if (selected != address || monitor.Completion.IsCompleted)
                {
                    AgentState? recovery = previous != null && previous.DeviceAddress == address && logon.Length > 0 && previous.LogonSession == logon
                        && previous.SystemUptime <= Environment.TickCount64 && DateTimeOffset.UtcNow >= previous.Heartbeat && DateTimeOffset.UtcNow - previous.Heartbeat < TimeSpan.FromSeconds(100)
                        ? previous : null;
                    await monitor.StartAsync(new PairedDevice(address, settings.DeviceName), recovery).ConfigureAwait(false);
                    selected = address;
                    previous = null;
                }
                AgentStateStore.Write(new AgentState(Environment.ProcessId, address, DateTimeOffset.UtcNow, logon, Environment.TickCount64, monitor.Snapshot));
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }
        finally
        {
            await monitor.DisposeAsync();
            if (selected is ulong address) AgentStateStore.Write(new AgentState(Environment.ProcessId, address, DateTimeOffset.UtcNow, logon, Environment.TickCount64, monitor.Snapshot));
        }
    }
}
