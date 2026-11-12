namespace DG.Uninstaller.Models;

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public enum LeftoverType
{
    Folder,
    File,
    Shortcut,
    RegistryKey,
    RegistryValue,
    StartupEntry,
    Service,
    ScheduledTask,
    ComExtension,
    StorePackageData
}
