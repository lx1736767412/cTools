using System.Text;

namespace cTools.Core.Logging;

public static class Log
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Core.Storage.AppPaths.LogFile)!);
            _initialized = true;
        }
        catch
        {
            _initialized = false;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(' ')
            .Append(level)
            .Append(' ')
            .Append(message);
        if (exception is not null)
            line.AppendLine().Append(exception);
        lock (Gate)
        {
            try
            {
                if (!_initialized) return;
                var file = Core.Storage.AppPaths.LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024)
                    File.Move(file, file + ".1", true);
                File.AppendAllText(file, line.ToString() + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 日志失败不得影响主流程。
            }
        }
    }
}
