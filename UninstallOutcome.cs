namespace DG.Uninstaller.Models;

public sealed class UninstallOutcome
{
    public bool OfficialUninstallerRan { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public CommandResult? Result { get; init; }
}
