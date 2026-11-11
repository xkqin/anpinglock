#include <windows.h>
#include <wtsapi32.h>
#include <sddl.h>
#include <shlobj.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Devices.Bluetooth.h>
#include <winrt/Windows.Devices.Bluetooth.Rfcomm.h>
#include <winrt/Windows.Devices.Enumeration.h>
#include <atomic>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <thread>
#include <vector>
#include "PresencePolicy.h"

using namespace winrt;
using namespace Windows::Data::Json;
using namespace Windows::Devices::Bluetooth;
using namespace Windows::Devices::Bluetooth::Rfcomm;
using namespace anpinglock;
namespace fs = std::filesystem;

struct Session {
    std::mutex gate;
    bool locked{};
    uint64_t generation{};
    handle wake{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
    std::atomic_bool stopping{};
};

static bool sessionLocked() {
    LPWSTR buffer = nullptr; DWORD length = 0;
    if (WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, WTS_CURRENT_SESSION, WTSSessionInfoEx, &buffer, &length)) {
        auto info = reinterpret_cast<WTSINFOEXW*>(buffer);
        bool locked = info->Level == 1 && info->Data.WTSInfoExLevel1.SessionFlags == WTS_SESSIONSTATE_LOCK;
        WTSFreeMemory(buffer);
        return locked;
    }
    HDESK desktop = OpenInputDesktop(0, FALSE, DESKTOP_READOBJECTS);
    if (!desktop) return true;
    CloseDesktop(desktop);
    return false;
}

static std::optional<int64_t> idleTime() {
    LASTINPUTINFO input{sizeof(input)};
    if (!GetLastInputInfo(&input)) return {};
    int32_t elapsed = static_cast<int32_t>(static_cast<DWORD>(GetTickCount64()) - input.dwTime);
    return std::max<int64_t>(0, elapsed);
}

static std::wstring userSid() {
    HANDLE raw = nullptr; check_bool(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &raw));
    handle token{raw}; DWORD bytes = 0;
    GetTokenInformation(token.get(), TokenUser, nullptr, 0, &bytes);
    std::vector<BYTE> data(bytes);
    check_bool(GetTokenInformation(token.get(), TokenUser, data.data(), bytes, &bytes));
    LPWSTR sid = nullptr; check_bool(ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(data.data())->User.Sid, &sid));
    std::wstring result(sid); LocalFree(sid); return result;
}

static std::wstring logonSession() {
    HANDLE raw = nullptr; check_bool(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &raw));
    handle token{raw}; TOKEN_STATISTICS stats{}; DWORD bytes = 0;
    check_bool(GetTokenInformation(token.get(), TokenStatistics, &stats, sizeof(stats), &bytes));
    wchar_t value[17]; swprintf_s(value, L"%08X%08X", stats.AuthenticationId.HighPart, stats.AuthenticationId.LowPart);
    return value;
}

static fs::path dataDirectory() {
    PWSTR path = nullptr; check_hresult(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &path));
    fs::path root(path); CoTaskMemFree(path);
    fs::path result = root / L"AnpingLock";
    fs::create_directories(result);
    // Also migrate from the actual interactive task context (packaged launchers may redirect AppData).
    auto preferences = result / L"settings.json";
    auto legacy = root / L"AwayLock" / L"settings.json";
    if (!fs::exists(preferences) && fs::exists(legacy)) {
        if (!CopyFileW(legacy.c_str(), preferences.c_str(), TRUE) && GetLastError() != ERROR_FILE_EXISTS) throw_last_error();
    }
    return result;
}

static hstring timestamp() {
    SYSTEMTIME now; GetSystemTime(&now); wchar_t value[32];
    swprintf_s(value, L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
    return value;
}

static JsonObject readJson(fs::path const& path) {
    try {
        HANDLE raw = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (raw == INVALID_HANDLE_VALUE) return nullptr;
        handle file{raw};
        LARGE_INTEGER length{};
        if (!GetFileSizeEx(file.get(), &length) || length.QuadPart <= 0 || length.QuadPart > 65536) return nullptr;
        std::string bytes(static_cast<size_t>(length.QuadPart), '\0'); DWORD read = 0;
        if (!ReadFile(file.get(), bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr) || read != bytes.size()) return nullptr;
        if (bytes.starts_with("\xef\xbb\xbf")) bytes.erase(0, 3);
        return JsonObject::Parse(to_hstring(bytes));
    } catch (...) { return nullptr; }
}

static void atomicWrite(fs::path const& path, JsonObject const& object) {
    auto temporary = path; temporary += L".tmp";
    std::string bytes = to_string(object.Stringify());
    {
        std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
        output.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
        if (!output) throw hresult_error(E_FAIL, L"Cannot write agent heartbeat");
    }
    check_bool(MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING));
}

