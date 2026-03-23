namespace JIE剪切板.Services;

public static class LogService
{
    private static readonly string _logFolder;
    private static readonly object _lock = new();

    static LogService()
    {
        _logFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JIE剪切板", "Logs");
    }

    public static void Initialize()
    {
        try
        {
            if (!Directory.Exists(_logFolder))
                Directory.CreateDirectory(_logFolder);
            CleanOldLogs();
        }
        catch { /* Ignore initialization failures */ }
    }

    public static void Log(string message, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                var logFile = Path.Combine(_logFolder, $"log_{DateTime.Now:yyyyMMdd}.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                if (ex != null)
                {
                    sb.AppendLine($"  Type: {ex.GetType().FullName}");
                    sb.AppendLine($"  Message: {ex.Message}");
                    sb.AppendLine($"  Stack: {ex.StackTrace}");
                }
                File.AppendAllText(logFile, sb.ToString());
            }
        }
        catch { /* Cannot fail during logging */ }
    }

    private static void CleanOldLogs()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_logFolder, "log_*.txt"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7))
                    File.Delete(file);
            }
        }
        catch { /* Ignore cleanup failures */ }
    }
}
