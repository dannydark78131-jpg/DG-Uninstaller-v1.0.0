using System.Diagnostics;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class ProcessService
{
    public List<Process> FindProcesses(InstalledApp app)
    {
        var result = new List<Process>();
        var installLocation = PathSafety.Normalize(app.InstallLocation);
        var appCompact = NameNormalizer.Compact(app.DisplayName);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName ?? "";
                var pathNorm = PathSafety.Normalize(path);
                var matchesPath = installLocation.Length > 0 &&
                                  pathNorm.StartsWith(installLocation + "\\", StringComparison.OrdinalIgnoreCase);
                var matchesName = appCompact.Length >= 4 &&
                                  NameNormalizer.Compact(process.ProcessName).Contains(appCompact, StringComparison.OrdinalIgnoreCase);
                if (matchesPath || matchesName)
                    result.Add(process);
                else
                    process.Dispose();
            }
            catch
            {
                process.Dispose();
            }
        }
        return result;
    }

    public async Task CloseAsync(IEnumerable<Process> processes, CancellationToken ct = default)
    {
        foreach (var process in processes)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                App.Log.Action($"Closing process {process.ProcessName} ({process.Id}).");
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    var exited = await WaitForExitAsync(process, 2500, ct);
                    if (!exited && !process.HasExited)
                    {
                        App.Log.Warn($"Killing process {process.ProcessName} ({process.Id}).");
                        process.Kill(true);
                        await WaitForExitAsync(process, 2500, ct);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Log.Warn($"Could not close {process.ProcessName}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return false;
        }
    }
}
