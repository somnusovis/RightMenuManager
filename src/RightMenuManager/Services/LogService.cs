namespace RightMenuManager.Services;

public static class LogService
{
    private static readonly object Gate = new();

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RightMenuManager",
                "Logs");
            Directory.CreateDirectory(directory);

            string file = Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd}.log");
            string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            if (exception != null) text += Environment.NewLine + exception;

            lock (Gate)
            {
                File.AppendAllText(file, text + Environment.NewLine + Environment.NewLine);
            }
        }
        catch
        {
            // 日志失败不应影响主流程。
        }
    }
}
