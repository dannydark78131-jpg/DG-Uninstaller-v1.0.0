namespace DG.Uninstaller.Models;

public sealed class AppSettings
{
    public bool CreateRestorePoint { get; set; } = true;
    public bool BackupLeftovers { get; set; } = true;
    public bool SilentUninstall { get; set; }
    public bool CloseRunningApps { get; set; } = true;
    public bool IncludeMicrosoft { get; set; } = true;
    public bool ShowSystemComponents { get; set; }
    public bool AggressiveMode { get; set; }
    public bool RemoveProvisionedStorePackages { get; set; }
    public bool CleanerAggressiveMode { get; set; }
    public bool StoreAllUsers { get; set; }
    public List<string> PortableScanRoots { get; set; } = new();
    public string BackupRoot { get; set; } = "";
    public string LogRoot { get; set; } = "";

    public static AppSettings CreateDefault()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Fixed && drive.Name != Path.GetPathRoot(Environment.SystemDirectory))
                roots.Add(drive.Name);
        }
        return new AppSettings
        {
            BackupRoot = Path.Combine(common, "DG Uninstaller", "Backups"),
            LogRoot = Path.Combine(common, "DG Uninstaller", "Logs"),
            PortableScanRoots = roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }
}
