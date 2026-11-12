using System.Text;

namespace DG.Uninstaller.Services;

public sealed class LogService
{
    private readonly object _gate = new();
    private readonly List<string> _recent = new();
    private string _logFilePath = "";
    private bool _initialized;

    public event Action<string>? LogAdded;
    public string LogFilePath => _logFilePath;
    public IReadOnlyList<string> Recent
    {
        get { lock (_gate) return _recent.ToList(); }
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "DG Uninstaller", "Logs");
            Directory.CreateDirectory(root);
            _logFilePath = Path.Combine(root, $"uninstaller-{DateTime.Now:yyyyMMdd}.log");
        }
        catch
        {
            var root = Path.Combine(Path.GetTempPath(), "DG Uninstaller", "Logs");
            Directory.CreateDirectory(root);
            _logFilePath = Path.Combine(root, $"uninstaller-{DateTime.Now:yyyyMMdd}.log");
        }
        Info("DG Uninstaller & Cleaner started.");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Action(string message) => Write("ACTION", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_gate)
        {
            _recent.Add(line);
            if (_recent.Count > 1000) _recent.RemoveRange(0, _recent.Count - 1000);
            try
            {
                if (!string.IsNullOrWhiteSpace(_logFilePath))
                    File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
        try { LogAdded?.Invoke(line); } catch { }
    }
}
