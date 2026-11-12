using Microsoft.Win32;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class RegistryScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<List<InstalledApp>> ScanAsync(CancellationToken ct = default)
    {
        return Task.Run(() => Scan(ct), ct);
    }

    private List<InstalledApp> Scan(CancellationToken ct)
    {
        var results = new List<InstalledApp>();
        var seen = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        ScanRoot(RegistryHive.LocalMachine, RegistryView.Registry64, "64", results, seen, ct);
        ScanRoot(RegistryHive.LocalMachine, RegistryView.Registry32, "32", results, seen, ct);
        ScanRoot(RegistryHive.CurrentUser, RegistryView.Registry64, "64", results, seen, ct);
        ScanRoot(RegistryHive.CurrentUser, RegistryView.Registry32, "32", results, seen, ct);

        return results.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void ScanRoot(RegistryHive hive, RegistryView view, string viewText,
        List<InstalledApp> results, Dictionary<string, InstalledApp> seen, CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall == null) return;

            foreach (var subName in uninstall.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var key = uninstall.OpenSubKey(subName);
                    if (key == null) continue;
                    var app = ReadApp(key, hive, viewText);
                    if (app == null) continue;

                    var dedupeKey = $"{app.DisplayName}|{app.Version}|{app.UninstallString}".ToLowerInvariant();
                    if (seen.TryGetValue(dedupeKey, out var existing))
                    {
                        if (Score(app) > Score(existing))
                            seen[dedupeKey] = app;
                        continue;
                    }
                    seen[dedupeKey] = app;
                    results.Add(app);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not scan {hive} {view}: {ex.Message}");
        }
    }

    private static int Score(InstalledApp app)
    {
        var score = 0;
        if (app.RegistryView == "64") score += 10;
        if (!string.IsNullOrWhiteSpace(app.InstallLocation)) score += 5;
        if (app.EstimatedSizeBytes > 0) score += 3;
        if (!string.IsNullOrWhiteSpace(app.QuietUninstallString)) score += 1;
        return score;
    }

    private static InstalledApp? ReadApp(RegistryKey key, RegistryHive hive, string viewText)
    {
        var displayName = GetString(key, "DisplayName");
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        var systemComponent = GetInt(key, "SystemComponent") == 1;
        var releaseType = GetString(key, "ReleaseType");
        var parentKey = GetString(key, "ParentKeyName");
        var noRemove = GetInt(key, "NoRemove") == 1;
        if (noRemove) return null;

        var isUpdate = !string.IsNullOrWhiteSpace(parentKey) ||
                       releaseType.Contains("update", StringComparison.OrdinalIgnoreCase) ||
                       releaseType.Contains("hotfix", StringComparison.OrdinalIgnoreCase) ||
                       displayName.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase) ||
                       displayName.StartsWith("Security Update for ", StringComparison.OrdinalIgnoreCase) ||
                       displayName.StartsWith("Hotfix for ", StringComparison.OrdinalIgnoreCase) ||
                       (displayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && displayName.Length <= 10);

        var uninstall = GetString(key, "UninstallString");
        var quietUninstall = GetString(key, "QuietUninstallString");
        var displayIcon = GetString(key, "DisplayIcon");
        var installLocation = GetString(key, "InstallLocation");
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            var iconPath = CommandLineParser.StripIconIndex(displayIcon);
            if (!string.IsNullOrWhiteSpace(iconPath))
                installLocation = Path.GetDirectoryName(iconPath) ?? "";
        }
        if (string.IsNullOrWhiteSpace(installLocation) && !string.IsNullOrWhiteSpace(uninstall))
        {
            var exe = CommandLineParser.ExtractExecutablePath(uninstall);
            if (Path.IsPathRooted(exe))
                installLocation = Path.GetDirectoryName(exe) ?? "";
        }

        var sizeKb = GetInt(key, "EstimatedSize") ?? 0;
        var installDate = ParseInstallDate(GetString(key, "InstallDate"));
        var publisher = GetString(key, "Publisher");
        var isMicrosoft = publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                          displayName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                          key.Name.Contains(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);

        return new InstalledApp
        {
            DisplayName = displayName.Trim(),
            Publisher = publisher.Trim(),
            Version = GetString(key, "DisplayVersion").Trim(),
            InstallLocation = PathSafety.Normalize(installLocation),
            UninstallString = uninstall,
            QuietUninstallString = quietUninstall,
            DisplayIcon = displayIcon,
            RegistryKey = key.Name,
            RegistryHive = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
            RegistryView = viewText,
            Source = "Desktop",
            EstimatedSizeBytes = Math.Max(0, sizeKb) * 1024L,
            InstallDate = installDate,
            IsSystemComponent = systemComponent || isUpdate,
            IsMicrosoft = isMicrosoft
        };
    }

    private static string GetString(RegistryKey key, string name)
    {
        try { return key.GetValue(name)?.ToString() ?? ""; }
        catch { return ""; }
    }

    private static int? GetInt(RegistryKey key, string name)
    {
        try
        {
            var value = key.GetValue(name);
            if (value == null) return null;
            return Convert.ToInt32(value);
        }
        catch { return null; }
    }

    private static DateTime? ParseInstallDate(string value)
    {
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return date;
        return null;
    }
}
