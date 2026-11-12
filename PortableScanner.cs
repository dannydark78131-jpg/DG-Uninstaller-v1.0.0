using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class PortableScanner
{
    private static readonly string[] IgnoredExePatterns =
    {
        "unins", "uninstall", "setup", "install", "update", "updater", "crash", "helper",
        "report", "bug", "elevate", "launcher", "vc_redist", "dotnet", "directx", "dxsetup"
    };

    private static readonly HashSet<string> IgnoredFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "program files", "program files (x86)", "programdata", "users", "temp",
        "tmp", "cache", "node_modules", ".git", "bin", "obj", "packages", "system volume information",
        "$recycle.bin", "recovery", "boot", "efi", "perflogs"
    };

    public async Task<List<InstalledApp>> ScanAsync(IEnumerable<string> roots,
        IEnumerable<string> installedLocations, IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var rootList = roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var installed = installedLocations
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(PathSafety.Normalize)
            .Where(p => p.Length > 0)
            .ToList();

        return await Task.Run(() =>
        {
            var results = new List<InstalledApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;

            foreach (var root in rootList)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                progress?.Report(new ProgressInfo($"Scanning portable root: {root}", null));
                IEnumerable<string> dirs;
                try
                {
                    dirs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                        RecurseSubdirectories = false
                    });
                }
                catch (Exception ex)
                {
                    App.Log.Warn($"Portable scan could not open {root}: {ex.Message}");
                    continue;
                }

                foreach (var dir in dirs)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var name = Path.GetFileName(dir);
                        if (string.IsNullOrWhiteSpace(name) || IgnoredFolderNames.Contains(name)) continue;
                        if (PathSafety.IsProtectedPath(dir) || PathSafety.IsUnderUserData(dir) &&
                            !IsTopLevelUserFolder(dir, root)) continue;
                        if (installed.Any(loc => dir.Equals(loc, StringComparison.OrdinalIgnoreCase) ||
                                                 dir.StartsWith(loc + "\\", StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (!seen.Add(PathSafety.Normalize(dir))) continue;

                        var exes = FindExecutables(dir, ct);
                        if (exes.Count == 0) continue;
                        var mainExe = exes.OrderByDescending(e => e.Length).First();
                        var (size, _) = FileSizeHelper.GetDirectorySize(dir, ct);
                        var modified = Directory.GetLastWriteTime(dir);
                        results.Add(new InstalledApp
                        {
                            DisplayName = name,
                            Publisher = "Portable",
                            Version = "",
                            InstallLocation = PathSafety.Normalize(dir),
                            PortablePath = PathSafety.Normalize(dir),
                            PortableExecutables = string.Join(", ", exes.Take(4).Select(e => e.Name)),
                            PortableModified = modified,
                            Source = "Portable",
                            IsPortable = true,
                            EstimatedSizeBytes = size
                        });
                        progress?.Report(new ProgressInfo($"Found portable app: {name}", null));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
            }
            return results.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, ct);
    }

    private static bool IsTopLevelUserFolder(string dir, string root)
    {
        var parent = Directory.GetParent(dir)?.FullName;
        return parent != null && PathSafety.Normalize(parent).Equals(PathSafety.Normalize(root), StringComparison.OrdinalIgnoreCase);
    }

    private static List<FileInfo> FindExecutables(string dir, CancellationToken ct)
    {
        var list = new List<FileInfo>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 2,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in Directory.EnumerateFiles(dir, "*.exe", options))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    var name = info.Name.ToLowerInvariant();
                    if (IgnoredExePatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;
                    if (info.Length < 16 * 1024) continue;
                    list.Add(info);
                }
                catch { }
            }
        }
        catch { }
        return list;
    }
}
