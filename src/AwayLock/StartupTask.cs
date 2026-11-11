using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AwayLock;

public static class StartupTask
{
    public static string UserId => WindowsIdentity.GetCurrent().User!.Value;
    public static string Name => "AwayLock-" + UserId;

    private static dynamic Connect()
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    public static void Enable()
    {
        string agent = Path.Combine(AppContext.BaseDirectory, "AwayLock.Agent.exe");
        if (!File.Exists(agent)) throw new FileNotFoundException("Install the complete AwayLock package to enable background protection.", agent);
        dynamic service = Connect();
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "AwayLock persistent background protection, independent of its dashboard.";
        definition.Principal.UserId = UserId;
        definition.Principal.LogonType = 3;
        definition.Principal.RunLevel = 0;
        definition.Settings.MultipleInstances = 2;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.RestartCount = 3;
        definition.Settings.RestartInterval = "PT1M";
        dynamic logon = definition.Triggers.Create(9);
        logon.UserId = UserId;
        logon.Delay = "PT10S";
        dynamic unlock = definition.Triggers.Create(11);
        unlock.UserId = UserId;
        unlock.StateChange = 8;
        unlock.Delay = "PT3S";
        dynamic recovery = definition.Triggers.Create(1);
        recovery.StartBoundary = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss");
        recovery.Repetition.Interval = "PT1M";
        dynamic action = definition.Actions.Create(0);
        action.Path = agent;
        action.WorkingDirectory = AppContext.BaseDirectory;
        service.GetFolder("\\").RegisterTaskDefinition(Name, definition, 6, UserId, null, 3, null);
    }

    public static void Run() => Connect().GetFolder("\\").GetTask(Name).Run(null);

    public static void Remove()
    {
        dynamic folder = Connect().GetFolder("\\");
        try
        {
            dynamic task = folder.GetTask(Name);
            try { task.Stop(0); }
            catch (COMException exception) when ((uint)exception.HResult == 0x8004130B) { }
            folder.DeleteTask(Name, 0);
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070002) { }
    }
}
