namespace DG.Uninstaller.Services;

public static class FileSizeHelper
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    public static (long Size, int Count) GetDirectorySize(string path, CancellationToken ct = default)
    {
        long size = 0;
        var count = 0;
        try
        {
            if (!Directory.Exists(path)) return (0, 0);
            foreach (var file in Directory.EnumerateFiles(path, "*", Options))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    size += info.Length;
                    count++;
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return (size, count);
    }

    public static long GetPathSize(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path)) return GetDirectorySize(path).Size;
        }
        catch { }
        return 0;
    }
}
