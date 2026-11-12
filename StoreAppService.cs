using System.Text.Json;
using DG.Uninstaller.Models;

namespace DG.Uninstaller.Services;

public sealed class StoreAppService
{
    public async Task<List<InstalledApp>> ScanAsync(bool allUsers, CancellationToken ct = default)
    {
        var all = allUsers ? "$true" : "$false";
        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
$pkgs = if ({all}) {{ Get-AppxPackage -AllUsers }} else {{ Get-AppxPackage }}
$pkgs | Where-Object {{ $_.Name -and -not $_.IsFramework -and -not $_.IsResourcePackage }} |
  Select-Object Name, PackageFullName, PackageFamilyName, PublisherDisplayName, Version, InstallLocation |
  ConvertTo-Json -Compress -Depth 3
";
        var result = await CommandRunner.RunPowerShellAsync(script, 120_000, ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StdOut))
        {
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                App.Log.Warn("Store scan: " + result.StdErr.Trim());
            return new List<InstalledApp>();
        }

        var apps = new List<InstalledApp>();
        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                    AddApp(element, apps);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                AddApp(doc.RootElement, apps);
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn("Could not parse Store apps: " + ex.Message);
        }
        return apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddApp(JsonElement element, List<InstalledApp> apps)
    {
        var name = GetString(element, "Name");
        if (string.IsNullOrWhiteSpace(name)) return;
        var fullName = GetString(element, "PackageFullName");
        var family = GetString(element, "PackageFamilyName");
        var publisher = GetString(element, "PublisherDisplayName");
        var version = GetString(element, "Version");
        var location = GetString(element, "InstallLocation");
        var isMicrosoft = publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                          name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
        apps.Add(new InstalledApp
        {
            DisplayName = name,
            Publisher = publisher,
            Version = version,
            InstallLocation = PathSafety.Normalize(location),
            Source = "Store",
            IsStore = true,
            IsMicrosoft = isMicrosoft,
            StorePackageName = name,
            StorePackageFullName = fullName,
            StorePackageFamilyName = family,
            UninstallString = "Remove-AppxPackage",
            QuietUninstallString = "Remove-AppxPackage",
            EstimatedSizeBytes = 0
        });
    }

    public async Task<CommandResult> RemoveAsync(InstalledApp app, UninstallOptions options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(app.StorePackageFullName))
            return new CommandResult { ExitCode = -1, StdErr = "Store package name is missing." };

        var fullName = EscapeSingle(app.StorePackageFullName);
        var packageName = EscapeSingle(app.StorePackageName);
        var allUsers = options.AllUsers ? " -AllUsers" : "";
        var provisioned = options.RemoveProvisionedStorePackage && !string.IsNullOrWhiteSpace(packageName)
            ? $"Remove-AppxProvisionedPackage -Online -PackageName '{packageName}' -ErrorAction SilentlyContinue | Out-Null;"
            : "";
        var script = $@"
$ErrorActionPreference = 'Stop'
Remove-AppxPackage -Package '{fullName}'{allUsers} -ErrorAction Stop
{provisioned}
Write-Output 'OK'
";
        var result = await CommandRunner.RunPowerShellAsync(script, 600_000, ct);
        if (result.Success) App.Log.Action($"Removed Store app {app.DisplayName}.");
        else App.Log.Warn($"Store removal failed for {app.DisplayName}: {result.StdErr.Trim()}");
        return result;
    }

    public static string GetPackageDataPath(InstalledApp app)
    {
        if (string.IsNullOrWhiteSpace(app.StorePackageFamilyName)) return "";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "Packages", app.StorePackageFamilyName);
    }

    private static string GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }

    private static string EscapeSingle(string value) => value.Replace("'", "''");
}
