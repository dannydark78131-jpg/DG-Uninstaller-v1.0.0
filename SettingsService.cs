using System.Text.Json;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DG Uninstaller", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    Normalize(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn("Could not load settings: " + ex.Message);
        }
        var defaults = AppSettings.CreateDefault();
        Save(defaults);
        return defaults;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Normalize(settings);
            var folder = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(folder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            App.Log.Error("Could not save settings: " + ex.Message);
        }
    }

    private static void Normalize(AppSettings settings)
    {
        var defaults = AppSettings.CreateDefault();
        if (string.IsNullOrWhiteSpace(settings.BackupRoot)) settings.BackupRoot = defaults.BackupRoot;
        if (string.IsNullOrWhiteSpace(settings.LogRoot)) settings.LogRoot = defaults.LogRoot;
        if (settings.PortableScanRoots == null) settings.PortableScanRoots = new List<string>();
        settings.PortableScanRoots = settings.PortableScanRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => PathSafety.Normalize(r))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (settings.PortableScanRoots.Count == 0) settings.PortableScanRoots = defaults.PortableScanRoots;
    }
}
