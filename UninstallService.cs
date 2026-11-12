using System.Text.RegularExpressions;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class UninstallService
{
    private static readonly Regex MsiPattern = new(@"(?i)/[ix]\s*(\{[0-9a-f\-]{36}\})", RegexOptions.Compiled);
    private readonly StoreAppService _store = new();

    public async Task<UninstallOutcome> UninstallAsync(InstalledApp app, UninstallOptions options,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        if (app.IsStore)
        {
            progress?.Report(new ProgressInfo($"Removing Store app: {app.DisplayName}", null));
            var storeResult = await _store.RemoveAsync(app, options, ct);
            return new UninstallOutcome
            {
                OfficialUninstallerRan = true,
                Success = storeResult.Success,
                ExitCode = storeResult.ExitCode,
                Message = storeResult.Success ? "Store app removed." : storeResult.StdErr.Trim(),
                Result = storeResult
            };
        }

        if (app.IsPortable)
        {
            return new UninstallOutcome
            {
                OfficialUninstallerRan = false,
                Success = true,
                ExitCode = 0,
                Message = "Portable app selected for cleanup."
            };
        }

        var command = !string.IsNullOrWhiteSpace(app.QuietUninstallString) && options.Silent
            ? app.QuietUninstallString
            : app.UninstallString;
        if (string.IsNullOrWhiteSpace(command))
        {
            return new UninstallOutcome
            {
                OfficialUninstallerRan = false,
                Success = true,
                ExitCode = 0,
                Message = "No official uninstaller found. Continuing to leftover cleanup."
            };
        }

        var msiMatch = MsiPattern.Match(command);
        if (msiMatch.Success)
        {
            var guid = msiMatch.Groups[1].Value;
            var silent = options.Silent;
            var args = silent ? $"/x {guid} /qn /norestart" : $"/x {guid}";
            progress?.Report(new ProgressInfo($"Running MSI uninstaller for {app.DisplayName}", null));
            App.Log.Action($"Uninstall MSI: msiexec.exe {args}");
            var msiResult = silent
                ? await CommandRunner.RunAsync("msiexec.exe", args, 1_800_000, null, ct)
                : await CommandRunner.RunInteractiveAsync("msiexec.exe", args, null, ct);
            return BuildOutcome(app, msiResult);
        }

        var (fileName, arguments) = CommandLineParser.Split(command);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new UninstallOutcome
            {
                OfficialUninstallerRan = false,
                Success = false,
                ExitCode = -1,
                Message = "Could not parse the uninstall command."
            };
        }

        var useSilent = options.Silent && !string.IsNullOrWhiteSpace(app.QuietUninstallString);
        if (options.Silent && !useSilent)
            App.Log.Warn($"No quiet uninstall string for {app.DisplayName}; running the normal uninstaller.");
        if (options.Silent && !useSilent && arguments.Length == 0)
            useSilent = false;

        var workingDirectory = !string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation)
            ? app.InstallLocation
            : null;

        progress?.Report(new ProgressInfo($"Running uninstaller for {app.DisplayName}", null));
        App.Log.Action($"Uninstall: \"{fileName}\" {arguments}");
        var result = useSilent
            ? await CommandRunner.RunAsync(fileName, arguments, 1_800_000, workingDirectory, ct)
            : await CommandRunner.RunInteractiveAsync(fileName, arguments, workingDirectory, ct);
        return BuildOutcome(app, result);
    }

    private static UninstallOutcome BuildOutcome(InstalledApp app, CommandResult result)
    {
        var successCodes = new[] { 0, 1605, 1614, 1641, 3010 };
        var success = successCodes.Contains(result.ExitCode);
        var message = result.ExitCode switch
        {
            0 => "Uninstaller completed.",
            1605 or 1614 => "The app was already uninstalled.",
            1641 or 3010 => "Uninstaller completed; a restart is recommended.",
            _ when result.TimedOut => "Uninstaller timed out.",
            _ => $"Uninstaller exited with code {result.ExitCode}."
        };
        if (!string.IsNullOrWhiteSpace(result.StdErr) && !success)
            message += " " + result.StdErr.Trim();
        App.Log.Action($"Uninstall result for {app.DisplayName}: {message}");
        return new UninstallOutcome
        {
            OfficialUninstallerRan = true,
            Success = success,
            ExitCode = result.ExitCode,
            Message = message,
            Result = result
        };
    }
}