struct Settings { bool enabled{}; uint64_t address{}; hstring name; };
static Settings loadSettings(fs::path const& directory) {
    Settings settings;
    if (auto json = readJson(directory / L"settings.json")) {
        try {
            settings.enabled = json.GetNamedBoolean(L"Enabled", false);
            settings.address = static_cast<uint64_t>(json.GetNamedNumber(L"DeviceAddress", 0));
            settings.name = json.GetNamedString(L"DeviceName", L"");
        } catch (...) { return {}; }
    }
    if (!settings.address || settings.address > 0xffffffffffff) settings.enabled = false;
    return settings;
}

template<class Async> static auto awaitResult(Async const& operation) {
    if (operation.wait_for(std::chrono::seconds(20)) == Windows::Foundation::AsyncStatus::Started) {
        operation.Cancel(); throw hresult_error(HRESULT_FROM_WIN32(ERROR_TIMEOUT), L"The Bluetooth request timed out.");
    }
    return operation.GetResults();
}

static void runWorker(Session& session, HANDLE settingsEvent, fs::path const& directory, std::wstring const& logon) {
    init_apartment(apartment_type::multi_threaded);
    Settings settings = loadSettings(directory);
    JsonObject recovery = readJson(directory / L"agent-state.json");
    BluetoothDevice phone{nullptr}; std::vector<RfcommDeviceService> services;
    event_token changed{}; bool subscribed = false;
    auto release = [&] {
        if (phone && subscribed) { phone.ConnectionStatusChanged(changed); subscribed = false; }
        for (auto& service : services) service.Close();
        services.clear(); if (phone) phone.Close(); phone = nullptr;
    };
    PresencePolicy policy;
    uint64_t selected = 0, generation = UINT64_MAX, started = GetTickCount64(), nextDiscovery = 0;
    uint64_t nextSettings = started + 60000, lastWrite = 0;
    int requests = 0;
    std::wstring previousSignature;
    auto synchronizeSession = [&] {
        if (generation != session.generation) {
            // An entire lock/unlock cycle may have happened while a Bluetooth call was pending.
            policy.setLocked(true); policy.setLocked(session.locked); generation = session.generation;
        }
    };
    auto publish = [&](std::wstring const& state, Decision const& decision, std::optional<int64_t> idle, hstring const& error, bool running, bool force = false) {
        uint64_t now = GetTickCount64();
        std::wstring signature = state + L"|" + error.c_str() + (decision.armed ? L"|1" : L"|0");
        if (!force && signature == previousSignature && now - lastWrite < 30000) return;
        JsonObject snapshot;
        snapshot.Insert(L"State", JsonValue::CreateStringValue(state));
        snapshot.Insert(L"DeviceName", JsonValue::CreateStringValue(settings.name));
        snapshot.Insert(L"Running", JsonValue::CreateBooleanValue(running));
        snapshot.Insert(L"CheckedAt", JsonValue::CreateStringValue(timestamp()));
        snapshot.Insert(L"Requests", JsonValue::CreateNumberValue(requests));
        snapshot.Insert(L"Error", JsonValue::CreateStringValue(error));
        snapshot.Insert(L"Armed", JsonValue::CreateBooleanValue(decision.armed));
        snapshot.Insert(L"LockSecondsRemaining", decision.remaining ? JsonValue::CreateNumberValue(*decision.remaining) : JsonValue::CreateNullValue());
        snapshot.Insert(L"IdleSeconds", idle ? JsonValue::CreateNumberValue(static_cast<double>(*idle / 1000)) : JsonValue::CreateNullValue());
        JsonObject stateObject;
        stateObject.Insert(L"ProcessId", JsonValue::CreateNumberValue(GetCurrentProcessId()));
        stateObject.Insert(L"DeviceAddress", JsonValue::CreateNumberValue(static_cast<double>(selected)));
        stateObject.Insert(L"Heartbeat", JsonValue::CreateStringValue(timestamp()));
        stateObject.Insert(L"LogonSession", JsonValue::CreateStringValue(logon));
        stateObject.Insert(L"SystemUptime", JsonValue::CreateNumberValue(static_cast<double>(now)));
        stateObject.Insert(L"Snapshot", snapshot);
        atomicWrite(directory / L"agent-state.json", stateObject);
        // Keep actionable diagnostics, without continuously appending Bluetooth fluctuations.
        if (signature != previousSignature && (state == L"locking" || state == L"lockfailed" || !error.empty())) {
            try {
                auto path = directory / L"agent-events.jsonl";
                if (fs::exists(path) && fs::file_size(path) > 1024 * 1024) {
                    auto previous = path; previous += L".previous";
                    check_bool(MoveFileExW(path.c_str(), previous.c_str(), MOVEFILE_REPLACE_EXISTING));
                }
                snapshot.Insert(L"At", JsonValue::CreateStringValue(timestamp()));
                std::ofstream output(path, std::ios::binary | std::ios::app);
                output << to_string(snapshot.Stringify()) << '\n';
            } catch (...) {}
        }
        previousSignature = std::move(signature); lastWrite = now;
    };
    try {
        while (!session.stopping) {
            if (!settings.enabled) break;
            if (selected != settings.address) {
                release(); policy = {}; generation = UINT64_MAX;
                selected = settings.address; requests = 0; started = GetTickCount64(); nextDiscovery = 0;
                { std::lock_guard guard(session.gate); synchronizeSession(); }
                if (recovery) {
                    try {
                        auto snapshot = recovery.GetNamedObject(L"Snapshot");
                        double savedUptime = recovery.GetNamedNumber(L"SystemUptime");
                        double gap = static_cast<double>(started) - savedUptime;
                        auto oldState = snapshot.GetNamedString(L"State");
                        if (recovery.GetNamedNumber(L"DeviceAddress") == static_cast<double>(selected) && recovery.GetNamedString(L"LogonSession") == logon
                            && gap >= 0 && gap < 100000 && snapshot.GetNamedBoolean(L"Armed") && oldState != L"locked" && oldState != L"locking") {
                            auto remaining = snapshot.GetNamedValue(L"LockSecondsRemaining");
                            policy.restore(remaining.ValueType() == JsonValueType::Number ? std::optional<int>(std::max(0, static_cast<int>(remaining.GetNumber() - gap / 1000))) : std::nullopt);
                        }
                    } catch (...) {}
                    recovery = nullptr;
                }
                publish(L"checking", {}, {}, L"", true, true);
            }
            bool locked;
            { std::lock_guard guard(session.gate); synchronizeSession(); locked = session.locked; }
            std::optional<bool> connected;
            bool paired = true; hstring error;
            if (!locked) {
                try {
                    if (!phone) {
                        phone = awaitResult(BluetoothDevice::FromBluetoothAddressAsync(selected));
                        if (!phone) throw hresult_error(E_FAIL, L"The selected device is unavailable.");
                        changed = phone.ConnectionStatusChanged([wake = session.wake.get()](auto const&, auto const&) { SetEvent(wake); });
                        subscribed = true;
                    }
                    paired = phone.DeviceInformation().Pairing().IsPaired();
                    if (!paired) release();
                    else {
                        connected = phone.ConnectionStatus() == BluetoothConnectionStatus::Connected;
                        if (connected == false && GetTickCount64() >= nextDiscovery) {
                            nextDiscovery = GetTickCount64() + 60000;
                            for (auto& service : services) service.Close();
                            services.clear(); ++requests;
                            auto discovery = awaitResult(phone.GetRfcommServicesAsync(BluetoothCacheMode::Uncached));
                            for (auto const& service : discovery.Services()) services.push_back(service);
                            if (discovery.Error() != BluetoothError::Success) throw hresult_error(E_FAIL, L"Windows Bluetooth service discovery is unavailable.");
                            connected = phone.ConnectionStatus() == BluetoothConnectionStatus::Connected;
                        }
                    }
                } catch (hresult_error const& exception) { connected.reset(); error = exception.message(); release(); }
            }
            auto idle = idleTime();
            Decision decision;
            std::wstring state;
            {
                std::lock_guard guard(session.gate); synchronizeSession();
                auto elapsed = static_cast<int64_t>(GetTickCount64() - started);
                decision = policy.observe(connected, elapsed, idle);
                auto stateName = [&]() -> std::wstring {
                    if (session.locked) return L"locked";
                    if (!paired) return L"pair";
                    if (!connected || !idle) return L"unknown";
                    if (*connected) return L"connected";
                    if (decision.armed && *idle < 60000) return L"active";
                    return decision.armed ? L"confirming" : L"waiting";
                };
                state = stateName();
                if (decision.shouldLock) {
                    Settings latest = loadSettings(directory);
                    if (!latest.enabled || latest.address != selected) break;
                    connected = phone ? std::optional<bool>(phone.ConnectionStatus() == BluetoothConnectionStatus::Connected) : std::nullopt;
                    idle = idleTime();
                    if (sessionLocked()) { session.locked = true; ++session.generation; synchronizeSession(); }
                    auto result = policy.tryLock(connected, static_cast<int64_t>(GetTickCount64() - started), idle, [&] {
                        return !session.stopping && LockWorkStation() != FALSE;
                    });
                    decision = policy.observe(connected, static_cast<int64_t>(GetTickCount64() - started), idle);
                    state = result ? (*result ? L"locking" : L"lockfailed") : stateName();
                }
                locked = session.locked;
            }
            publish(state, decision, idle, error, true);
            // Connected/locked states sleep until an OS event or the 30-second heartbeat.
            // During absence, cheap idle/policy checks remain at 5 seconds.
            DWORD delay = locked || connected == true ? 30000 : 5000;
            delay = static_cast<DWORD>(std::min<uint64_t>(delay, 30000 - std::min<uint64_t>(29999, GetTickCount64() - lastWrite)));
            HANDLE handles[]{session.wake.get(), settingsEvent};
            DWORD result = WaitForMultipleObjects(2, handles, FALSE, delay);
            if (result == WAIT_FAILED) throw_last_error();
            if (result == WAIT_OBJECT_0 + 1 || GetTickCount64() >= nextSettings) {
                settings = loadSettings(directory); nextSettings = GetTickCount64() + 60000;
            }
        }
        release();
        publish(L"stopped", {}, {}, L"", false, true);
    } catch (...) { release(); throw; }
}

