// Exercise the exact production WinRT/file helpers without issuing a lock request.
#define wWinMain UnusedProductionEntry
#include "../../src/AnpingLock.NativeAgent/main.cpp"
#undef wWinMain
#include <psapi.h>

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    init_apartment(apartment_type::multi_threaded);
    JsonObject result;
    try {
        auto settings = loadSettings(dataDirectory());
        auto phone = awaitResult(BluetoothDevice::FromBluetoothAddressAsync(settings.address));
        if (!phone) throw hresult_error(E_FAIL, L"Selected device is unavailable");
        result.Insert(L"Paired", JsonValue::CreateBooleanValue(phone.DeviceInformation().Pairing().IsPaired()));
        auto discovery = awaitResult(phone.GetRfcommServicesAsync(BluetoothCacheMode::Uncached));
        result.Insert(L"DiscoveryError", JsonValue::CreateNumberValue(static_cast<int>(discovery.Error())));
        result.Insert(L"ServiceCount", JsonValue::CreateNumberValue(discovery.Services().Size()));
        DWORD handles = 0; GetProcessHandleCount(GetCurrentProcess(), &handles);
        result.Insert(L"HandlesAfterDiscovery", JsonValue::CreateNumberValue(handles));
        // Retain the device/services exactly as the agent does, and sample the loaded Bluetooth path.
        SIZE_T peak = 0;
        for (int index = 0; index < 6; ++index) {
            PROCESS_MEMORY_COUNTERS_EX memory{}; memory.cb = sizeof(memory);
            check_bool(GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory), sizeof(memory)));
            peak = std::max(peak, memory.WorkingSetSize);
            result.Insert(L"WorkingSetMiB", JsonValue::CreateNumberValue(memory.WorkingSetSize / 1048576.0));
            result.Insert(L"PrivateMiB", JsonValue::CreateNumberValue(memory.PrivateUsage / 1048576.0));
            result.Insert(L"Connected", JsonValue::CreateBooleanValue(phone.ConnectionStatus() == BluetoothConnectionStatus::Connected));
            Sleep(5000);
        }
        result.Insert(L"PeakSampledWorkingSetMiB", JsonValue::CreateNumberValue(peak / 1048576.0));
        result.Insert(L"LockRequests", JsonValue::CreateNumberValue(0));
        result.Insert(L"Passed", JsonValue::CreateBooleanValue(true));
        for (auto const& service : discovery.Services()) service.Close();
        phone.Close();
    } catch (hresult_error const& error) {
        result.Insert(L"Passed", JsonValue::CreateBooleanValue(false));
        result.Insert(L"Error", JsonValue::CreateStringValue(error.message()));
    }
    atomicWrite(fs::path(argv[1]), result);
    return result.GetNamedBoolean(L"Passed") ? 0 : 1;
}
