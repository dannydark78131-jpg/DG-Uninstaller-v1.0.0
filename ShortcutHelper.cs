using System.Runtime.InteropServices;

namespace DG.Uninstaller.Services;

public static class ShortcutHelper
{
    public static string ResolveTarget(string lnkPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return "";
            shell = Activator.CreateInstance(type);
            if (shell == null) return "";
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(lnkPath);
            dynamic dynamicShortcut = shortcut;
            var target = dynamicShortcut.TargetPath as string ?? "";
            return target;
        }
        catch
        {
            return "";
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
