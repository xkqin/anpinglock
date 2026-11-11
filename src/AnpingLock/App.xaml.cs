using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace AnpingLock;

public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;
    private EventWaitHandle? showEvent;
    private EventWaitHandle? quitEvent;
    private RegisteredWaitHandle? showRegistration;
    private RegisteredWaitHandle? quitRegistration;
    private Forms.NotifyIcon? tray;
    private MainWindow? dashboard;
    private readonly SemaphoreSlim controls = new(1, 1);

    public AppSettings Settings { get; private set; } = new();
    public AgentStatusClient Monitor { get; }
    public bool Exiting { get; private set; }
    public string Text(string key) => Strings.Get(key, Settings.Language);

    public App() => Monitor = new AgentStatusClient(() => { Settings = SettingsStore.Load(); return Settings; });

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 2 && e.Args[0] == "--render-preview")
        {
            Settings = new AppSettings { Language = e.Args.Contains("--english") ? "en" : "zh-CN" };
            var preview = new MainWindow(this);
            var surface = (FrameworkElement)preview.Content;
            surface.Measure(new System.Windows.Size(978, 798));
            surface.Arrange(new Rect(0, 0, 978, 798));
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1956, 1596, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream output = File.Create(e.Args[1])) encoder.Save(output);
            ExitInterface();
            return;
        }
        string identity = StartupTask.UserId;
        // Close the old dashboard during an installed upgrade.
        Signal("Local\\AwayLock.Ui.Quit." + identity);
        string mutexName = "Local\\AnpingLock.Ui." + identity;
        string showName = "Local\\AnpingLock.Ui.Show." + identity;
        string quitName = "Local\\AnpingLock.Ui.Quit." + identity;
        Settings = SettingsStore.Load();
        if (e.Args.Contains("--resume-protection"))
        {
            try { if (Settings.Enabled) { StartupTask.Enable(); StartupTask.Run(); } Shutdown(); }
            catch (Exception exception) { LogStartupFailure(exception); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--uninstall") || e.Args.Contains("--prepare-update") || e.Args.Contains("--stop-protection"))
        {
            try
            {
                if (!e.Args.Contains("--prepare-update")) { Settings.Enabled = false; SettingsStore.Save(Settings); }
                StartupTask.Remove();
                Signal(quitName);
                await WaitForAgentExitAsync(identity);
                Shutdown();
            }
            catch (Exception exception) { LogStartupFailure(exception); Shutdown(1); }
            return;
        }
        instanceMutex = new Mutex(true, mutexName, out bool firstInstance);
        if (!firstInstance)
        {
            Signal(showName);
            instanceMutex.Dispose();
            instanceMutex = null;
            Shutdown();
            return;
        }
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showName);
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, quitName);
        showRegistration = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(ShowDashboard), null, Timeout.Infinite, false);
        quitRegistration = ThreadPool.RegisterWaitForSingleObject(quitEvent, (_, _) => Dispatcher.BeginInvoke(ExitInterface), null, Timeout.Infinite, false);
        Monitor.Changed += snapshot => RefreshTray(snapshot);
        CreateTray();
        Monitor.Start();
        if (Settings.Enabled)
        {
            try { StartupTask.Enable(); StartupTask.Run(); }
            catch (Exception exception) { ShowDashboard(); dashboard?.ShowError(exception.Message); }
        }
        ShowDashboard();
    }

    private static void Signal(string name)
    {
        try { using EventWaitHandle signal = EventWaitHandle.OpenExisting(name); signal.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    private static void LogStartupFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "startup-error.log"), DateTimeOffset.Now.ToString("o") + "\n" + exception);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static async Task WaitForAgentExitAsync(string identity)
    {
        await Task.Run(() =>
        {
            try
            {
                foreach (string prefix in new[] { "Local\\AnpingLock.Agent.", "Local\\AwayLock.Agent." })
                {
                    try
                    {
                        using Mutex running = Mutex.OpenExisting(prefix + identity);
                        try { if (running.WaitOne(TimeSpan.FromSeconds(25))) running.ReleaseMutex(); }
                        catch (AbandonedMutexException) { running.ReleaseMutex(); }
                    }
                    catch (WaitHandleCannotBeOpenedException) { }
                }
            }
            catch (WaitHandleCannotBeOpenedException) { }
        });
    }

    private void CreateTray()
    {
        tray = new Forms.NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application, Visible = true, Text = Text("productName") };
        tray.DoubleClick += (_, _) => ShowDashboard();
        RefreshTray(Monitor.Snapshot);
    }

    public void RefreshLanguage()
    {
        RefreshTray(Monitor.Snapshot);
        dashboard?.ApplyLanguage();
    }

    private void RefreshTray(ConnectionSnapshot snapshot)
    {
        if (tray == null) return;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Text("show"), null, (_, _) => ShowDashboard());
        menu.Items.Add(Text(Settings.Enabled ? "stop" : "start"), null, async (_, _) =>
        {
            if (Settings.DeviceAddress == null) { ShowDashboard(); return; }
            try { await SetMonitoringAsync(!Settings.Enabled); }
            catch (Exception exception) { ShowDashboard(); dashboard?.ShowError(exception.Message); }
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Text("exit"), null, (_, _) => ExitInterface());
        Forms.ContextMenuStrip? previous = tray.ContextMenuStrip;
        tray.ContextMenuStrip = menu;
        previous?.Dispose();
        string label = Text("productName") + " · " + Text(snapshot.State + ".title");
        tray.Text = label.Length > 63 ? label[..63] : label;
    }

    public void ShowDashboard()
    {
        if (Exiting) return;
        dashboard ??= new MainWindow(this);
        dashboard.Show();
        dashboard.WindowState = WindowState.Normal;
        dashboard.Activate();
    }

    public async Task SetMonitoringAsync(bool enabled)
    {
        await controls.WaitAsync();
        bool wasEnabled = Settings.Enabled;
        try
        {
            if (enabled)
            {
                if (Settings.DeviceAddress == null) return;
                StartupTask.Enable();
                Settings.Enabled = true;
                SettingsStore.Save(Settings);
                try { StartupTask.Run(); }
                catch
                {
                    Settings.Enabled = wasEnabled;
                    SettingsStore.Save(Settings);
                    if (!wasEnabled) StartupTask.Remove();
                    throw;
                }
            }
            else
            {
                Settings.Enabled = false;
                SettingsStore.Save(Settings);
                StartupTask.Remove();
            }
        }
        finally
        {
            controls.Release();
            Monitor.Refresh();
            dashboard?.RefreshStatus(Monitor.Snapshot);
        }
    }

    public static void OpenWindowsSettings(string page) => Process.Start(new ProcessStartInfo("ms-settings:" + page) { UseShellExecute = true });

    public void ExitInterface()
    {
        if (Exiting) return;
        Exiting = true;
        Monitor.Dispose();
        tray?.Dispose();
        tray = null;
        showRegistration?.Unregister(null);
        quitRegistration?.Unregister(null);
        showEvent?.Dispose();
        quitEvent?.Dispose();
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        instanceMutex = null;
        Shutdown();
    }
}
