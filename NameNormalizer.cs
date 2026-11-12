using System.Text.RegularExpressions;

namespace DG.Uninstaller.Services;

public static class NameNormalizer
{
    private static readonly Regex VersionPattern = new(@"(?i)\b(?:v|version)?\s*\d+(?:\.\d+){1,4}(?:\.\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex CamelPattern = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumeric = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "application", "apps", "setup", "install", "installer", "uninstall", "uninstaller",
        "x64", "x86", "64", "32", "bit", "portable", "beta", "alpha", "release", "final",
        "the", "and", "for", "with", "pro", "free", "trial", "demo", "version", "v"
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Trim();
        text = VersionPattern.Replace(text, " ");
        text = CamelPattern.Replace(text, " ");
        text = text.ToLowerInvariant();
        text = NonAlphaNumeric.Replace(text, " ");
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    public static string Compact(string? value)
    {
        return Normalize(value).Replace(" ", "");
    }

    public static HashSet<string> Tokens(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !StopWords.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsLikelyMatch(string? candidate, string? appName, string? publisher = null)
    {
        var candidateNorm = Normalize(candidate);
        var appNorm = Normalize(appName);
        if (candidateNorm.Length == 0 || appNorm.Length == 0) return false;
        if (candidateNorm == appNorm) return true;

        var candidateCompact = candidateNorm.Replace(" ", "");
        var appCompact = appNorm.Replace(" ", "");
        if (candidateCompact == appCompact) return true;

        if (appCompact.Length >= 5 && candidateCompact.Contains(appCompact, StringComparison.OrdinalIgnoreCase))
            return true;
        if (candidateCompact.Length >= 5 && appCompact.Contains(candidateCompact, StringComparison.OrdinalIgnoreCase))
            return true;

        var appTokens = Tokens(appName);
        var candidateTokens = Tokens(candidate);
        if (appTokens.Count > 0 && candidateTokens.Count > 0)
        {
            var overlap = appTokens.Count(t => candidateTokens.Contains(t));
            if (appTokens.Count == 1 && overlap == 1 && appTokens.First().Length >= 5)
                return true;
            if (appTokens.Count >= 2 && overlap >= 2)
                return true;
            if (appTokens.Count >= 2 && overlap == 1 && appTokens.Any(t => t.Length >= 8 && candidateTokens.Contains(t)))
                return true;
            if (appTokens.Count >= 2 && candidateTokens.Count == 1)
            {
                var token = candidateTokens.First();
                if (token.Length >= 4 && appTokens.Contains(token)) return true;
            }
        }

        var publisherNorm = Normalize(publisher);
        if (publisherNorm.Length >= 4 && !IsGenericPublisher(publisherNorm) &&
            candidateNorm.Contains(publisherNorm, StringComparison.OrdinalIgnoreCase) &&
            appTokens.Count > 0 && appTokens.Any(t => candidateTokens.Contains(t)))
            return true;

        return false;
    }

    public static bool IsGenericPublisher(string normalized)
    {
        return normalized is "microsoft" or "windows" or "google" or "mozilla" or "adobe"
            or "oracle" or "intel" or "nvidia" or "amd" or "realtek" or "apple"
            or "common" or "shared" or "system" or "software" or "program";
    }

    public static bool IsGenericFolder(string name)
    {
        var norm = Normalize(name);
        if (norm.Length == 0) return true;
        return norm is "microsoft" or "windows" or "common" or "shared" or "temp" or "tmp"
            or "cache" or "data" or "program" or "programs" or "software" or "system"
            or "users" or "default" or "packages" or "crashdumps" or "d3dscache"
            or "google" or "mozilla" or "adobe" or "intel" or "nvidia" or "amd"
            or "realtek" or "apple" or "oracle" or "java" or "node_modules" or "bin"
            or "obj" or "packages" or "logs" or "log" or "backup" or "backups"
            or "installer" or "installers" or "downloads" or "desktop" or "documents";
    }
}
