using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Event;
using SDUI.Controls;

namespace RSBot.Log.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    private const int MaxVisibleLogCharacters = 200_000;
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly ConcurrentQueue<FileBatch> _pendingFileWrites = new();
    private readonly System.Windows.Forms.Timer _flushTimer;
    private int _fileWriterRunning;

    public Main()
    {
        InitializeComponent();
        LoadConfig();

        _flushTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _flushTimer.Tick += FlushTimer_Tick;
        _flushTimer.Start();
        Disposed += (_, _) => _flushTimer.Stop();

        EventManager.SubscribeEvent("OnAddLog", new Action<string, LogLevel>(AppendLog));

        if (!Kernel.Debug)
        {
            checkDebug.Checked = false;
            checkError.Visible = false;
            checkNormal.Visible = false;
            checkWarning.Visible = false;
            checkDebug.Visible = false;
        }
    }

    public void AppendLog(string message, LogLevel level = LogLevel.Notify)
    {
        _pendingLogs.Enqueue(new LogEntry(DateTime.Now, message, level));
    }

    private void FlushTimer_Tick(object sender, EventArgs e)
    {
        if (!checkEnabled.Checked)
        {
            while (_pendingLogs.TryDequeue(out _)) { }
            return;
        }

        var visibleText = new StringBuilder();
        var fileBatches = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);

        while (_pendingLogs.TryDequeue(out var entry))
        {
            if (!ShouldDisplay(entry.Level))
                continue;

            var line = $"[{entry.Timestamp:HH:mm:ss}]\t<{entry.Level}> \t{entry.Message}{Environment.NewLine}";
            visibleText.Append(line);

            if (!Kernel.Debug)
                continue;

            var logFile = Path.Combine(
                Kernel.BasePath,
                "User",
                "Logs",
                Game.Player == null ? "Environment" : Game.Player.Name,
                $"{entry.Timestamp:dd-MM-yyyy}.txt"
            );

            if (!fileBatches.TryGetValue(logFile, out var fileText))
            {
                fileText = new StringBuilder();
                fileBatches.Add(logFile, fileText);
            }

            fileText.Append(line);
        }

        if (visibleText.Length > 0)
        {
            var overflow = txtLog.TextLength + visibleText.Length - MaxVisibleLogCharacters;
            if (overflow > 0 && txtLog.TextLength > 0)
            {
                txtLog.Select(0, Math.Min(overflow, txtLog.TextLength));
                txtLog.SelectedText = string.Empty;
            }

            txtLog.AppendText(visibleText.ToString());
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        foreach (var fileBatch in fileBatches)
            _pendingFileWrites.Enqueue(new FileBatch(fileBatch.Key, fileBatch.Value.ToString()));

        StartFileWriter();
    }

    private bool ShouldDisplay(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => checkDebug.Checked,
            LogLevel.Error => checkError.Checked,
            LogLevel.Notify => checkNormal.Checked,
            LogLevel.Warning => checkWarning.Checked,
            _ => true
        };
    }

    private void StartFileWriter()
    {
        if (_pendingFileWrites.IsEmpty || Interlocked.CompareExchange(ref _fileWriterRunning, 1, 0) != 0)
            return;

        _ = Task.Run(ProcessFileWrites);
    }

    private void ProcessFileWrites()
    {
        do
        {
            while (_pendingFileWrites.TryDequeue(out var batch))
            {
                try
                {
                    var directory = Path.GetDirectoryName(batch.Path);
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.AppendAllText(batch.Path, batch.Text);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine(exception);
                }
            }

            Interlocked.Exchange(ref _fileWriterRunning, 0);
        } while (!_pendingFileWrites.IsEmpty && Interlocked.CompareExchange(ref _fileWriterRunning, 1, 0) == 0);
    }

    private void LoadConfig()
    {
        checkEnabled.Checked = GlobalConfig.Get("RSBot.Log.logEnabled", true);
    }

    private void checkEnabled_CheckedChanged(object sender, EventArgs e)
    {
        GlobalConfig.Set("RSBot.Log.logEnabled", checkEnabled.Checked.ToString());
    }

    private void btnReset_Click(object sender, EventArgs e)
    {
        while (_pendingLogs.TryDequeue(out _)) { }
        txtLog.Text = string.Empty;
    }

    private readonly struct LogEntry
    {
        public LogEntry(DateTime timestamp, string message, LogLevel level)
        {
            Timestamp = timestamp;
            Message = message;
            Level = level;
        }

        public DateTime Timestamp { get; }
        public string Message { get; }
        public LogLevel Level { get; }
    }

    private readonly struct FileBatch
    {
        public FileBatch(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public string Path { get; }
        public string Text { get; }
    }
}
