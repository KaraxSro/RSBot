using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private readonly System.Windows.Forms.Timer _flushTimer;
    private const int SbVert = 1;
    private const uint SifAll = 0x17;

    public Main()
    {
        InitializeComponent();
        LoadConfig();

        var openLogFolder = new System.Windows.Forms.Button
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(475, 10),
            Size = new Size(140, 28),
            Text = "Open log folder",
        };
        openLogFolder.Click += (_, _) =>
        {
            var path = RSBot.Core.Log.CurrentFilePath;
            var directory = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Kernel.BasePath, "User", "Logs", "Sessions")
                : Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory!);
            Process.Start(new ProcessStartInfo("explorer.exe", directory!) { UseShellExecute = true });
        };
        panel1.Controls.Add(openLogFolder);
        openLogFolder.BringToFront();

        _flushTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _flushTimer.Tick += FlushTimer_Tick;
        _flushTimer.Start();
        Disposed += (_, _) => _flushTimer.Stop();

        EventManager.SubscribeEvent("OnAddLog", new Action<string, LogLevel>(AppendLog));

        if (!Kernel.Debug)
            checkDebug.Checked = false;
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
        while (_pendingLogs.TryDequeue(out var entry))
        {
            if (!ShouldDisplay(entry.Level))
                continue;

            var line = $"[{entry.Timestamp:HH:mm:ss}]\t<{entry.Level}> \t{entry.Message}{Environment.NewLine}";
            visibleText.Append(line);

        }

        if (visibleText.Length > 0)
        {
            var selectionStart = txtLog.SelectionStart;
            var selectionLength = txtLog.SelectionLength;
            var scrollInfo = GetVerticalScrollInfo(txtLog.Handle);
            var followTail = scrollInfo.nPos + scrollInfo.nPage >= scrollInfo.nMax;
            var overflow = txtLog.TextLength + visibleText.Length - MaxVisibleLogCharacters;
            if (overflow > 0 && txtLog.TextLength > 0)
            {
                txtLog.Select(0, Math.Min(overflow, txtLog.TextLength));
                txtLog.SelectedText = string.Empty;
                selectionStart = Math.Max(0, selectionStart - overflow);
            }

            txtLog.AppendText(visibleText.ToString());
            if (followTail)
            {
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.SelectionLength = 0;
                txtLog.ScrollToCaret();
            }
            else
            {
                txtLog.Select(Math.Min(selectionStart, txtLog.TextLength), Math.Min(selectionLength, txtLog.TextLength - Math.Min(selectionStart, txtLog.TextLength)));
                scrollInfo.fMask = 0x4; // SIF_POS only; do not restore the old scrollbar range.
                SetScrollInfo(txtLog.Handle, SbVert, ref scrollInfo, true);
            }
        }
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

    private static ScrollInfo GetVerticalScrollInfo(IntPtr handle)
    {
        var info = new ScrollInfo { cbSize = (uint)Marshal.SizeOf<ScrollInfo>(), fMask = SifAll };
        GetScrollInfo(handle, SbVert, ref info);
        return info;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

    [DllImport("user32.dll")]
    private static extern int SetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info, bool redraw);
}
