using System.Diagnostics;

namespace NeoShell.Logging;

/// <summary>A line of the log; <see cref="Level"/> is INFO, WARN or ERROR.</summary>
public sealed record LogEntry(DateTime Time, string Level, string Message);

/// <summary>
/// Appends to one file per day in the log directory and keeps the newest few files.
/// Until <see cref="Initialize"/> is called (e.g. in tests) messages only go to the debugger.
/// </summary>
public static class Log
{
    private const int FilesToKeep = 10;
    private const int EntriesToKeep = 100;

    private static readonly Lock s_lock = new();
    private static string? s_directory;
    private static StreamWriter? s_writer;
    private static DateOnly s_writerDate;
    private static readonly Queue<LogEntry> s_recent = new();

    /// <summary>A line was logged, on the thread that logged it.</summary>
    public static event Action<LogEntry>? Written;

    /// <summary>The last lines logged, oldest first, for a live view of the log.</summary>
    public static LogEntry[] Recent()
    {
        lock (s_lock)
            return [.. s_recent];
    }

    public static void Initialize(string directory)
    {
        lock (s_lock)
        {
            Directory.CreateDirectory(directory);
            s_directory = directory;
            DeleteOldFiles();
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public static void Close()
    {
        lock (s_lock)
        {
            s_writer?.Dispose();
            s_writer = null;
            s_directory = null;
        }
    }

    private static void Write(string level, string message, Exception? exception)
    {
        DateTime now = DateTime.Now;
        string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} {level} [{Environment.CurrentManagedThreadId}] {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        Debug.WriteLine(line);

        var entry = new LogEntry(now, level.TrimEnd(), exception is null ? message : $"{message}: {exception.Message}");
        lock (s_lock)
        {
            s_recent.Enqueue(entry);
            if (s_recent.Count > EntriesToKeep)
                s_recent.Dequeue();
        }
        Written?.Invoke(entry);

        lock (s_lock)
        {
            if (s_directory is null)
                return;

            try
            {
                var today = DateOnly.FromDateTime(now);
                if (s_writer is null || s_writerDate != today)
                {
                    s_writer?.Dispose();
                    s_writer = Open(s_directory, today);
                    s_writerDate = today;
                }
                s_writer.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the shell down.
            }
        }
    }

    private static StreamWriter Open(string directory, DateOnly date)
    {
        string path = Path.Combine(directory, $"neoshell-{date:yyyy-MM-dd}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        // Flush every line: the last lines before a crash are the ones that matter.
        return new StreamWriter(stream) { AutoFlush = true };
    }

    private static void DeleteOldFiles()
    {
        // The date in the name sorts the files oldest to newest.
        string[] files = Directory.GetFiles(s_directory!, "neoshell-*.log");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string file in files.SkipLast(FilesToKeep))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
