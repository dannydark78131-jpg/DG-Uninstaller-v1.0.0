namespace DG.Uninstaller.Services;

public static class CommandLineParser
{
    public static (string FileName, string Arguments) Split(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return ("", "");
        var text = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 0)
            {
                var file = text[1..end];
                var args = text[(end + 1)..].Trim();
                return (file, args);
            }
        }

        var space = text.IndexOf(' ');
        if (space < 0) return (text, "");
        return (text[..space], text[(space + 1)..].Trim());
    }

    public static string ExtractExecutablePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return "";
        var (file, _) = Split(commandLine);
        if (string.IsNullOrWhiteSpace(file)) return "";
        if (!Path.IsPathRooted(file)) return file;
        try { return Path.GetFullPath(file); }
        catch { return file; }
    }

    public static string StripIconIndex(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return "";
        var text = Environment.ExpandEnvironmentVariables(displayIcon.Trim().Trim('"'));
        var comma = text.LastIndexOf(',');
        if (comma > 2 && int.TryParse(text[(comma + 1)..], out _))
            text = text[..comma];
        return text.Trim().Trim('"');
    }
}
