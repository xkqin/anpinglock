using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AwayLock;

public partial class MainWindow : Window
{
    private readonly App app;
    private readonly ObservableCollection<string> activity = new();
    private bool loading;
    private bool updatingControls;
    private bool changingConnection;
    private string previousActivity = "";

    public MainWindow(App application)
    {
        app = application;
        InitializeComponent();
        ActivityList.ItemsSource = activity;
        app.Monitor.Changed += OnSnapshot;
        ApplyLanguage();
    }

    public void ApplyLanguage()
    {
        TaglineText.Text = app.Text("tagline");
        HeadingText.Text = app.Text("heading");
        IntroText.Text = app.Text("intro");
        AppRunningText.Text = app.Text("appRunning");
        DeviceLabel.Text = app.Text("deviceTitle");
        DevicePlaceholder.Text = app.Text("deviceHint");
        RefreshButton.Content = app.Text("refresh");
        CheckedLabel.Text = app.Text("checked");
        RequestsLabel.Text = app.Text("requests");
        IntervalLabel.Text = app.Text("interval");
        IntervalValue.Text = app.Text("intervalValue");
        ActivityLabel.Text = app.Text("activity");
        ActivityEmptyText.Text = app.Text("activityEmpty");
        StartupText.Text = app.Text("startup");
        StartupText.ToolTip = app.Text("startupHint");
        BluetoothSettingsButton.Content = app.Text("bluetoothSettings");
        DynamicTitle.Text = app.Text("dynamicTitle");
        DynamicBody.Text = app.Text("dynamicBody");
        DynamicButton.Content = app.Text("dynamicButton");
        FooterText.Text = app.Text("footer");
        LanguageButton.Content = app.Settings.Language == "zh-CN" ? "EN" : "中文";
        ToolTip = null;
        RefreshStatus(app.Monitor.Snapshot);
    }

    public void RefreshStatus(ConnectionSnapshot snapshot)
    {
        StateTitleText.Text = app.Text(snapshot.State + ".title");
        StateBodyText.Text = snapshot.State == "confirming"
            ? string.Format(app.Text("confirming.body"), snapshot.LockSecondsRemaining ?? 60)
            : app.Text(snapshot.State + ".body");
        CheckedValue.Text = snapshot.CheckedAt?.ToString("HH:mm:ss") ?? "—";
        RequestsValue.Text = snapshot.Requests.ToString();
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(snapshot.Armed ? "#5EE3AD" : snapshot.Running ? "#F1C679" : "#83B4FF"));
        StartButton.Content = app.Text(snapshot.Running ? "stop" : "start");
        StartButton.IsEnabled = !changingConnection && app.Settings.DeviceAddress != null;
        DevicePlaceholder.Visibility = DeviceCombo.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSnapshot(ConnectionSnapshot snapshot)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            RefreshStatus(snapshot);
            string signature = snapshot.State + "|" + snapshot.Error;
            if (signature == previousActivity || snapshot.State == "checking") return;
            previousActivity = signature;
            string line = DateTime.Now.ToString("HH:mm:ss") + "   " + app.Text(snapshot.State + ".title");
            if (snapshot.Error.Length != 0) line += "\n" + snapshot.Error;
            activity.Insert(0, line);
            ActivityEmptyText.Visibility = Visibility.Collapsed;
            while (activity.Count > 20) activity.RemoveAt(activity.Count - 1);
        });
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await LoadDevicesAsync();
    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadDevicesAsync();

    private async Task LoadDevicesAsync()
    {
        if (loading) return;
        loading = true;
        RefreshButton.IsEnabled = false;
        DeviceCombo.IsEnabled = false;
        try
        {
            IReadOnlyList<PairedDevice> devices = await BluetoothMonitor.GetPairedDevicesAsync();
            updatingControls = true;
            DeviceCombo.ItemsSource = devices;
            if (app.Settings.DeviceAddress is ulong address) DeviceCombo.SelectedItem = devices.FirstOrDefault(device => device.Address == address);
            if (devices.Count == 0) DevicePlaceholder.Text = app.Text("noDevices");
        }
        catch (Exception exception) { ShowError(app.Text("deviceError") + "\n\n" + exception.Message); }
        finally
        {
            updatingControls = false;
            loading = false;
            RefreshButton.IsEnabled = true;
            DeviceCombo.IsEnabled = true;
            RefreshStatus(app.Monitor.Snapshot);
        }
    }

    private async void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingControls || DeviceCombo.SelectedItem is not PairedDevice device) return;
        app.Settings.DeviceAddress = device.Address;
        app.Settings.DeviceName = device.Name;
        try
        {
            SettingsStore.Save(app.Settings);
            if (app.Settings.Enabled) await ChangeConnectionAsync(true);
        }
        catch (Exception exception) { ShowError(exception.Message); }
        RefreshStatus(app.Monitor.Snapshot);
    }

    private async void OnStartStop(object sender, RoutedEventArgs e) => await ChangeConnectionAsync(!app.Settings.Enabled);

    private async Task ChangeConnectionAsync(bool start)
    {
        if (changingConnection) return;
        changingConnection = true;
        DeviceCombo.IsEnabled = false;
        RefreshStatus(app.Monitor.Snapshot);
        try { await app.SetMonitoringAsync(start); }
        catch (Exception exception) { ShowError(exception.Message); }
        finally
        {
            changingConnection = false;
            DeviceCombo.IsEnabled = true;
            RefreshStatus(app.Monitor.Snapshot);
        }
    }

    private void OnLanguage(object sender, RoutedEventArgs e)
    {
        app.Settings.Language = app.Settings.Language == "zh-CN" ? "en" : "zh-CN";
        try { SettingsStore.Save(app.Settings); app.RefreshLanguage(); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void OnBluetoothSettings(object sender, RoutedEventArgs e)
    {
        try { App.OpenWindowsSettings("bluetooth"); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void OnDynamicLockSettings(object sender, RoutedEventArgs e)
    {
        try { App.OpenWindowsSettings("signinoptions"); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    public void ShowError(string message) => System.Windows.MessageBox.Show(this, message, app.Text("error"), MessageBoxButton.OK, MessageBoxImage.Information);
    private void OnMinimize(object sender, RoutedEventArgs e) => Hide();
    private void OnClose(object sender, RoutedEventArgs e) => app.ExitInterface();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!app.Exiting) { e.Cancel = true; Dispatcher.BeginInvoke(app.ExitInterface); }
    }
    private void OnDragHeader(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); }
}
