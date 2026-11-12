namespace DG.Uninstaller.Services;

public sealed class RestorePointService
{
    public async Task<(bool Success, string Message)> CreateAsync(string description,
        CancellationToken ct = default)
    {
        var safeDescription = description.Replace("'", "''");
        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
  Checkpoint-Computer -Description '{safeDescription}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
  Write-Output 'OK'
}} catch {{
  Write-Error $_.Exception.Message
  exit 1
}}
";
        var result = await CommandRunner.RunPowerShellAsync(script, 300_000, ct);
        if (result.Success && result.StdOut.Contains("OK", StringComparison.OrdinalIgnoreCase))
        {
            App.Log.Action("System restore point created: " + description);
            return (true, "Restore point created.");
        }

        var message = (result.StdErr + " " + result.StdOut).Trim();
        if (string.IsNullOrWhiteSpace(message)) message = "Restore point creation failed.";
        App.Log.Warn(message);
        return (false, message);
    }
}
