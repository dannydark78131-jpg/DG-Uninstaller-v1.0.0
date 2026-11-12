using System.Text;
using System.Text.Json;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _backupRoot;

    public BackupService(string backupRoot)
    {
        _backupRoot = string.IsNullOrWhiteSpace(backupRoot)
            ? AppSettings.CreateDefault().BackupRoot
            : backupRoot;
        Directory.CreateDirectory(_backupRoot);
    }

    public string BackupRoot => _backupRoot;

    public string CreateSession(string appName)
    {
        Directory.CreateDirectory(_backupRoot);
        var baseName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Format.SafeFolderName(appName)}";
        var folder = Path.Combine(_backupRoot, baseName);
        var suffix = 2;
        while (Directory.Exists(folder))
        {
            folder = Path.Combine(_backupRoot, baseName + "_" + suffix);
            suffix++;
        }
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "Files"));
        Directory.CreateDirectory(Path.Combine(folder, "Registry"));
        Directory.CreateDirectory(Path.Combine(folder, "Tasks"));
        var manifest = new BackupManifest
        {
            Id = Path.GetFileName(folder),
            AppName = appName,
            CreatedUtc = DateTime.UtcNow
        };
        SaveManifest(folder, manifest);
        return folder;
    }

    public async Task<CleanupResult> CleanAsync(IEnumerable<LeftoverItem> items, string appName,
        bool backup, IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var list = items.ToList();
        var result = new CleanupResult();
        var session = backup ? CreateSession(appName) : "";
        result.BackupPath = session;
        var manifest = new BackupManifest
        {
            Id = session.Length > 0 ? Path.GetFileName(session) : Guid.NewGuid().ToString("N"),
            AppName = appName,
            CreatedUtc = DateTime.UtcNow
        };

        for (var i = 0; i < list.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = list[i];
            var percent = list.Count == 0 ? 100 : (i + 1) * 100.0 / list.Count;
            progress?.Report(new ProgressInfo($"Cleaning {item.TypeText}: {item.Name}", percent));
            try
            {
                var ok = item.Type switch
                {
                    LeftoverType.Folder or LeftoverType.StorePackageData =>
                        await BackupAndDeletePath(item, session, manifest, backup, i, result, ct),
                    LeftoverType.File or LeftoverType.Shortcut =>
                        await BackupAndDeleteFile(item, session, manifest, backup, i, result, ct),
                    LeftoverType.RegistryKey or LeftoverType.ComExtension =>
                        await BackupAndDeleteRegistryKey(item, session, manifest, backup, i, result, ct),
                    LeftoverType.RegistryValue or LeftoverType.StartupEntry =>
                        await BackupAndDeleteRegistryValue(item, session, manifest, backup, i, result, ct),
                    LeftoverType.Service =>
                        await BackupAndDeleteService(item, session, manifest, backup, i, result, ct),
                    LeftoverType.ScheduledTask =>
                        await BackupAndDeleteTask(item, session, manifest, backup, i, result, ct),
                    _ => false
                };
                if (ok) result.DeletedItems++;
                else result.FailedItems++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.FailedItems++;
                result.Errors.Add($"{item.TypeText} {item.Name}: {ex.Message}");
                App.Log.Error($"Cleanup failed for {item.DisplayPath}: {ex.Message}");
            }
        }

        if (backup && session.Length > 0)
            SaveManifest(session, manifest);
        App.Log.Action($"Cleanup finished. Deleted={result.DeletedItems}, failed={result.FailedItems}, freed={Format.Bytes(result.FreedBytes)}, backup={result.BackupPath}");
        return result;
    }

    private async Task<bool> BackupAndDeletePath(LeftoverItem item, string session, BackupManifest manifest,
        bool backup, int index, CleanupResult result, CancellationToken ct)
    {
        var path = PathSafety.Normalize(item.Path);
        if (!PathSafety.IsSafeToDelete(path, allowUserData: true))
        {
            result.Errors.Add($"Blocked by safety rules: {path}");
            App.Log.Warn("Blocked by safety rules: " + path);
            return false;
        }

        var size = FileSizeHelper.GetPathSize(path);
        var backupPath = "";
        if (backup)
        {
            backupPath = Path.Combine(session, "Files", $"{index:D3}_{Format.SafeFolderName(item.Name)}");
            if (!await MovePathAsync(path, backupPath, ct))
            {
                result.Errors.Add($"Could not back up {path}");
                return false;
            }
        }
        else
        {
            if (!await DeletePathAsync(path, ct))
            {
                result.Errors.Add($"Could not delete {path}");
                return false;
            }
        }

        manifest.Items.Add(new BackupAction
        {
            Type = item.Type.ToString(),
            OriginalPath = path,
            BackupPath = backupPath
        });
        result.FreedBytes += size;
        App.Log.Action($"Removed {item.TypeText}: {path}");
        return true;
    }

    private async Task<bool> BackupAndDeleteFile(LeftoverItem item, string session, BackupManifest manifest,
        bool backup, int index, CleanupResult result, CancellationToken ct)
    {
        var path = PathSafety.Normalize(item.Path);
        if (!PathSafety.IsSafeToDelete(path, allowUserData: true))
        {
            result.Errors.Add($"Blocked by safety rules: {path}");
            return false;
        }

        var size = FileSizeHelper.GetPathSize(path);
        var backupPath = "";
        if (backup)
        {
            backupPath = Path.Combine(session, "Files", $"{index:D3}_{Path.GetFileName(path)}");
            if (!await MovePathAsync(path, backupPath, ct))
            {
                result.Errors.Add($"Could not back up {path}");
                return false;
            }
        }
        else if (!await DeletePathAsync(path, ct))
        {
            result.Errors.Add($"Could not delete {path}");
            return false;
        }

        manifest.Items.Add(new BackupAction
        {
            Type = item.Type.ToString(),
            OriginalPath = path,
            BackupPath = backupPath
        });
        result.FreedBytes += size;
        App.Log.Action($"Removed {item.TypeText}: {path}");
        return true;
    }

    private async Task<bool> BackupAndDeleteRegistryKey(LeftoverItem item, string session, BackupManifest manifest,
        bool backup, int index, CleanupResult result, CancellationToken ct)
    {
        var allowClsid = item.Type == LeftoverType.ComExtension;
        if (PathSafety.IsProtectedRegistryKey(item.Path, allowClsidKey: allowClsid))
        {
            result.Errors.Add($"Protected registry key: {item.Path}");
            return false;
        }

        var backupFile = "";
        if (backup)
        {
            backupFile = Path.Combine(session, "Registry", $"{index:D3}.reg");
            if (!await ExportRegistryAsync(item.Path, item.RegistryView, backupFile, ct))
            {
                result.Errors.Add($"Could not back up registry key: {item.Path}");
                return false;
            }
        }

        if (!RegistryHelper.DeleteKey(item.Path, item.RegistryView))
        {
            result.Errors.Add($"Could not delete registry key: {item.Path}");
            return false;
        }

        manifest.Items.Add(new BackupAction
        {
            Type = item.Type.ToString(),
            OriginalPath = item.Path,
            BackupPath = backupFile,
            RegistryView = item.RegistryView
        });
        App.Log.Action($"Removed registry key: {item.Path}");
        return true;
    }

    private async Task<bool> BackupAndDeleteRegistryValue(LeftoverItem item, string session, BackupManifest manifest,
        bool backup, int index, CleanupResult result, CancellationToken ct)
    {
        if (PathSafety.IsProtectedRegistryKey(item.Path, allowRunValue: true))
        {
            result.Errors.Add($"Protected registry key: {item.Path}");
            return false;
        }

        var backupFile = "";
        if (backup)
        {
            backupFile = Path.Combine(session, "Registry", $"{index:D3}_value.reg");
            if (!await ExportRegistryAsync(item.Path, item.RegistryView, backupFile, ct))
            {
                result.Errors.Add($"Could not back up registry value: {item.DisplayPath}");
                return false;
            }
        }

        if (!RegistryHelper.DeleteValue(item.Path, item.ValueName, item.RegistryView))
        {
            result.Errors.Add($"Could not delete registry value: {item.DisplayPath}");
            return false;
        }

        manifest.Items.Add(new BackupAction
        {
            Type = item.Type.ToString(),
            OriginalPath = item.Path,
            BackupPath = backupFile,
            RegistryView = item.RegistryView,
            ValueName = item.ValueName
        });
        App.Log.Action($"Removed registry value: {item.DisplayPath}");
        return true;
    }

    private async Task<bool> BackupAndDeleteService(LeftoverItem item, string session, BackupManifest manifest,
        bool backup, int index, CleanupResult result, CancellationToken ct)
    {
        if (PathSafety.IsProtectedService(item.ServiceName) ||
            PathSafety.IsProtectedRegistryKey(item.Path, allowServiceKey: true))
        {
            result.Errors.Add($"Protected service: {item.ServiceName}");
            return false;
        }

        var backupFile = "";
        if (backup)
        {
            backupFile = Path.Combine(session, "Registry", $"{index:D3}_service.reg");
            if (!await ExportRegistryAsync(item.Path, "64", backupFile, ct))
            {
                result.Errors.Add($"Could not back up service: {item.ServiceName}");
                return false;
            }
        }

        var sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        await CommandRunner.RunAsync(sc, $"stop \"{item.ServiceName}\"", 60_000, null, ct);
        var delete = await CommandRunner.RunAsync(sc, $"delete \"{item.ServiceName}\"", 60_000, null, ct);
        if (!delete.Success)
        {
            result.Errors.Add($"Could not delete service {item.ServiceName}: {delete.StdErr.Trim()}");
            return false;
        }

        manifest.Items.Add(new BackupAction
        {
            Type = LeftoverType.Service.ToString(),
            OriginalPath = item.Path,
            BackupPath = backupFile,
            RegistryView = "64",
            ServiceName = item.ServiceName,
            ServiceImagePath = item.ServiceImagePath,
            ServiceStart = item.ServiceStart,
            ServiceDisplayName = item.ServiceDisplayName,
            ServiceDescription = item.ServiceDescription
        });
        App.Log.Action($"Removed service: {item.ServiceName}");
        return true;
    }

    private async Task<bool> BackupAndDeleteTask(LeftoverItem item, string session, BackupManifest manifest,
        bool backup, int index, CleanupResult result, CancellationToken ct)
    {
        var backupFile = "";
        if (backup)
        {
            backupFile = Path.Combine(session, "Tasks", $"{index:D3}.xml");
            if (!await ExportTaskAsync(item.TaskName, item.TaskPath, backupFile, ct))
            {
                result.Errors.Add($"Could not back up task: {item.TaskName}");
                return false;
            }
        }

        var name = EscapeSingle(item.TaskName);
        var path = EscapeSingle(item.TaskPath);
        var script = $@"
$ErrorActionPreference = 'Stop'
Unregister-ScheduledTask -TaskName '{name}' -TaskPath '{path}' -Confirm:$false -ErrorAction Stop
Write-Output 'OK'
";
        var delete = await CommandRunner.RunPowerShellAsync(script, 120_000, ct);
        if (!delete.Success)
        {
            result.Errors.Add($"Could not delete task {item.TaskName}: {delete.StdErr.Trim()}");
            return false;
        }

        manifest.Items.Add(new BackupAction
        {
            Type = LeftoverType.ScheduledTask.ToString(),
            OriginalPath = item.TaskPath + item.TaskName,
            BackupPath = backupFile,
            TaskName = item.TaskName,
            TaskPath = item.TaskPath
        });
        App.Log.Action($"Removed scheduled task: {item.TaskPath}{item.TaskName}");
        return true;
    }

    public List<BackupSession> ListSessions()
    {
        var sessions = new List<BackupSession>();
        try
        {
            if (!Directory.Exists(_backupRoot)) return sessions;
            foreach (var folder in Directory.EnumerateDirectories(_backupRoot))
            {
                var manifestPath = Path.Combine(folder, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var manifest = LoadManifest(manifestPath);
                    if (manifest == null) continue;
                    var size = FileSizeHelper.GetDirectorySize(folder).Size;
                    sessions.Add(new BackupSession
                    {
                        Id = manifest.Id,
                        AppName = manifest.AppName,
                        CreatedUtc = manifest.CreatedUtc,
                        FolderPath = folder,
                        ManifestPath = manifestPath,
                        ItemCount = manifest.Items.Count,
                        SizeBytes = size
                    });
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn("Could not list backups: " + ex.Message);
        }
        return sessions.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    public async Task<RestoreResult> RestoreAsync(BackupSession session,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var result = new RestoreResult();
        var manifest = LoadManifest(session.ManifestPath);
        if (manifest == null)
        {
            result.Errors.Add("Backup manifest could not be read.");
            result.FailedItems++;
            return result;
        }

        var actions = manifest.Items.AsEnumerable().Reverse().ToList();
        for (var i = 0; i < actions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var action = actions[i];
            progress?.Report(new ProgressInfo($"Restoring {action.Type}: {action.OriginalPath}", (i + 1) * 100.0 / actions.Count));
            try
            {
                var ok = action.Type switch
                {
                    "Folder" or "StorePackageData" or "File" or "Shortcut" => await RestorePathAsync(action, ct),
                    "RegistryKey" or "RegistryValue" or "ComExtension" or "StartupEntry" => await ImportRegistryAsync(action.BackupPath, ct),
                    "Service" => await RestoreServiceAsync(action, ct),
                    "ScheduledTask" => await RestoreTaskAsync(action, ct),
                    _ => false
                };
                if (ok) result.RestoredItems++;
                else
                {
                    result.FailedItems++;
                    result.Errors.Add($"Could not restore {action.Type}: {action.OriginalPath}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.FailedItems++;
                result.Errors.Add($"{action.Type}: {ex.Message}");
            }
        }
        App.Log.Action($"Restore finished. Restored={result.RestoredItems}, failed={result.FailedItems}");
        return result;
    }

    public void DeleteSession(BackupSession session)
    {
        if (Directory.Exists(session.FolderPath))
            Directory.Delete(session.FolderPath, true);
    }

    private async Task<bool> RestorePathAsync(BackupAction action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.BackupPath) || !File.Exists(action.BackupPath) && !Directory.Exists(action.BackupPath))
            return false;

        var original = action.OriginalPath;
        if (File.Exists(original) || Directory.Exists(original))
        {
            var conflict = original + ".restored_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            App.Log.Warn($"Original exists; restoring to {conflict}");
            original = conflict;
        }

        var parent = Path.GetDirectoryName(original);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        await MovePathAsync(action.BackupPath, original, ct);
        return File.Exists(original) || Directory.Exists(original);
    }

    private async Task<bool> ImportRegistryAsync(string backupFile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(backupFile) || !File.Exists(backupFile)) return false;
        var reg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe");
        var result = await CommandRunner.RunAsync(reg, $"import \"{backupFile}\"", 120_000, null, ct);
        return result.Success;
    }

    private async Task<bool> RestoreServiceAsync(BackupAction action, CancellationToken ct)
    {
        if (!await ImportRegistryAsync(action.BackupPath, ct)) return false;
        var sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        var query = await CommandRunner.RunAsync(sc, $"query \"{action.ServiceName}\"", 30_000, null, ct);
        if (query.Success) return true;
        if (string.IsNullOrWhiteSpace(action.ServiceImagePath)) return false;
        var start = action.ServiceStart switch
        {
            "2" => "auto",
            "3" => "demand",
            "4" => "disabled",
            _ => "demand"
        };
        var createArgs = $"create \"{action.ServiceName}\" binPath= \"{action.ServiceImagePath}\" start= {start} DisplayName= \"{action.ServiceDisplayName}\"";
        var create = await CommandRunner.RunAsync(sc, createArgs, 60_000, null, ct);
        if (!create.Success) return false;
        if (!string.IsNullOrWhiteSpace(action.ServiceDescription))
            await CommandRunner.RunAsync(sc, $"description \"{action.ServiceName}\" \"{action.ServiceDescription}\"", 30_000, null, ct);
        return true;
    }

    private async Task<bool> RestoreTaskAsync(BackupAction action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.BackupPath) || !File.Exists(action.BackupPath)) return false;
        var name = EscapeSingle(action.TaskName);
        var path = EscapeSingle(action.TaskPath);
        var file = EscapeSingle(action.BackupPath);
        var script = $@"
$ErrorActionPreference = 'Stop'
$xml = Get-Content -LiteralPath '{file}' -Raw
Register-ScheduledTask -Xml $xml -TaskName '{name}' -TaskPath '{path}' -Force -ErrorAction Stop | Out-Null
Write-Output 'OK'
";
        var result = await CommandRunner.RunPowerShellAsync(script, 120_000, ct);
        return result.Success;
    }

    private static async Task<bool> ExportRegistryAsync(string keyPath, string view, string backupFile, CancellationToken ct)
    {
        var reg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe");
        var viewArg = view == "32" ? " /reg:32" : " /reg:64";
        var result = await CommandRunner.RunAsync(reg, $"export \"{keyPath}\" \"{backupFile}\" /y{viewArg}", 120_000, null, ct);
        if (!result.Success)
            App.Log.Warn($"Registry export failed for {keyPath}: {result.StdErr.Trim()}");
        return result.Success;
    }

    private static async Task<bool> ExportTaskAsync(string taskName, string taskPath, string backupFile, CancellationToken ct)
    {
        var name = EscapeSingle(taskName);
        var path = EscapeSingle(taskPath);
        var file = EscapeSingle(backupFile);
        var script = $@"
$ErrorActionPreference = 'Stop'
$xml = Export-ScheduledTask -TaskName '{name}' -TaskPath '{path}' -ErrorAction Stop
Set-Content -LiteralPath '{file}' -Value $xml -Encoding UTF8
Write-Output 'OK'
";
        var result = await CommandRunner.RunPowerShellAsync(script, 120_000, ct);
        return result.Success;
    }

    private static async Task<bool> MovePathAsync(string source, string destination, CancellationToken ct)
    {
        try
        {
            if (File.Exists(source))
            {
                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                if (SameVolume(source, destination))
                    File.Move(source, destination, overwrite: true);
                else
                {
                    File.Copy(source, destination, overwrite: true);
                    File.Delete(source);
                }
                return true;
            }

            if (Directory.Exists(source))
            {
                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                if (SameVolume(source, destination))
                    Directory.Move(source, destination);
                else
                {
                    await CopyDirectoryAsync(source, destination, ct);
                    Directory.Delete(source, true);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Move failed from {source} to {destination}: {ex.Message}");
        }
        return false;
    }

    private static async Task<bool> DeletePathAsync(string path, CancellationToken ct)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
            return true;
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Delete failed for {path}: {ex.Message}");
            return false;
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(source, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(source, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(dir));
            await CopyDirectoryAsync(dir, target, ct);
        }
    }

    private static bool SameVolume(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string EscapeSingle(string value) => value.Replace("'", "''");

    private static void SaveManifest(string folder, BackupManifest manifest)
    {
        try
        {
            File.WriteAllText(Path.Combine(folder, "manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            App.Log.Warn("Could not write backup manifest: " + ex.Message);
        }
    }

    private static BackupManifest? LoadManifest(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private sealed class BackupManifest
    {
        public string Id { get; set; } = "";
        public string AppName { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public List<BackupAction> Items { get; set; } = new();
    }

    private sealed class BackupAction
    {
        public string Type { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public string BackupPath { get; set; } = "";
        public string RegistryView { get; set; } = "64";
        public string ValueName { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public string ServiceImagePath { get; set; } = "";
        public string ServiceStart { get; set; } = "";
        public string ServiceDisplayName { get; set; } = "";
        public string ServiceDescription { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string TaskPath { get; set; } = "";
    }
}
