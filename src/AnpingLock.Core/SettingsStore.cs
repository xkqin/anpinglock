using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Security.Principal;
using System.Threading;

namespace AnpingLock;

public sealed class AppSettings
{
    public ulong? DeviceAddress { get; set; }
    public string DeviceName { get; set; } = "";
    public bool Enabled { get; set; }
    public string Language { get; set; } = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";
}

public static class SettingsStore
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnpingLock");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwayLock", "settings.json");
                if (!File.Exists(legacy)) return new AppSettings();
                Directory.CreateDirectory(DirectoryPath);
                try { File.Copy(legacy, FilePath, false); }
                catch (IOException) when (File.Exists(FilePath)) { }
            }
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            settings.Language = settings.Language == "zh-CN" ? "zh-CN" : "en";
            settings.DeviceName ??= "";
            if (settings.DeviceAddress is 0 or > 0xFFFFFFFFFFFF) settings.DeviceAddress = null;
            if (settings.DeviceAddress == null) settings.Enabled = false;
            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
        // Wake the native agent only for real preference changes; it caches settings.
        try
        {
            using EventWaitHandle changed = EventWaitHandle.OpenExisting("Local\\AnpingLock.Settings." + WindowsIdentity.GetCurrent().User!.Value);
            changed.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }
}
