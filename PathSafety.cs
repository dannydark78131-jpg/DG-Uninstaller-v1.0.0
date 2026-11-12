namespace DG.Uninstaller.Services;

public static class PathSafety
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> ProtectedPaths = new(Comparer)
    {
        @"C:\",
        @"C:\Windows",
        @"C:\Windows\System32",
        @"C:\Windows\SysWOW64",
        @"C:\Windows\WinSxS",
        @"C:\Windows\WindowsApps",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData",
        @"C:\Users",
        @"C:\$Recycle.Bin",
        @"C:\System Volume Information",
        @"C:\Recovery",
        @"C:\Boot",
        @"C:\EFI",
        @"C:\PerfLogs"
    };

    private static readonly HashSet<string> ProtectedSegments = new(Comparer)
    {
        "Windows", "System32", "SysWOW64", "WinSxS", "WindowsApps", "$Recycle.Bin",
        "System Volume Information", "Boot", "EFI", "Recovery", "PerfLogs"
    };

    private static readonly string[] UserDataRoots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive")
    };

    private static readonly string[] CleanupRoots =
    {
        @"C:\Windows\Temp",
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
        @"C:\Windows\SoftwareDistribution\Download",
        @"C:\Windows\Logs",
        @"C:\Windows\Prefetch",
        @"C:\Windows\Minidump",
        @"C:\Windows\LiveKernelReports",
        @"C:\Windows\MEMORY.DMP",
        @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache",
        @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive",
        @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mozilla", "Firefox")
    };

    private static readonly HashSet<string> ProtectedServices = new(Comparer)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "BrokerInfrastructure", "Power", "PlugPlay",
        "SystemEventsBroker", "EventLog", "Winmgmt", "Schedule", "Themes", "AudioSrv",
        "AudioEndpointBuilder", "Dhcp", "Dnscache", "LanmanWorkstation", "LanmanServer",
        "NlaSvc", "netprofm", "Wcmsvc", "WinDefend", "SecurityHealthService", "mpssvc",
        "BFE", "CryptSvc", "TrustedInstaller", "msiserver", "wuauserv", "UsoSvc",
        "WaaSMedicSvc", "LicenseManager", "ClipSVC", "AppXSvc", "StateRepository",
        "TokenBroker", "UserManager", "ProfSvc", "gpsvc", "SamSs", "KeyIso", "VaultSvc",
        "EFS", "seclogon", "Spooler", "SysMain", "WSearch", "DiagTrack", "DusmSvc",
        "WlanSvc", "WwanSvc", "BluetoothUserService", "BthAvctpSvc", "BthHFSrv",
        "CDPSvc", "CDPUserSvc", "OneSyncSvc", "PimIndexMaintenanceSvc", "UnistoreSvc",
        "UserDataSvc", "W32Time", "WdiServiceHost", "WdiSystemHost", "WdNisSvc",
        "SharedAccess", "IKEEXT", "PolicyAgent", "WinHttpAutoProxySvc", "iphlpsvc",
        "NcaSvc", "Nsi", "netbt", "mrxsmb", "srv2", "rdbss", "bowser", "lmhosts"
    };

    private static readonly HashSet<string> ProtectedServicePrefixes = new(Comparer)
    {
        "Win", "MS", "Microsoft", "Wlan", "Wwan", "Bluetooth", "Bth", "Rpc", "Dcom",
        "Event", "Schedule", "Themes", "Audio", "Dhcp", "Dns", "Lanman", "Nla",
        "net", "Wcm", "Security", "Sense", "MdCore", "mpssvc", "BFE", "Crypt",
        "Trusted", "msi", "wuauserv", "Uso", "WaaS", "License", "Clip", "AppX",
        "State", "Token", "User", "Prof", "gpsvc", "SamSs", "Key", "Vault", "EFS",
        "seclogon", "SysMain", "WSearch", "DiagTrack", "Dusm", "CDP", "OneSync",
        "Pim", "Unistore", "UserData", "W32Time", "Wdi", "WdNis", "WinDefend",
        "SecurityHealth", "SharedAccess", "IKEEXT", "PolicyAgent", "WinHttp", "iphlpsvc",
        "Nca", "Nsi", "netbt", "mrxsmb", "srv2", "rdbss", "bowser", "lmhosts", "smb"
    };

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().Trim('"');
        }
    }

    public static bool IsProtectedPath(string path)
    {
        var full = Normalize(path);
        if (full.Length == 0) return true;
        if (ProtectedPaths.Contains(full)) return true;
        if (full.Length == 3 && full.EndsWith(@":\", StringComparison.Ordinal)) return true;

        var parts = full.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return true;
        for (var i = 1; i < parts.Length; i++)
        {
            if (ProtectedSegments.Contains(parts[i])) return true;
        }
        return false;
    }

    public static bool IsUnderUserData(string path)
    {
        var full = Normalize(path);
        foreach (var root in UserDataRoots)
        {
            var r = Normalize(root);
            if (r.Length > 0 && (full.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                                 full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    public static bool IsUnderCleanupRoot(string path)
    {
        var full = Normalize(path);
        foreach (var root in CleanupRoots)
        {
            var r = Normalize(root);
            if (r.Length > 0 && (full.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                                 full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    public static bool IsSafeToDelete(string path, bool allowUserData = false, bool allowSystemCleanup = false)
    {
        var full = Normalize(path);
        if (full.Length == 0) return false;
        if (!File.Exists(full) && !Directory.Exists(full)) return false;
        if (IsProtectedPath(full))
        {
            if (!allowSystemCleanup || !IsUnderCleanupRoot(full)) return false;
        }
        if (!allowUserData && IsUnderUserData(full)) return false;
        return true;
    }

    public static bool IsProtectedRegistryKey(string keyPath, bool allowRunValue = false,
        bool allowServiceKey = false, bool allowClsidKey = false)
    {
        var p = NormalizeRegistry(keyPath);
        if (p.Length == 0) return true;
        if (p is "HKLM" or "HKCU" or "HKCR" or "HKU" or "HKCC" or
            "HKEY_LOCAL_MACHINE" or "HKEY_CURRENT_USER" or "HKEY_CLASSES_ROOT" or
            "HKEY_USERS" or "HKEY_CURRENT_CONFIG") return true;

        if (allowRunValue && IsRunKey(p)) return false;

        if (allowServiceKey && p.StartsWith(@"HKLM\SYSTEM\CurrentControlSet\Services\", StringComparison.OrdinalIgnoreCase))
        {
            var name = p[@"HKLM\SYSTEM\CurrentControlSet\Services\".Length..].Split('\\')[0];
            if (!IsProtectedService(name)) return false;
        }

        if (allowClsidKey)
        {
            if (p.StartsWith(@"HKLM\SOFTWARE\Classes\CLSID\", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(@"HKCU\SOFTWARE\Classes\CLSID\", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p[(p.IndexOf(@"\CLSID\", StringComparison.OrdinalIgnoreCase) + 7)..];
                if (rest.Length >= 32 && rest.Contains('{')) return false;
            }
        }

        string[] protectedPrefixes =
        {
            @"HKLM\SOFTWARE\Microsoft",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft",
            @"HKCU\SOFTWARE\Microsoft",
            @"HKLM\SYSTEM",
            @"HKLM\SECURITY",
            @"HKLM\SAM",
            @"HKLM\SOFTWARE\Classes",
            @"HKCU\SOFTWARE\Classes",
            @"HKLM\SOFTWARE\WOW6432Node\Classes",
            @"HKLM\SOFTWARE\Policies",
            @"HKCU\SOFTWARE\Policies"
        };
        foreach (var prefix in protectedPrefixes)
        {
            if (p.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsRunKey(string keyPath)
    {
        var p = NormalizeRegistry(keyPath);
        return p.Equals(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase) ||
               p.Equals(@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce", StringComparison.OrdinalIgnoreCase) ||
               p.Equals(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase) ||
               p.Equals(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", StringComparison.OrdinalIgnoreCase) ||
               p.Equals(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase) ||
               p.Equals(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProtectedService(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (ProtectedServices.Contains(name)) return true;
        foreach (var prefix in ProtectedServicePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static string NormalizeRegistry(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath)) return "";
        var p = keyPath.Trim().TrimEnd('\\');
        p = p.Replace("HKEY_LOCAL_MACHINE", "HKLM", StringComparison.OrdinalIgnoreCase)
             .Replace("HKEY_CURRENT_USER", "HKCU", StringComparison.OrdinalIgnoreCase)
             .Replace("HKEY_CLASSES_ROOT", "HKCR", StringComparison.OrdinalIgnoreCase)
             .Replace("HKEY_USERS", "HKU", StringComparison.OrdinalIgnoreCase)
             .Replace("HKEY_CURRENT_CONFIG", "HKCC", StringComparison.OrdinalIgnoreCase);
        return p;
    }
}
