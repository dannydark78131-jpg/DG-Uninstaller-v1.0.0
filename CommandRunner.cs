using System.Diagnostics;
using System.Text;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(string fileName, string arguments,
        int timeoutMs = 600_000, string? workingDirectory = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                return new CommandResult { ExitCode = -1, StdErr = "Failed to start process." };

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            if (timeoutMs > 0)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeoutMs);
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    if (ct.IsCancellationRequested) throw;
                    return new CommandResult
                    {
                        ExitCode = -1,
                        TimedOut = true,
                        StdOut = SafeResult(stdoutTask),
                        StdErr = SafeResult(stderrTask) + "\nTimed out."
                    };
                }
            }
            else
            {
                await process.WaitForExitAsync(ct);
            }

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StdOut = await stdoutTask,
                StdErr = await stderrTask
            };
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            return new CommandResult { ExitCode = -1, StdErr = ex.Message };
        }
    }

    public static async Task<CommandResult> RunPowerShellAsync(string script,
        int timeoutMs = 600_000, CancellationToken ct = default)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) powershell = "powershell.exe";
        return await RunAsync(powershell,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            timeoutMs, null, ct);
    }

    public static async Task<CommandResult> RunInteractiveAsync(string fileName, string arguments,
        string? workingDirectory = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            CreateNoWindow = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                return new CommandResult { ExitCode = -1, StdErr = "Failed to start process." };
            await process.WaitForExitAsync(ct);
            return new CommandResult { ExitCode = process.ExitCode };
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            return new CommandResult { ExitCode = -1, StdErr = ex.Message };
        }
    }

    private static string SafeResult(Task<string> task)
    {
        try { return task.IsCompletedSuccessfully ? task.Result : ""; }
        catch { return ""; }
    }
}
