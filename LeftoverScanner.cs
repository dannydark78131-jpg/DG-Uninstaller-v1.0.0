using System.Text.Json;
using Microsoft.Win32;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class LeftoverScanner
{
    private static readonly string[] AppDataRoots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
    };

    private static readonly (Environment.SpecialFolder Folder, string Label)[] ShortcutRoots =
    {
        (Environment.SpecialFolder.DesktopDirectory, "Desktop"),
        (Environment.SpecialFolder.CommonDesktopDirectory, "Public desktop"),
        (Environment.SpecialFolder.StartMenu, "Start menu"),
        (Environment.SpecialFolder.CommonStartMenu, "Public start menu"),
        (Environment.SpecialFolder.Startup, "Startup"),
        (Environment.SpecialFolder.CommonStartup, "Public startup")
    };

    public async Task<List<LeftoverItem>> ScanAsync(InstalledApp app, bool aggressive,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var results = new List<LeftoverItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        progress?.Report(new ProgressInfo($"Scanning leftovers for {app.DisplayName}", 5));
        await Task.Run(() =>
        {
            AddInstallLocation(app, results, seen, ct);
            AddStoreData(app, results, seen, ct);
            ScanAppData(app, results, seen, ct);
            ScanRegistry(app, results, seen, ct);
            ScanShortcuts(app, results, seen, ct);
            ScanStartupEntries(app, results, seen, ct);
            if (aggressive)
            {
                ScanServices(app, results, seen, ct);
                ScanComExtensions(app, results, seen, ct);
            }
        }, ct);

        if (aggressive && !app.IsMicrosoft)
        {
            progress?.Report(new ProgressInfo("Scanning scheduled tasks...", 85));
            await ScanScheduledTasksAsync(app, results, seen, ct);
        }

        progress?.Report(new ProgressInfo("Leftover scan complete.", 100));
        return results
            .OrderByDescending(i => i.IsRecommended)
            .ThenBy(i => i.Risk)
            .ThenBy(i => i.Type)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddInstallLocation(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        var location = !string.IsNullOrWhiteSpace(app.PortablePath) ? app.PortablePath : app.InstallLocation;
        if (string.IsNullOrWhiteSpace(location)) return;
        var full = PathSafety.Normalize(location);
        if (!Directory.Exists(full) || PathSafety.IsProtectedPath(full)) return;
        if (!seen.Add("F|" + full)) return;

        var size = FileSizeHelper.GetDirectorySize(full, ct).Size;
        results.Add(new LeftoverItem
        {
            Type = LeftoverType.Folder,
            Name = Path.GetFileName(full),
            Path = full,
            Details = app.IsPortable ? "Portable application folder" : "Application install folder",
            SizeBytes = size,
            Risk = app.IsPortable ? RiskLevel.High : RiskLevel.Medium,
            IsRecommended = true,
            IsSelected = true
        });
    }

    private static void AddStoreData(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        if (!app.IsStore) return;
        var dataPath = StoreAppService.GetPackageDataPath(app);
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath)) return;
        var full = PathSafety.Normalize(dataPath);
        if (!seen.Add("S|" + full)) return;
        var size = FileSizeHelper.GetDirectorySize(full, ct).Size;
        results.Add(new LeftoverItem
        {
            Type = LeftoverType.StorePackageData,
            Name = app.StorePackageFamilyName,
            Path = full,
            Details = "Local Store app data",
            SizeBytes = size,
            Risk = RiskLevel.Medium,
            IsRecommended = true,
            IsSelected = true
        });
    }

    private static void ScanAppData(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        foreach (var root in AppDataRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
            }
            catch { continue; }

            foreach (var dir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (PathSafety.IsProtectedPath(dir)) continue;

                    if (!NameNormalizer.IsGenericFolder(name) && IsAppMatch(name, app))
                    {
                        AddFolderItem(dir, name, results, seen, ct);
                        continue;
                    }

                    if (IsPublisherFolder(name, app))
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = false,
                            IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        }))
                        {
                            ct.ThrowIfCancellationRequested();
                            var subName = Path.GetFileName(sub);
                            if (string.IsNullOrWhiteSpace(subName)) continue;
                            if (IsAppMatch(subName, app) || IsPublisherFolder(subName, app))
                                AddFolderItem(sub, subName, results, seen, ct);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
    }

    private static void AddFolderItem(string dir, string name, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        var full = PathSafety.Normalize(dir);
        if (!Directory.Exists(full) || PathSafety.IsProtectedPath(full)) return;
        if (!seen.Add("F|" + full)) return;
        var size = FileSizeHelper.GetDirectorySize(full, ct).Size;
        results.Add(new LeftoverItem
        {
            Type = LeftoverType.Folder,
            Name = name,
            Path = full,
            Details = "Application data folder",
            SizeBytes = size,
            Risk = RiskLevel.Medium,
            IsRecommended = true,
            IsSelected = true
        });
    }

    private static void ScanRegistry(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        ScanRegistryRoot(RegistryHive.LocalMachine, RegistryView.Registry64, "64", app, results, seen, ct);
        ScanRegistryRoot(RegistryHive.LocalMachine, RegistryView.Registry32, "32", app, results, seen, ct);
        ScanRegistryRoot(RegistryHive.CurrentUser, RegistryView.Registry64, "64", app, results, seen, ct);
    }

    private static void ScanRegistryRoot(RegistryHive hive, RegistryView view, string viewText,
        InstalledApp app, List<LeftoverItem> results, HashSet<string> seen, CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var software = baseKey.OpenSubKey("SOFTWARE");
            if (software == null) return;

            foreach (var publisherName in software.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var publisherPath = $"{HiveText(hive)}\\SOFTWARE\\{publisherName}";
                    if (PathSafety.IsProtectedRegistryKey(publisherPath)) continue;

                    if (!NameNormalizer.IsGenericFolder(publisherName) && IsAppMatch(publisherName, app))
                    {
                        AddRegistryKey(publisherPath, publisherName, viewText, results, seen);
                        continue;
                    }

                    if (!IsPublisherFolder(publisherName, app)) continue;
                    using var publisherKey = software.OpenSubKey(publisherName);
                    if (publisherKey == null) continue;
                    foreach (var appName in publisherKey.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!IsAppMatch(appName, app) && !IsPublisherFolder(appName, app)) continue;
                        var keyPath = $"{publisherPath}\\{appName}";
                        if (PathSafety.IsProtectedRegistryKey(keyPath)) continue;
                        AddRegistryKey(keyPath, appName, viewText, results, seen);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Registry leftover scan failed for {hive} {view}: {ex.Message}");
        }
    }

    private static void AddRegistryKey(string keyPath, string name, string viewText,
        List<LeftoverItem> results, HashSet<string> seen)
    {
        if (!seen.Add($"R|{viewText}|{keyPath}")) return;
        results.Add(new LeftoverItem
        {
            Type = LeftoverType.RegistryKey,
            Name = name,
            Path = keyPath,
            Details = "Application registry key",
            Risk = RiskLevel.Medium,
            IsRecommended = true,
            IsSelected = true,
            RegistryView = viewText
        });
    }

    private static void ScanShortcuts(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        foreach (var (folder, label) in ShortcutRoots)
        {
            ct.ThrowIfCancellationRequested();
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            var recurse = folder is Environment.SpecialFolder.StartMenu or Environment.SpecialFolder.CommonStartMenu;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = recurse,
                    MaxRecursionDepth = recurse ? 3 : 0,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var target = ShortcutHelper.ResolveTarget(lnk);
                        var name = Path.GetFileNameWithoutExtension(lnk);
                        var matches = IsPathUnder(target, app.InstallLocation) ||
                                      IsPathUnder(target, app.PortablePath) ||
                                      IsAppMatch(name, app);
                        if (!matches) continue;
                        var full = PathSafety.Normalize(lnk);
                        if (!seen.Add("L|" + full)) continue;
                        results.Add(new LeftoverItem
                        {
                            Type = LeftoverType.Shortcut,
                            Name = name,
                            Path = full,
                            Details = $"{label} shortcut" + (string.IsNullOrWhiteSpace(target) ? "" : $" → {target}"),
                            SizeBytes = FileSizeHelper.GetPathSize(full),
                            Risk = RiskLevel.Low,
                            IsRecommended = true,
                            IsSelected = true
                        });
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
    }

    private static void ScanStartupEntries(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        var keys = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, "64", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, "64", @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, "64", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, "64", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "32", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "32", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce")
        };

        foreach (var (hive, view, viewText, keyPath) in keys)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                var (_, subKey) = RegistryHelper.Split(keyPath);
                using var key = baseKey.OpenSubKey(subKey);
                if (key == null) continue;
                foreach (var valueName in key.GetValueNames())
                {
                    ct.ThrowIfCancellationRequested();
                    var data = key.GetValue(valueName)?.ToString() ?? "";
                    var matches = IsPathUnder(data, app.InstallLocation) ||
                                  IsPathUnder(data, app.PortablePath) ||
                                  IsAppMatch(valueName, app) ||
                                  (!string.IsNullOrWhiteSpace(app.DisplayName) &&
                                   data.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase));
                    if (!matches) continue;
                    if (!seen.Add($"V|{viewText}|{keyPath}|{valueName}")) continue;
                    results.Add(new LeftoverItem
                    {
                        Type = LeftoverType.StartupEntry,
                        Name = valueName,
                        Path = keyPath,
                        Details = data,
                        Risk = RiskLevel.Medium,
                        IsRecommended = !app.IsMicrosoft,
                        IsSelected = !app.IsMicrosoft,
                        RegistryView = viewText,
                        ValueName = valueName
                    });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
    }

    private static void ScanServices(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        if (app.IsMicrosoft) return;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var services = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null) return;
            foreach (var serviceName in services.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                if (PathSafety.IsProtectedService(serviceName)) continue;
                try
                {
                    using var key = services.OpenSubKey(serviceName);
                    if (key == null) continue;
                    var imagePath = GetString(key, "ImagePath");
                    var displayName = GetString(key, "DisplayName");
                    var description = GetString(key, "Description");
                    var start = GetInt(key, "Start")?.ToString() ?? "";
                    var matches = IsPathUnder(imagePath, app.InstallLocation) ||
                                  IsPathUnder(imagePath, app.PortablePath) ||
                                  IsAppMatch(serviceName, app) ||
                                  IsAppMatch(displayName, app);
                    if (!matches) continue;
                    var keyPath = @"HKLM\SYSTEM\CurrentControlSet\Services\" + serviceName;
                    if (!seen.Add("SVC|" + serviceName)) continue;
                    results.Add(new LeftoverItem
                    {
                        Type = LeftoverType.Service,
                        Name = serviceName,
                        Path = keyPath,
                        Details = string.IsNullOrWhiteSpace(displayName) ? imagePath : $"{displayName} — {imagePath}",
                        Risk = RiskLevel.High,
                        IsRecommended = false,
                        IsSelected = false,
                        RegistryView = "64",
                        ServiceName = serviceName,
                        ServiceImagePath = imagePath,
                        ServiceStart = start,
                        ServiceDisplayName = displayName,
                        ServiceDescription = description
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn("Service leftover scan failed: " + ex.Message);
        }
    }

    private static void ScanComExtensions(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        if (app.IsMicrosoft) return;
        ScanComRoot(RegistryHive.LocalMachine, RegistryView.Registry64, "64", results, seen, app, ct);
        ScanComRoot(RegistryHive.LocalMachine, RegistryView.Registry32, "32", results, seen, app, ct);
        ScanComRoot(RegistryHive.CurrentUser, RegistryView.Registry64, "64", results, seen, app, ct);
    }

    private static void ScanComRoot(RegistryHive hive, RegistryView view, string viewText,
        List<LeftoverItem> results, HashSet<string> seen, InstalledApp app, CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var clsid = baseKey.OpenSubKey(@"SOFTWARE\Classes\CLSID");
            if (clsid == null) return;
            foreach (var guid in clsid.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var key = clsid.OpenSubKey(guid);
                    if (key == null) continue;
                    foreach (var serverName in new[] { "InprocServer32", "LocalServer32" })
                    {
                        using var server = key.OpenSubKey(serverName);
                        if (server == null) continue;
                        var path = server.GetValue(null)?.ToString() ?? "";
                        if (!IsPathUnder(path, app.InstallLocation) && !IsPathUnder(path, app.PortablePath))
                            continue;
                        var keyPath = $"{HiveText(hive)}\\SOFTWARE\\Classes\\CLSID\\{guid}";
                        if (PathSafety.IsProtectedRegistryKey(keyPath, allowClsidKey: true)) continue;
                        if (!seen.Add($"COM|{viewText}|{keyPath}")) continue;
                        results.Add(new LeftoverItem
                        {
                            Type = LeftoverType.ComExtension,
                            Name = guid,
                            Path = keyPath,
                            Details = path,
                            Risk = RiskLevel.High,
                            IsRecommended = false,
                            IsSelected = false,
                            RegistryView = viewText
                        });
                        break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn($"COM leftover scan failed for {hive} {view}: {ex.Message}");
        }
    }

    private static async Task ScanScheduledTasksAsync(InstalledApp app, List<LeftoverItem> results,
        HashSet<string> seen, CancellationToken ct)
    {
        var script = @"
$ErrorActionPreference = 'SilentlyContinue'
Get-ScheduledTask | ForEach-Object {
  $actions = ($_.Actions | ForEach-Object { ""$($_.Execute) $($_.Arguments)"" }) -join ' | '
  [pscustomobject]@{ TaskName = $_.TaskName; TaskPath = $_.TaskPath; Actions = $actions }
} | ConvertTo-Json -Compress -Depth 3
";
        var result = await CommandRunner.RunPowerShellAsync(script, 120_000, ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StdOut)) return;

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                    AddScheduledTask(element, app, results, seen);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                AddScheduledTask(doc.RootElement, app, results, seen);
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn("Could not parse scheduled tasks: " + ex.Message);
        }
    }

    private static void AddScheduledTask(JsonElement element, InstalledApp app,
        List<LeftoverItem> results, HashSet<string> seen)
    {
        var name = GetString(element, "TaskName");
        var path = GetString(element, "TaskPath");
        var actions = GetString(element, "Actions");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)) return;
        var matches = IsPathUnder(actions, app.InstallLocation) ||
                      IsPathUnder(actions, app.PortablePath) ||
                      IsAppMatch(name, app) ||
                      (!string.IsNullOrWhiteSpace(app.DisplayName) &&
                       actions.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (!matches) return;
        if (!seen.Add($"TASK|{path}{name}")) return;
        results.Add(new LeftoverItem
        {
            Type = LeftoverType.ScheduledTask,
            Name = name,
            Path = path + name,
            Details = actions,
            Risk = RiskLevel.High,
            IsRecommended = false,
            IsSelected = false,
            TaskName = name,
            TaskPath = string.IsNullOrWhiteSpace(path) ? "\\" : path
        });
    }

    private static bool IsAppMatch(string candidate, InstalledApp app)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (NameNormalizer.IsLikelyMatch(candidate, app.DisplayName, app.Publisher)) return true;
        if (!string.IsNullOrWhiteSpace(app.PortablePath) &&
            NameNormalizer.IsLikelyMatch(candidate, Path.GetFileName(app.PortablePath), app.Publisher))
            return true;
        return false;
    }

    private static bool IsPublisherFolder(string candidate, InstalledApp app)
    {
        var norm = NameNormalizer.Normalize(candidate);
        if (norm.Length < 3 || NameNormalizer.IsGenericPublisher(norm)) return false;
        var publisher = NameNormalizer.Normalize(app.Publisher);
        if (publisher.Length < 3 || NameNormalizer.IsGenericPublisher(publisher)) return false;
        return norm == publisher || norm.Contains(publisher, StringComparison.OrdinalIgnoreCase) ||
               publisher.Contains(norm, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathUnder(string? candidate, string? root)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root)) return false;
        var c = PathSafety.Normalize(candidate.Trim().Trim('"'));
        var r = PathSafety.Normalize(root);
        if (c.Length == 0 || r.Length == 0) return false;
        return c.Equals(r, StringComparison.OrdinalIgnoreCase) ||
               c.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string HiveText(RegistryHive hive) =>
        hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";

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
            return value == null ? null : Convert.ToInt32(value);
        }
        catch { return null; }
    }

    private static string GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }
}
