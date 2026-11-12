namespace DG.Uninstaller.Models;

public sealed class LeftoverItem : ObservableObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public LeftoverType Type { get; init; }
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Details { get; init; } = "";
    public long SizeBytes { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Medium;
    public bool IsRecommended { get; init; } = true;
    public string RegistryView { get; init; } = "64";
    public string ValueName { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public string ServiceImagePath { get; init; } = "";
    public string ServiceStart { get; init; } = "";
    public string ServiceDisplayName { get; init; } = "";
    public string ServiceDescription { get; init; } = "";
    public string TaskName { get; init; } = "";
    public string TaskPath { get; init; } = "";
    public string BackupFile { get; set; } = "";
    public string Action { get; set; } = "";

    public string SizeText => SizeBytes > 0 ? Services.Format.Bytes(SizeBytes) : "—";
    public string TypeText => Type switch
    {
        LeftoverType.Folder => "Folder",
        LeftoverType.File => "File",
        LeftoverType.Shortcut => "Shortcut",
        LeftoverType.RegistryKey => "Registry key",
        LeftoverType.RegistryValue => "Registry value",
        LeftoverType.StartupEntry => "Startup entry",
        LeftoverType.Service => "Service",
        LeftoverType.ScheduledTask => "Scheduled task",
        LeftoverType.ComExtension => "COM extension",
        LeftoverType.StorePackageData => "Store data",
        _ => Type.ToString()
    };

    public string RiskText => Risk switch
    {
        RiskLevel.Low => "Low",
        RiskLevel.Medium => "Medium",
        RiskLevel.High => "High",
        RiskLevel.Critical => "Critical",
        _ => Risk.ToString()
    };

    public string DisplayPath => Type == LeftoverType.RegistryValue && !string.IsNullOrWhiteSpace(ValueName)
        ? Path + "\\" + ValueName
        : Path;
}
