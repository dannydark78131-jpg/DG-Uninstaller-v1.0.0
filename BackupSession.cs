namespace DG.Uninstaller.Models;

public sealed class BackupSession
{
    public string Id { get; init; } = "";
    public string AppName { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public string FolderPath { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public int ItemCount { get; init; }
    public long SizeBytes { get; init; }
    public string DateText => CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string SizeText => Services.Format.Bytes(SizeBytes);
}
