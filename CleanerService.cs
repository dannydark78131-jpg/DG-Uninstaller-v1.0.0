using System.Runtime.InteropServices;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class CleanerService
{
    private const uint SherbNoConfirmation = 0x00000001;
    private const uint SherbNoProgressUi = 0x00000002;
    private const uint SherbNoSound = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int CbSize;
        public long I64Size;
        public long I64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref ShQueryRbInfo pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public List<CleanupCategory> GetDefaultCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDrive = Path.GetPathRoot(windir) ?? @"C:\";
        var temp = Path.GetTempPath();

        var categories = new List<CleanupCategory>
        {
            Category("User temporary files",
                "Temporary files created by applications and Windows for your user account.",
                new[] { temp, Path.Combine(local, "Temp") }, Array.Empty<string>(),
                RiskLevel.Low, aggressive: false, selected: true),

            Category("Windows temporary files",
                "System-wide temporary files. Files in use are skipped automatically.",
                new[] { Path.Combine(windir, "Temp") }, Array.Empty<string>(),
                RiskLevel.Low, aggressive: false, selected: true),

            Category("Windows logs",
                "Windows setup, servicing, and diagnostic log files.",
                new[] { Path.Combine(windir, "Logs") }, Array.Empty<string>(),
                RiskLevel.Low, aggressive: false, selected: true),

            Category("Windows error reports",
                "Crash reports and error-report queues collected by Windows Error Reporting.",
                new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(local, "Microsoft", "Windows", "WER")
                }, Array.Empty<string>(),
                RiskLevel.Low, aggressive: false, selected: true),

            Category("Browser caches",
                "Cached web content for Edge, Chrome, Firefox, Brave, Opera, and Vivaldi. Passwords and cookies are not touched.",
                BrowserCachePaths(local, roaming), Array.Empty<string>(),
                RiskLevel.Low, aggressive: false, selected: true),

            Category("Legacy internet cache",
                "Temporary internet files used by older Windows components.",
                new[] { Path.Combine(local, "Microsoft", "Windows", "INetCache") }, Array.Empty<string>(),
                RiskLevel.Low, aggressive: false, selected: true),

            Category("Thumbnail and icon cache",
                "Explorer thumbnail and icon caches. Windows rebuilds them automatically.",
                new[] { Path.Combine(local, "Microsoft", "Windows", "Explorer") },
                new[] { "thumbcache_*.db", "iconcache_*.db" },
                RiskLevel.Medium, aggressive: false, selected: true),

            Category("Windows Update download cache",
                "Downloaded update packages that Windows can download again if needed.",
                new[] { Path.Combine(windir, "SoftwareDistribution", "Download") }, Array.Empty<string>(),
                RiskLevel.Medium, aggressive: false, selected: true),

            Category("Delivery Optimization cache",
                "Cached update content shared by Delivery Optimization.",
                new[]
                {
                    Path.Combine(windir, "ServiceProfiles", "NetworkService", "AppData", "Local",
                        "Microsoft", "Windows", "DeliveryOptimization", "Cache")
                }, Array.Empty<string>(),
                RiskLevel.Low, aggressive: false, selected: true),

            Category("Recycle Bin",
                "Files you previously deleted. Emptying the Recycle Bin permanently removes them.",
                Array.Empty<string>(), Array.Empty<string>(),
                RiskLevel.Medium, aggressive: false, selected: false, recycleBin: true),

            Category("Crash dumps",
                "Application crash dumps, kernel minidumps, and live kernel reports used for diagnostics.",
                new[]
                {
                    Path.Combine(local, "CrashDumps"),
                    Path.Combine(windir, "Minidump"),
                    Path.Combine(windir, "LiveKernelReports"),
                    Path.Combine(windir, "MEMORY.DMP")
                }, Array.Empty<string>(),
                RiskLevel.Medium, aggressive: true, selected: false),

            Category("Prefetch data",
                "Windows application launch traces. Deleting them can make the next launches slower.",
                new[] { Path.Combine(windir, "Prefetch") }, Array.Empty<string>(),
                RiskLevel.Medium, aggressive: true, selected: false),

            Category("Previous Windows installation",
                "The previous Windows installation kept for rollback. Removing it frees significant space and disables rollback.",
                new[] { Path.Combine(systemDrive, "Windows.old") }, Array.Empty<string>(),
                RiskLevel.High, aggressive: true, selected: false, deleteRoot: true),

            Category("Windows upgrade leftovers",
                "Temporary Windows upgrade folders. They are safe to remove after an upgrade completes.",
                new[]
                {
                    Path.Combine(systemDrive, "$Windows.~BT"),
                    Path.Combine(systemDrive, "$Windows.~WS")
                }, Array.Empty<string>(),
                RiskLevel.High, aggressive: true, selected: false, deleteRoot: true)
        };

        return categories;
    }

    public async Task<List<CleanupCategory>> ScanAsync(bool aggressive,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var categories = GetDefaultCategories()
            .Where(c => aggressive || !c.IsAggressive)
            .ToList();

        await Task.Run(() =>
        {
            for (var i = 0; i < categories.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var category = categories[i];
                progress?.Report(new ProgressInfo($"Scanning {category.Name}...", (i + 1) * 100.0 / categories.Count));
                if (category.IsRecycleBin)
                {
                    var (size, count) = GetRecycleBinInfo();
                    category.SizeBytes = size;
                    category.FileCount = count;
                    continue;
                }

                long totalSize = 0;
                var totalCount = 0;
                foreach (var path in ExpandCategoryPaths(category))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (File.Exists(path))
                        {
                            totalSize += new FileInfo(path).Length;
                            totalCount++;
                        }
                        else if (Directory.Exists(path))
                        {
                            var (size, count) = FileSizeHelper.GetDirectorySize(path, ct);
                            totalSize += size;
                            totalCount += count;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
                category.SizeBytes = totalSize;
                category.FileCount = totalCount;
            }
        }, ct);

        progress?.Report(new ProgressInfo("Cleaner scan complete.", 100));
        return categories;
    }

    public async Task<CleanupResult> CleanAsync(IEnumerable<CleanupCategory> categories,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var result = new CleanupResult();
        var selected = categories.Where(c => c.IsSelected).ToList();
        for (var i = 0; i < selected.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var category = selected[i];
            progress?.Report(new ProgressInfo($"Cleaning {category.Name}...", (i + 1) * 100.0 / selected.Count));
            try
            {
                if (category.IsRecycleBin)
                {
                    if (EmptyRecycleBin())
                    {
                        result.DeletedItems += category.FileCount;
                        result.FreedBytes += category.SizeBytes;
                        App.Log.Action($"Emptied Recycle Bin ({Format.Bytes(category.SizeBytes)}).");
                    }
                    else
                    {
                        result.FailedItems++;
                        result.Errors.Add("Could not empty the Recycle Bin.");
                    }
                    continue;
                }

                foreach (var path in ExpandCategoryPaths(category).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(path))
                    {
                        DeleteFile(path, result);
                    }
                    else if (Directory.Exists(path))
                    {
                        if (category.DeleteRoot)
                        {
                            DeleteDirectory(path, result);
                        }
                        else
                        {
                            DeleteDirectoryContents(path, result, ct);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.FailedItems++;
                result.Errors.Add($"{category.Name}: {ex.Message}");
                App.Log.Warn($"Cleaner category failed: {category.Name}: {ex.Message}");
            }
        }

        App.Log.Action($"Cleaner finished. Deleted={result.DeletedItems}, failed={result.FailedItems}, freed={Format.Bytes(result.FreedBytes)}");
        return result;
    }

    private static CleanupCategory Category(string name, string description, string[] paths,
        string[] patterns, RiskLevel risk, bool aggressive, bool selected,
        bool recycleBin = false, bool deleteRoot = false)
    {
        return new CleanupCategory
        {
            Name = name,
            Description = description,
            Paths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray(),
            FilePatterns = patterns,
            Risk = risk,
            IsAggressive = aggressive,
            IsSelected = selected,
            IsRecycleBin = recycleBin,
            DeleteRoot = deleteRoot
        };
    }

    private static string[] BrowserCachePaths(string local, string roaming)
    {
        var paths = new List<string>();
        var profiles = new[]
        {
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(local, "Vivaldi", "User Data")
        };
        foreach (var profileRoot in profiles)
        {
            foreach (var profile in new[] { "*", "Default", "Profile *" })
            {
                var basePath = Path.Combine(profileRoot, profile);
                paths.Add(Path.Combine(basePath, "Cache"));
                paths.Add(Path.Combine(basePath, "Code Cache"));
                paths.Add(Path.Combine(basePath, "GPUCache"));
                paths.Add(Path.Combine(basePath, "Service Worker", "CacheStorage"));
            }
        }

        paths.Add(Path.Combine(local, "Mozilla", "Firefox", "Profiles", "*", "cache2"));
        paths.Add(Path.Combine(local, "Mozilla", "Firefox", "Profiles", "*", "startupCache"));
        paths.Add(Path.Combine(local, "Mozilla", "Firefox", "Profiles", "*", "shader-cache"));
        paths.Add(Path.Combine(local, "Opera Software", "Opera Stable", "Cache"));
        paths.Add(Path.Combine(local, "Opera Software", "Opera Stable", "Code Cache"));
        paths.Add(Path.Combine(local, "Opera Software", "Opera Stable", "GPUCache"));
        paths.Add(Path.Combine(roaming, "Opera Software", "Opera Stable", "Cache"));
        return paths.ToArray();
    }

    private static IEnumerable<string> ExpandCategoryPaths(CleanupCategory category)
    {
        if (category.FilePatterns.Length == 0)
        {
            foreach (var path in category.Paths)
            {
                foreach (var expanded in ExpandPath(path))
                    yield return expanded;
            }
            yield break;
        }

        foreach (var path in category.Paths)
        {
            if (File.Exists(path))
            {
                yield return path;
                continue;
            }
            if (!Directory.Exists(path)) continue;
            foreach (var pattern in category.FilePatterns)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, pattern, new EnumerationOptions
                    {
                        RecurseSubdirectories = false,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    });
                }
                catch { continue; }
                foreach (var file in files)
                    yield return file;
            }
        }
    }

    private static IEnumerable<string> ExpandPath(string path)
    {
        var full = PathSafety.Normalize(path);
        if (full.Length == 0) yield break;
        if (!full.Contains('*') && !full.Contains('?'))
        {
            if (File.Exists(full) || Directory.Exists(full)) yield return full;
            yield break;
        }

        var parts = full.Split(Path.DirectorySeparatorChar);
        var baseParts = new List<string>();
        var index = 0;
        while (index < parts.Length && !parts[index].Contains('*') && !parts[index].Contains('?'))
        {
            baseParts.Add(parts[index]);
            index++;
        }
        if (index >= parts.Length) yield break;

        var baseDir = string.Join(Path.DirectorySeparatorChar.ToString(), baseParts);
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = Path.GetPathRoot(full) ?? "";
        var remaining = parts.Skip(index).ToArray();
        foreach (var expanded in ExpandSegments(baseDir, remaining, 0))
            yield return expanded;
    }

    private static IEnumerable<string> ExpandSegments(string current, string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            if (File.Exists(current) || Directory.Exists(current)) yield return current;
            yield break;
        }

        var part = parts[index];
        if (part.Contains('*') || part.Contains('?'))
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current, part, new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
            }
            catch { yield break; }

            foreach (var entry in entries)
            {
                if (index == parts.Length - 1)
                {
                    yield return entry;
                }
                else if (Directory.Exists(entry))
                {
                    foreach (var child in ExpandSegments(entry, parts, index + 1))
                        yield return child;
                }
            }
            yield break;
        }

        var next = Path.Combine(current, part);
        if (index == parts.Length - 1)
        {
            if (File.Exists(next) || Directory.Exists(next)) yield return next;
        }
        else if (Directory.Exists(next))
        {
            foreach (var child in ExpandSegments(next, parts, index + 1))
                yield return child;
        }
    }

    private static void DeleteDirectoryContents(string path, CleanupResult result, CancellationToken ct)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            });
        }
        catch (Exception ex)
        {
            result.FailedItems++;
            result.Errors.Add($"{path}: {ex.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(entry)) DeleteFile(entry, result);
                else if (Directory.Exists(entry)) DeleteDirectory(entry, result);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.FailedItems++;
                result.Errors.Add($"{entry}: {ex.Message}");
            }
        }
    }

    private static void DeleteFile(string path, CleanupResult result)
    {
        try
        {
            if (!PathSafety.IsSafeToDelete(path, allowUserData: true, allowSystemCleanup: true))
            {
                result.FailedItems++;
                result.Errors.Add($"Blocked by safety rules: {path}");
                return;
            }
            var size = FileSizeHelper.GetPathSize(path);
            File.Delete(path);
            result.DeletedItems++;
            result.FreedBytes += size;
        }
        catch (Exception ex)
        {
            result.FailedItems++;
            result.Errors.Add($"{path}: {ex.Message}");
        }
    }

    private static void DeleteDirectory(string path, CleanupResult result)
    {
        try
        {
            if (!PathSafety.IsSafeToDelete(path, allowUserData: true, allowSystemCleanup: true))
            {
                result.FailedItems++;
                result.Errors.Add($"Blocked by safety rules: {path}");
                return;
            }
            var size = FileSizeHelper.GetPathSize(path);
            Directory.Delete(path, true);
            result.DeletedItems++;
            result.FreedBytes += size;
        }
        catch (Exception ex)
        {
            result.FailedItems++;
            result.Errors.Add($"{path}: {ex.Message}");
        }
    }

    private static (long Size, int Count) GetRecycleBinInfo()
    {
        try
        {
            var info = new ShQueryRbInfo { CbSize = Marshal.SizeOf<ShQueryRbInfo>() };
            var hr = SHQueryRecycleBin(null, ref info);
            if (hr != 0) return (0, 0);
            return (info.I64Size, (int)Math.Min(info.I64NumItems, int.MaxValue));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static bool EmptyRecycleBin()
    {
        try
        {
            var hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }
}
