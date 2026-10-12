namespace NeoShell.Frames;

/// <summary>
/// NeoShell.Frames.exe's log: one file per day next to NeoShell's (<c>frames-yyyy-MM-dd.log</c>), each line written
/// at once. Kept apart from NeoShell's file, which another process appending to would garble.
/// </summary>
internal static class FrameLog
{
    private static readonly Lock s_lock = new();
    private static string? s_directory;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        s_directory = directory;
        foreach (string old in Directory.GetFiles(directory, "frames-*.log").Order(StringComparer.OrdinalIgnoreCase).SkipLast(10))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        DateTime now = DateTime.Now;
        string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} {level} [{Environment.CurrentManagedThreadId}] {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;
        lock (s_lock)
        {
            if (s_directory is null)
                return;
            try
            {
                File.AppendAllText(Path.Combine(s_directory, $"frames-{now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the frames down.
            }
        }
    }
}
