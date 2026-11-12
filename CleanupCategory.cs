namespace DG.Uninstaller.Models;

public sealed class CleanupCategory : ObservableObject
{
    private bool _isSelected;
    private long _sizeBytes;
    private int _fileCount;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public long SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (SetProperty(ref _sizeBytes, value))
                OnPropertyChanged(nameof(SizeText));
        }
    }

    public int FileCount
    {
        get => _fileCount;
        set
        {
            if (SetProperty(ref _fileCount, value))
                OnPropertyChanged(nameof(FileCountText));
        }
    }

    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] Paths { get; init; } = Array.Empty<string>();
    public string[] FilePatterns { get; init; } = Array.Empty<string>();
    public bool IsAggressive { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Low;
    public bool IsRecycleBin { get; init; }
    public bool DeleteRoot { get; init; }
    public string SizeText => SizeBytes > 0 ? Services.Format.Bytes(SizeBytes) : "—";
    public string FileCountText => FileCount > 0 ? FileCount.ToString("N0") : "—";
    public string RiskText => Risk switch
    {
        RiskLevel.Low => "Safe",
        RiskLevel.Medium => "Caution",
        RiskLevel.High => "Aggressive",
        RiskLevel.Critical => "Critical",
        _ => Risk.ToString()
    };
}
