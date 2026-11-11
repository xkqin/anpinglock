using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace AwayLock;

public sealed record PairedDevice(ulong Address, string Name);
public sealed record ConnectionSnapshot(string State, string DeviceName, bool Running, DateTimeOffset? CheckedAt, int Requests, string Error = "", bool Armed = false, int? LockSecondsRemaining = null, int? IdleSeconds = null);

public sealed class BluetoothMonitor : IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private PresencePolicy? policy;
    private volatile bool sessionLocked;
    private PresenceDecision decision = new(false, false, null);
    private int requests;
    private int? idleSeconds;

    public ConnectionSnapshot Snapshot { get; private set; } = new("choose", "", false, null, 0);
    public event Action<ConnectionSnapshot>? Changed;
    public Task Completion => worker ?? Task.CompletedTask;

    public static async Task<IReadOnlyList<PairedDevice>> GetPairedDevicesAsync()
    {
        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        var result = new List<PairedDevice>();
        foreach (DeviceInformation information in devices)
        {
            using BluetoothDevice? device = await BluetoothDevice.FromIdAsync(information.Id);
            if (device != null) result.Add(new PairedDevice(device.BluetoothAddress, device.Name));
        }
        return result.DistinctBy(device => device.Address).OrderBy(device => device.Name).ToArray();
    }

    public async Task StartAsync(PairedDevice device, AgentState? recovery = null)
    {
        await StopAsync();
        requests = 0;
        decision = new PresenceDecision(false, false, null);
        policy = new PresencePolicy(TimeSpan.FromSeconds(60));
        policy.SetSessionLocked(sessionLocked);
        if (recovery?.Snapshot.Armed == true && recovery.Snapshot.State is not ("locked" or "locking"))
        {
            int? remaining = recovery.Snapshot.LockSecondsRemaining;
            if (remaining != null) remaining = Math.Max(0, remaining.Value - (int)(DateTimeOffset.UtcNow - recovery.Heartbeat).TotalSeconds);
            policy.RestoreArmed(remaining);
        }
        cancellation = new CancellationTokenSource();
        worker = RunAsync(device, policy, cancellation.Token);
    }

    public void SetSessionLocked(bool value)
    {
        sessionLocked = value;
        policy?.SetSessionLocked(value);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? current = cancellation;
        if (current == null) return;
        current.Cancel();
        try { if (worker != null) await worker; }
        finally
        {
            current.Dispose();
            cancellation = null;
            worker = null;
            policy = null;
            decision = new PresenceDecision(false, false, null);
            Publish("stopped", Snapshot.DeviceName, false, Snapshot.CheckedAt);
        }
    }

    private async Task RunAsync(PairedDevice selected, PresencePolicy presence, CancellationToken token)
    {
        BluetoothDevice? phone = null;
        var services = new List<RfcommDeviceService>();
        Stopwatch clock = Stopwatch.StartNew();
        TimeSpan nextDiscovery = TimeSpan.Zero;

        void ReleaseDevice()
        {
            foreach (RfcommDeviceService service in services) service.Dispose();
            services.Clear();
            phone?.Dispose();
            phone = null;
        }

        Publish("checking", selected.Name, true, null);
        try
        {
            while (!token.IsCancellationRequested)
            {
                bool? connected = null;
                bool paired = true;
                string error = "";
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    phone ??= await BluetoothDevice.FromBluetoothAddressAsync(selected.Address).AsTask(timeout.Token).ConfigureAwait(false);
                    if (phone == null) throw new InvalidOperationException("The selected device is unavailable. Check Bluetooth in Windows Settings.");
                    paired = phone.DeviceInformation.Pairing.IsPaired;
                    if (!paired) ReleaseDevice();
                    else
                    {
                        connected = phone.ConnectionStatus == BluetoothConnectionStatus.Connected;
                        if (connected == false && clock.Elapsed >= nextDiscovery)
                        {
                            nextDiscovery = clock.Elapsed + TimeSpan.FromSeconds(60);
                            foreach (RfcommDeviceService service in services) service.Dispose();
                            services.Clear();
                            requests++;
                            RfcommDeviceServicesResult discovery = await phone.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token).ConfigureAwait(false);
                            services.AddRange(discovery.Services);
                            if (discovery.Error != BluetoothError.Success) throw new InvalidOperationException("Windows Bluetooth service discovery returned " + discovery.Error + ".");
                            connected = phone.ConnectionStatus == BluetoothConnectionStatus.Connected;
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (OperationCanceledException)
                {
                    ReleaseDevice();
                    connected = null;
                    error = "The Bluetooth request timed out.";
                }
                catch (Exception exception)
                {
                    ReleaseDevice();
                    connected = null;
                    error = exception.Message;
                }

                token.ThrowIfCancellationRequested();
                TimeSpan? idle = Workstation.IdleTime();
                idleSeconds = idle is TimeSpan duration ? (int)duration.TotalSeconds : null;
                decision = presence.Observe(connected, clock.Elapsed, idle);
                string State() => !paired ? "pair" : connected == null || idle == null ? "unknown"
                    : connected == true ? "connected" : decision.Armed && idle < TimeSpan.FromSeconds(60) ? "active"
                    : decision.Armed ? "confirming" : "waiting";
                string state = State();
                if (sessionLocked) state = "locked";
                if (decision.ShouldLock)
                {
                    AppSettings latest = SettingsStore.Load();
                    if (!latest.Enabled || latest.DeviceAddress != selected.Address) break;
                    // Bluetooth discovery can take seconds. Read both signals again before locking.
                    connected = phone?.ConnectionStatus == BluetoothConnectionStatus.Connected ? true
                        : phone != null && paired ? false : null;
                    idle = Workstation.IdleTime();
                    idleSeconds = idle is TimeSpan finalIdle ? (int)finalIdle.TotalSeconds : null;
                    if (Workstation.IsSessionLocked()) SetSessionLocked(true);
                    bool? locked = presence.TryLock(connected, clock.Elapsed, idle, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        AppSettings current = SettingsStore.Load();
                        return current.Enabled && current.DeviceAddress == selected.Address && Workstation.LockWorkStation();
                    });
                    decision = presence.Observe(connected, clock.Elapsed, idle);
                    state = locked == true ? "locking" : locked == false ? "lockfailed" : sessionLocked ? "locked" : State();
                }
                Publish(state, selected.Name, true, DateTimeOffset.Now, error);
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { ReleaseDevice(); }
    }

    private void Publish(string state, string name, bool running, DateTimeOffset? checkedAt, string error = "")
    {
        Snapshot = new ConnectionSnapshot(state, name, running, checkedAt, requests, error, decision.Armed, decision.SecondsRemaining, idleSeconds);
        Changed?.Invoke(Snapshot);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
