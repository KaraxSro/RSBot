using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private const int MaxVisiblePacketRows = 5_000;
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly System.Windows.Forms.Timer _flushTimer;
    private System.Windows.Forms.CheckBox _packetCaptureEnabled;
    private DataGridView _packetGrid;
    private System.Windows.Forms.Label _packetCapturePath;
    private PacketRowIdentity? _contextPacketIdentity;
    private bool _updatingPacketCaptureToggle;
    private const int SbVert = 1;
    private const uint SifAll = 0x17;

    public Main()
    {
        InitializeComponent();
        BuildTabbedLayout();
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
        Disposed += (_, _) =>
        {
            _flushTimer.Stop();
            PacketCaptureService.Instance.Stop();
        };

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
        FlushPacketCapture();

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
            AppendTextPreservingScroll(txtLog, visibleText.ToString(), MaxVisibleLogCharacters);
        }
    }

    private void BuildTabbedLayout()
    {
        Controls.Remove(txtLog);
        Controls.Remove(panel1);

        var tabs = new System.Windows.Forms.TabControl { Dock = DockStyle.Fill };
        var applicationLogPage = new System.Windows.Forms.TabPage("Application log");
        applicationLogPage.Controls.Add(txtLog);
        applicationLogPage.Controls.Add(panel1);
        txtLog.Dock = DockStyle.Fill;
        panel1.Dock = DockStyle.Top;

        var packetCapturePage = new System.Windows.Forms.TabPage("Packet capture");
        var packetHeader = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Top,
            Height = 76
        };

        _packetCaptureEnabled = new System.Windows.Forms.CheckBox
        {
            AutoSize = true,
            Location = new Point(10, 12),
            Text = "Capture packets"
        };
        _packetCaptureEnabled.CheckedChanged += PacketCaptureEnabled_CheckedChanged;

        var clearPackets = new System.Windows.Forms.Button
        {
            Location = new Point(98, 8),
            Size = new Size(75, 28),
            Text = "Clear"
        };
        clearPackets.Click += (_, _) =>
        {
            PacketCaptureService.Instance.ClearPending();
            _packetGrid.Rows.Clear();
        };

        var openCaptureFolder = new System.Windows.Forms.Button
        {
            Location = new Point(179, 8),
            Size = new Size(95, 28),
            Text = "Open folder"
        };
        openCaptureFolder.Click += (_, _) =>
        {
            var directory = Path.Combine(Kernel.BasePath, "User", "Logs", "PacketCapture");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
        };

        var manageHiddenPackets = new System.Windows.Forms.Button
        {
            Location = new Point(0, 8),
            Size = new Size(92, 28),
            Text = "Hidden..."
        };
        manageHiddenPackets.Click += (_, _) => ShowHiddenPacketsDialog();

        _packetCapturePath = new System.Windows.Forms.Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 3, 10, 3),
            Text = "Capture is off. Packet payloads are decoded; security headers are not included."
        };

        var packetButtons = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Right,
            Width = 280
        };
        packetButtons.Controls.Add(manageHiddenPackets);
        packetButtons.Controls.Add(clearPackets);
        packetButtons.Controls.Add(openCaptureFolder);

        var packetControls = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Top,
            Height = 44
        };
        packetControls.Controls.Add(_packetCaptureEnabled);
        packetControls.Controls.Add(packetButtons);

        packetHeader.Controls.Add(_packetCapturePath);
        packetHeader.Controls.Add(packetControls);

        _packetGrid = CreatePacketGrid();
        var packetMenu = new System.Windows.Forms.ContextMenuStrip();
        var copyPackets = packetMenu.Items.Add("Copy", null, (_, _) => CopySelectedPackets());
        packetMenu.Items.Add(new ToolStripSeparator());
        var setPacketName = packetMenu.Items.Add("Set packet name...", null, (_, _) => SetSelectedPacketName());
        var resetPacketName = packetMenu.Items.Add("Reset packet name", null, (_, _) => ResetSelectedPacketName());
        packetMenu.Items.Add(new ToolStripSeparator());
        var hidePackets = packetMenu.Items.Add("Hide selected packet type(s)", null, (_, _) => HideSelectedPacketTypes());
        packetMenu.Opening += (sender, args) =>
        {
            copyPackets.Enabled = _packetGrid.SelectedRows.Count > 0;
            setPacketName.Enabled = _contextPacketIdentity.HasValue;
            resetPacketName.Enabled = _contextPacketIdentity.HasValue;
            hidePackets.Enabled = GetSelectedPacketIdentities().Count > 0;
        };
        _packetGrid.ContextMenuStrip = packetMenu;
        _packetGrid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right && args.RowIndex >= 0)
            {
                var row = _packetGrid.Rows[args.RowIndex];
                if (!row.Selected)
                {
                    _packetGrid.ClearSelection();
                    row.Selected = true;
                }

                _packetGrid.CurrentCell = row.Cells[0];
                _contextPacketIdentity = row.Tag is PacketRowIdentity identity ? identity : null;
            }
        };

        packetCapturePage.Controls.Add(_packetGrid);
        packetCapturePage.Controls.Add(packetHeader);
        tabs.TabPages.Add(applicationLogPage);
        tabs.TabPages.Add(packetCapturePage);
        Controls.Add(tabs);
    }

    private static DataGridView CreatePacketGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = true,
            AllowUserToResizeRows = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders,
            BorderStyle = BorderStyle.None,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Timestamp",
            HeaderText = "Date and time",
            Width = 205
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Sequence",
            HeaderText = "#",
            Width = 45
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Direction",
            HeaderText = "Direction",
            Width = 115
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Opcode",
            HeaderText = "Opcode",
            Width = 90
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PacketName",
            HeaderText = "Name",
            Width = 260
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Length",
            HeaderText = "Len",
            Width = 42
        });
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Encrypted",
            HeaderText = "Enc",
            Width = 40
        });
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Massive",
            HeaderText = "Massive",
            Width = 72
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True },
            MinimumWidth = 180,
            Name = "Payload",
            HeaderText = "Payload (hex)"
        });

        return grid;
    }

    private bool IsFollowingPacketTail()
    {
        if (_packetGrid.Rows.Count == 0 || _packetGrid.FirstDisplayedScrollingRowIndex < 0)
            return true;

        var lastDisplayedRow = _packetGrid.FirstDisplayedScrollingRowIndex +
                               _packetGrid.DisplayedRowCount(false) - 1;
        return lastDisplayedRow >= _packetGrid.Rows.Count - 1;
    }

    private void CopySelectedPackets()
    {
        if (_packetGrid.SelectedRows.Count == 0)
            return;

        try
        {
            var clipboardContent = _packetGrid.GetClipboardContent();
            if (clipboardContent != null)
                Clipboard.SetDataObject(clipboardContent, true);
        }
        catch (ExternalException exception)
        {
            RSBot.Core.Log.Warn($"Could not copy packets to the clipboard: {exception.Message}");
        }
    }

    private void SetSelectedPacketName()
    {
        if (!_contextPacketIdentity.HasValue)
            return;

        var identity = _contextPacketIdentity.Value;
        var currentName = PacketNameRegistry.Instance.GetName(identity.Direction, identity.Opcode);
        var name = ShowPacketNameDialog(identity, currentName);
        if (name == null)
            return;

        PacketNameRegistry.Instance.SetCustomName(identity.Direction, identity.Opcode, name);
        UpdateVisiblePacketNames(identity);
    }

    private void ResetSelectedPacketName()
    {
        if (!_contextPacketIdentity.HasValue)
            return;

        var identity = _contextPacketIdentity.Value;
        PacketNameRegistry.Instance.ResetCustomName(identity.Direction, identity.Opcode);
        UpdateVisiblePacketNames(identity);
    }

    private List<PacketRowIdentity> GetSelectedPacketIdentities()
    {
        var identities = new List<PacketRowIdentity>();
        foreach (DataGridViewRow row in _packetGrid.SelectedRows)
        {
            if (row.Tag is PacketRowIdentity identity && !identities.Contains(identity))
                identities.Add(identity);
        }

        return identities;
    }

    private void HideSelectedPacketTypes()
    {
        var identities = GetSelectedPacketIdentities();
        foreach (var identity in identities)
            PacketNameRegistry.Instance.SetHidden(identity.Direction, identity.Opcode, true);

        for (var index = _packetGrid.Rows.Count - 1; index >= 0; index--)
        {
            if (_packetGrid.Rows[index].Tag is PacketRowIdentity identity && identities.Contains(identity))
                _packetGrid.Rows.RemoveAt(index);
        }

        _contextPacketIdentity = null;
    }

    private void UpdateVisiblePacketNames(PacketRowIdentity identity)
    {
        var name = PacketNameRegistry.Instance.GetName(identity.Direction, identity.Opcode);
        foreach (DataGridViewRow row in _packetGrid.Rows)
        {
            if (row.Tag is PacketRowIdentity rowIdentity && rowIdentity.Equals(identity))
                row.Cells["PacketName"].Value = name;
        }
    }

    private string ShowPacketNameDialog(PacketRowIdentity identity, string currentName)
    {
        using var dialog = new Form
        {
            AcceptButton = null,
            CancelButton = null,
            ClientSize = new Size(430, 126),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = $"Packet name - {identity.Direction} {identity.Opcode}"
        };

        var description = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Location = new Point(12, 14),
            Text = "Name used for every packet with this direction and opcode:"
        };
        var input = new System.Windows.Forms.TextBox
        {
            Location = new Point(12, 40),
            Size = new Size(406, 23),
            Text = currentName ?? string.Empty
        };
        var ok = new System.Windows.Forms.Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(262, 84),
            Size = new Size(75, 28),
            Text = "OK"
        };
        var cancel = new System.Windows.Forms.Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(343, 84),
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        dialog.Controls.Add(description);
        dialog.Controls.Add(input);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) =>
        {
            input.SelectAll();
            input.Focus();
        };

        return dialog.ShowDialog(FindForm()) == DialogResult.OK ? input.Text.Trim() : null;
    }

    private void ShowHiddenPacketsDialog()
    {
        using var dialog = new Form
        {
            AcceptButton = null,
            CancelButton = null,
            ClientSize = new Size(520, 330),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = "Hidden packet types"
        };
        var hiddenPackets = new ListBox
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 9F),
            Location = new Point(12, 12),
            SelectionMode = SelectionMode.MultiExtended,
            Size = new Size(496, 260)
        };
        var showSelected = new System.Windows.Forms.Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(313, 288),
            Size = new Size(114, 28),
            Text = "Show selected"
        };
        var close = new System.Windows.Forms.Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel,
            Location = new Point(433, 288),
            Size = new Size(75, 28),
            Text = "Close"
        };

        void ReloadHiddenPackets()
        {
            hiddenPackets.Items.Clear();
            foreach (var entry in PacketNameRegistry.Instance.GetHiddenPackets())
                hiddenPackets.Items.Add(entry);
            showSelected.Enabled = hiddenPackets.Items.Count > 0;
        }

        showSelected.Click += (_, _) =>
        {
            var selectedEntries = new List<HiddenPacketEntry>();
            foreach (var selectedItem in hiddenPackets.SelectedItems)
            {
                if (selectedItem is HiddenPacketEntry entry)
                    selectedEntries.Add(entry);
            }

            foreach (var entry in selectedEntries)
                PacketNameRegistry.Instance.SetHidden(entry.Direction, entry.Opcode, false);

            ReloadHiddenPackets();
        };

        dialog.Controls.Add(hiddenPackets);
        dialog.Controls.Add(showSelected);
        dialog.Controls.Add(close);
        dialog.CancelButton = close;
        ReloadHiddenPackets();
        dialog.ShowDialog(FindForm());
    }

    private void PacketCaptureEnabled_CheckedChanged(object sender, EventArgs e)
    {
        if (_updatingPacketCaptureToggle)
            return;

        if (!_packetCaptureEnabled.Checked)
        {
            PacketCaptureService.Instance.Stop();
            var lastFile = PacketCaptureService.Instance.CurrentFilePath;
            _packetCapturePath.Text = string.IsNullOrWhiteSpace(lastFile)
                ? "Capture is off."
                : $"Stopped. Last file: {lastFile}";
            return;
        }

        if (PacketCaptureService.Instance.Start(out var error))
        {
            _packetCapturePath.Text = $"Writing to: {PacketCaptureService.Instance.CurrentFilePath}";
            return;
        }

        _updatingPacketCaptureToggle = true;
        _packetCaptureEnabled.Checked = false;
        _updatingPacketCaptureToggle = false;
        _packetCapturePath.Text = $"Could not start capture: {error}";
    }

    private void FlushPacketCapture()
    {
        var followTail = IsFollowingPacketTail();
        _packetGrid.SuspendLayout();
        while (PacketCaptureService.Instance.TryDequeue(out var entry))
        {
            if (entry.Sequence == 0)
            {
                var statusRowIndex = _packetGrid.Rows.Add(
                    entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    string.Empty,
                    entry.Direction,
                    string.Empty,
                    entry.PayloadHex,
                    0,
                    false,
                    false,
                    string.Empty);
                _packetGrid.Rows[statusRowIndex].DefaultCellStyle.ForeColor = Color.DarkRed;
                continue;
            }

            if (PacketNameRegistry.Instance.IsHidden(entry.Direction, entry.Opcode))
                continue;

            var identity = new PacketRowIdentity(entry.Direction, entry.Opcode);
            var rowIndex = _packetGrid.Rows.Add(
                entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                entry.Sequence,
                entry.Direction,
                entry.Opcode,
                PacketNameRegistry.Instance.GetName(entry.Direction, entry.Opcode),
                entry.Length,
                entry.Encrypted,
                entry.Massive,
                FormatPayloadHex(entry.PayloadHex));
            _packetGrid.Rows[rowIndex].Tag = identity;
        }

        while (_packetGrid.Rows.Count > MaxVisiblePacketRows)
            _packetGrid.Rows.RemoveAt(0);

        _packetGrid.ResumeLayout();
        if (followTail && _packetGrid.Rows.Count > 0)
            _packetGrid.FirstDisplayedScrollingRowIndex = _packetGrid.Rows.Count - 1;

        if (_packetCaptureEnabled.Checked && !PacketCaptureService.Instance.Enabled)
        {
            _updatingPacketCaptureToggle = true;
            _packetCaptureEnabled.Checked = false;
            _updatingPacketCaptureToggle = false;
            _packetCapturePath.Text = $"Capture stopped. Last file: {PacketCaptureService.Instance.CurrentFilePath}";
        }
    }

    private static string FormatPayloadHex(string payloadHex)
    {
        if (string.IsNullOrEmpty(payloadHex) || payloadHex.Length <= 2)
            return payloadHex;

        var formatted = new StringBuilder(payloadHex.Length + payloadHex.Length / 2);
        for (var index = 0; index < payloadHex.Length; index += 2)
        {
            if (index > 0)
                formatted.Append(' ');

            formatted.Append(payloadHex, index, Math.Min(2, payloadHex.Length - index));
        }

        return formatted.ToString();
    }

    private static void AppendTextPreservingScroll(RichTextBox textBox, string text, int maximumCharacters)
    {
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        var scrollInfo = GetVerticalScrollInfo(textBox.Handle);
        var followTail = scrollInfo.nPos + scrollInfo.nPage >= scrollInfo.nMax;
        var overflow = textBox.TextLength + text.Length - maximumCharacters;
        if (overflow > 0 && textBox.TextLength > 0)
        {
            textBox.Select(0, Math.Min(overflow, textBox.TextLength));
            textBox.SelectedText = string.Empty;
            selectionStart = Math.Max(0, selectionStart - overflow);
        }

        textBox.AppendText(text);
        if (followTail)
        {
            textBox.SelectionStart = textBox.TextLength;
            textBox.SelectionLength = 0;
            textBox.ScrollToCaret();
        }
        else
        {
            var restoredStart = Math.Min(selectionStart, textBox.TextLength);
            textBox.Select(restoredStart, Math.Min(selectionLength, textBox.TextLength - restoredStart));
            scrollInfo.fMask = 0x4; // SIF_POS only; do not restore the old scrollbar range.
            SetScrollInfo(textBox.Handle, SbVert, ref scrollInfo, true);
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

    private readonly struct PacketRowIdentity
    {
        public PacketRowIdentity(string direction, string opcode)
        {
            Direction = direction;
            Opcode = opcode;
        }

        public string Direction { get; }
        public string Opcode { get; }
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
