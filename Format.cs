namespace DG.Uninstaller.Services;

public static class Format
{
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    public static string SafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "app";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars);
        return result.Length > 48 ? result[..48] : result;
    }
}
