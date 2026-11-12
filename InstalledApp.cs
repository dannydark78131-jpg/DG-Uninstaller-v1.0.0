using DG.Uninstaller.Services;

namespace DG.Uninstaller.Models;

public sealed class InstalledApp : ObservableObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public string DisplayIcon { get; init; } = "";
    public string RegistryKey { get; init; } = "";
    public string RegistryHive { get; init; } = "";
    public string RegistryView { get; init; } = "64";
    public string Source { get; init; } = "Desktop";
    public long EstimatedSizeBytes { get; init; }
    public DateTime? InstallDate { get; init; }
    public bool IsSystemComponent { get; init; }
    public bool IsMicrosoft { get; init; }
    public bool IsStore { get; init; }
    public bool IsPortable { get; init; }
    public bool IsProvisioned { get; init; }
    public string StorePackageName { get; init; } = "";
    public string StorePackageFullName { get; init; } = "";
    public string StorePackageFamilyName { get; init; } = "";
    public string PortablePath { get; init; } = "";
    public string PortableExecutables { get; init; } = "";
    public DateTime? PortableModified { get; init; }

    public string SizeText => EstimatedSizeBytes > 0 ? Format.Bytes(EstimatedSizeBytes) : "—";
    public string InstallDateText => InstallDate?.ToString("yyyy-MM-dd") ?? "—";
    public string TypeText => IsStore ? "Store" : IsPortable ? "Portable" : "Desktop";
    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[0].ToString().ToUpperInvariant();

    public string SearchText =>
        $"{DisplayName} {Publisher} {Version} {InstallLocation} {Source}".ToLowerInvariant();

    public string LocationForOpen =>
        IsPortable ? PortablePath :
        !string.IsNullOrWhiteSpace(InstallLocation) ? InstallLocation :
        !string.IsNullOrWhiteSpace(PortablePath) ? PortablePath : "";

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add(Publisher);
            if (!string.IsNullOrWhiteSpace(Version)) parts.Add("v" + Version);
            if (EstimatedSizeBytes > 0) parts.Add(SizeText);
            return string.Join("  •  ", parts);
        }
    }
}