static LRESULT CALLBACK windowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    auto session = reinterpret_cast<Session*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        session = static_cast<Session*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(session));
    }
    if (message == WM_WTSSESSION_CHANGE && session && (wparam == WTS_SESSION_LOCK || wparam == WTS_SESSION_UNLOCK)) {
        std::lock_guard guard(session->gate);
        session->locked = wparam == WTS_SESSION_LOCK; ++session->generation; SetEvent(session->wake.get());
        return 0;
    }
    if (message == WM_APP + 1) { PostQuitMessage(static_cast<int>(wparam)); return 0; }
    return DefWindowProcW(window, message, wparam, lparam);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    fs::path directory;
    try {
        init_apartment(apartment_type::multi_threaded);
        directory = dataDirectory();
        auto sid = userSid();
        handle mutex{CreateMutexW(nullptr, TRUE, (L"Local\\AnpingLock.Agent." + sid).c_str())};
        check_bool(static_cast<bool>(mutex));
        if (GetLastError() == ERROR_ALREADY_EXISTS) return 0;
        // Keep the legacy singleton name occupied so an old agent cannot run alongside us.
        handle legacyMutex{CreateMutexW(nullptr, TRUE, (L"Local\\AwayLock.Agent." + sid).c_str())};
        check_bool(static_cast<bool>(legacyMutex));
        if (GetLastError() == ERROR_ALREADY_EXISTS) return 0;
        handle settingsEvent{CreateEventW(nullptr, FALSE, FALSE, (L"Local\\AnpingLock.Settings." + sid).c_str())};
        check_bool(static_cast<bool>(settingsEvent));
        Session session; check_bool(static_cast<bool>(session.wake)); session.locked = sessionLocked();
        WNDCLASSW type{}; type.lpfnWndProc = windowProcedure; type.hInstance = instance; type.lpszClassName = L"AnpingLock.NativeAgent";
        check_bool(RegisterClassW(&type));
        HWND window = CreateWindowExW(0, type.lpszClassName, L"", 0, 0, 0, 0, 0, nullptr, nullptr, instance, &session);
        check_bool(window != nullptr);
        check_bool(WTSRegisterSessionNotification(window, NOTIFY_FOR_THIS_SESSION));
        std::wstring logon = logonSession();
        std::thread worker([&] {
            int result = 0;
            try { runWorker(session, settingsEvent.get(), directory, logon); }
            catch (...) {
                result = 1;
                try { std::ofstream output(directory / L"agent-error.log", std::ios::app); output << to_string(timestamp()) << " native agent error: " << std::hex << to_hresult().value << '\n'; } catch (...) {}
            }
            PostMessageW(window, WM_APP + 1, result, 0);
        });
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
        session.stopping = true; SetEvent(session.wake.get()); worker.join();
        WTSUnRegisterSessionNotification(window); DestroyWindow(window); ReleaseMutex(mutex.get());
        return static_cast<int>(message.wParam);
    } catch (...) { return 1; }
}
