namespace DG.Uninstaller.Models;

public sealed class UninstallOptions
{
    public bool Silent { get; init; }
    public bool CreateRestorePoint { get; init; }
    public bool BackupLeftovers { get; init; }
    public bool CloseRunningApps { get; init; }
    public bool Aggressive { get; init; }
    public bool RemoveProvisionedStorePackage { get; init; }
    public bool AllUsers { get; init; }
}

public sealed record ProgressInfo(string Status, double? Percent = null);

public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public bool TimedOut { get; init; }
    public bool Success => !TimedOut && ExitCode == 0;
}

public sealed class CleanupResult
{
    public int DeletedItems { get; set; }
    public int FailedItems { get; set; }
    public long FreedBytes { get; set; }
    public List<string> Errors { get; } = new();
    public string BackupPath { get; set; } = "";
    public bool Success => FailedItems == 0;
}

public sealed class RestoreResult
{
    public int RestoredItems { get; set; }
    public int FailedItems { get; set; }
    public List<string> Errors { get; } = new();
    public bool Success => FailedItems == 0;
}
