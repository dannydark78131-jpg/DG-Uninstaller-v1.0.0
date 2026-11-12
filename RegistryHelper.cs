using Microsoft.Win32;

namespace DG.Uninstaller.Services;

public static class RegistryHelper
{
    public static RegistryKey? OpenKey(string fullPath, string view)
    {
        try
        {
            var (hive, subKey) = Split(fullPath);
            if (hive == RegistryHive.LocalMachine || hive == RegistryHive.CurrentUser)
            {
                var baseKey = RegistryKey.OpenBaseKey(hive, ParseView(view));
                return baseKey.OpenSubKey(subKey, writable: true);
            }
        }
        catch { }
        return null;
    }

    public static bool DeleteKey(string fullPath, string view)
    {
        try
        {
            var (hive, subKey) = Split(fullPath);
            if (hive != RegistryHive.LocalMachine && hive != RegistryHive.CurrentUser) return false;
            if (string.IsNullOrWhiteSpace(subKey)) return false;
            using var baseKey = RegistryKey.OpenBaseKey(hive, ParseView(view));
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Registry delete failed for {fullPath}: {ex.Message}");
            return false;
        }
    }

    public static bool DeleteValue(string keyPath, string valueName, string view)
    {
        try
        {
            using var key = OpenKey(keyPath, view);
            if (key == null) return false;
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Registry value delete failed for {keyPath}\\{valueName}: {ex.Message}");
            return false;
        }
    }

    public static (RegistryHive Hive, string SubKey) Split(string fullPath)
    {
        var normalized = PathSafety.NormalizeRegistry(fullPath);
        var parts = normalized.Split('\\', 2);
        var hiveText = parts[0];
        var subKey = parts.Length > 1 ? parts[1] : "";
        var hive = hiveText.Equals("HKLM", StringComparison.OrdinalIgnoreCase)
            ? RegistryHive.LocalMachine
            : hiveText.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? RegistryHive.CurrentUser
                : RegistryHive.ClassesRoot;
        return (hive, subKey);
    }

    public static RegistryView ParseView(string view)
    {
        return view == "32" ? RegistryView.Registry32 : RegistryView.Registry64;
    }
}
