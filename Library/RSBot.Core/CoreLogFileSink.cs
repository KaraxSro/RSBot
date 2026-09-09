using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace RSBot.Core;

internal static class CoreLogFileSink
{
    private const long MaxFileSize = 10L * 1024 * 1024;
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly AutoResetEvent WakeUp = new(false);
    private static readonly object StartLock = new();
    private static Thread _writerThread;
    private static volatile bool _stopping;
    private static int _part;

    public static string SessionId { get; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private static string _currentFilePath;
    public static string CurrentFilePath
    {
        get
        {
            EnsureStarted();
            return _currentFilePath;
        }
        private set => _currentFilePath = value;
    }

    public static void Enqueue(DateTime timestamp, LogLevel level, string message, string subsystem, string operationId)
    {
        EnsureStarted();
        var safeMessage = (message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");
        Queue.Enqueue(
            $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [T:{Environment.CurrentManagedThreadId}] "
            + $"[Session:{SessionId}] [{subsystem ?? "General"}]"
            + (string.IsNullOrWhiteSpace(operationId) ? string.Empty : $" [Operation:{operationId}]")
            + $" {safeMessage}{Environment.NewLine}"
        );
        WakeUp.Set();
    }

    public static void FlushAndStop(int timeoutMilliseconds = 2000)
    {
        _stopping = true;
        WakeUp.Set();
        _writerThread?.Join(timeoutMilliseconds);
    }

    private static void EnsureStarted()
    {
        if (_writerThread != null)
            return;

        lock (StartLock)
        {
            if (_writerThread != null)
                return;

            CurrentFilePath = CreatePrimaryPath();
            DeleteExpiredLogs();
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "RSBot session log writer",
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushAndStop();
            _writerThread.Start();
        }
    }

    private static string CreatePrimaryPath()
    {
        var timestamp = DateTime.Now;
        var fileName = $"{timestamp:HHmmss}_{Environment.ProcessId}_{SessionId}.log";
        return Path.Combine(Kernel.BasePath, "User", "Logs", "Sessions", timestamp.ToString("yyyy-MM-dd"), fileName);
    }

    private static void WriterLoop()
    {
        while (!_stopping || !Queue.IsEmpty)
        {
            WakeUp.WaitOne(250);
            if (Queue.IsEmpty)
                continue;

            var batch = new StringBuilder();
            while (Queue.TryDequeue(out var line))
                batch.Append(line);

            try
            {
                RollOverIfNeeded(batch.Length);
                var directory = Path.GetDirectoryName(CurrentFilePath);
                Directory.CreateDirectory(directory!);
                File.AppendAllText(CurrentFilePath, batch.ToString(), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                TryFallback(batch.ToString());
            }
        }
    }

    private static void RollOverIfNeeded(int incomingCharacters)
    {
        if (!File.Exists(CurrentFilePath) || new FileInfo(CurrentFilePath).Length + incomingCharacters < MaxFileSize)
            return;

        _part++;
        CurrentFilePath = Path.Combine(
            Path.GetDirectoryName(CurrentFilePath)!,
            Path.GetFileNameWithoutExtension(CurrentFilePath) + $"_part{_part}" + Path.GetExtension(CurrentFilePath)
        );
    }

    private static void TryFallback(string text)
    {
        try
        {
            var fallbackDirectory = Path.Combine(Path.GetTempPath(), "RSBot", "Logs");
            Directory.CreateDirectory(fallbackDirectory);
            CurrentFilePath = Path.Combine(fallbackDirectory, $"RSBot_{Environment.ProcessId}_{SessionId}.log");
            File.AppendAllText(CurrentFilePath, text, Encoding.UTF8);
            Event.EventManager.FireEvent(
                "OnAddLog",
                $"Primary session log path was unavailable. Logging continues at: {CurrentFilePath}",
                LogLevel.Warning
            );
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private static void DeleteExpiredLogs()
    {
        try
        {
            var root = Path.Combine(Kernel.BasePath, "User", "Logs", "Sessions");
            if (!Directory.Exists(root))
                return;
            var cutoff = DateTime.UtcNow.AddDays(-30);
            foreach (var file in Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }
}
