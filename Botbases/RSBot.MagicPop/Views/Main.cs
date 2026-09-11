using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.MagicPop.Model;
using RSBot.MagicPop.References;

namespace RSBot.MagicPop.Views;

internal sealed class Main : UserControl
{
    private const string TargetsConfigKey = "RSBot.MagicPop.Targets";
    private const string CardCodeName = "ITEM_MALL_GACHA_CARD";

    private readonly ComboBox _degreeFilter;
    private readonly ComboBox _categoryFilter;
    private readonly ComboBox _raceFilter;
    private readonly ComboBox _rarityFilter;
    private readonly DataGridView _availableGrid;
    private readonly DataGridView _selectedGrid;
    private readonly Label _statusLabel;
    private readonly Label _referenceLabel;
    private readonly Label _currentTargetLabel;
    private readonly Label _countersLabel;
    private readonly DataGridView _diagnosticLog;
    private readonly List<GachaReward> _selectedTargets = new();
    private IReadOnlyList<GachaReward> _rewards = Array.Empty<GachaReward>();
    private string _targetConfigurationError;

    public Main()
    {
        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var magicPopTab = new TabPage("Magic POP") { Padding = new Padding(8), UseVisualStyleBackColor = true };
        var root = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var requirements = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.DarkRed,
            Height = 46,
            Margin = new Padding(3, 3, 3, 9),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Requirements: set the Return/recall point to Hotan and keep a usable return scroll in the inventory. " +
                   "After the return scroll is used, the game client will disconnect and the bot will continue in clientless mode."
        };
        root.Controls.Add(requirements, 0, 0);

        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        filters.Controls.Add(new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 3), Text = "Degree:" });
        _degreeFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        _degreeFilter.SelectedIndexChanged += (_, _) => RefreshAvailable();
        filters.Controls.Add(_degreeFilter);
        filters.Controls.Add(new Label { AutoSize = true, Margin = new Padding(16, 7, 3, 3), Text = "Category:" });
        _categoryFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        _categoryFilter.SelectedIndexChanged += (_, _) => RefreshAvailable();
        filters.Controls.Add(_categoryFilter);
        filters.Controls.Add(new Label { AutoSize = true, Margin = new Padding(16, 7, 3, 3), Text = "Race:" });
        _raceFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        _raceFilter.Items.AddRange(new object[] { "All", "CH", "EU" });
        _raceFilter.SelectedIndex = 0;
        _raceFilter.SelectedIndexChanged += (_, _) => RefreshAvailable();
        filters.Controls.Add(_raceFilter);
        filters.Controls.Add(new Label { AutoSize = true, Margin = new Padding(16, 7, 3, 3), Text = "Rarity:" });
        _rarityFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165 };
        _rarityFilter.Items.AddRange(new object[] { "All", "Seal of Star", "Seal of Moon", "Seal of Sun", "Seal of Nova" });
        _rarityFilter.SelectedItem = "Seal of Sun";
        _rarityFilter.SelectedIndexChanged += (_, _) => RefreshAvailable();
        filters.Controls.Add(_rarityFilter);
        root.Controls.Add(filters, 0, 1);

        _availableGrid = CreateGrid();
        _selectedGrid = CreateGrid();
        var transferButtons = new FlowLayoutPanel
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(8, 24, 8, 8),
            MinimumSize = new Size(150, 190),
            Padding = new Padding(7),
            WrapContents = false
        };
        transferButtons.Controls.Add(CreateButton("Add ->", AddSelectedTargets));
        transferButtons.Controls.Add(CreateButton("<- Remove", RemoveSelectedTargets));
        transferButtons.Controls.Add(CreateButton("Move up", () => MoveSelectedTarget(-1)));
        transferButtons.Controls.Add(CreateButton("Move down", () => MoveSelectedTarget(1)));

        var transfer = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, RowCount = 1 };
        transfer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transfer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        transfer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transfer.Controls.Add(CreateListPanel("Available rewards", _availableGrid), 0, 0);
        transfer.Controls.Add(transferButtons, 1, 0);
        transfer.Controls.Add(CreateListPanel("Selected targets (priority order)", _selectedGrid), 2, 0);
        root.Controls.Add(transfer, 0, 2);

        _diagnosticLog = CreateDiagnosticGrid();
        // Ensure InvokeRequired can reliably identify the owner thread even when
        // the first game-data callback arrives before the whole tab is displayed.
        _ = _diagnosticLog.Handle;
        root.Controls.Add(CreateDiagnosticPanel(), 0, 3);

        var status = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        _statusLabel = new Label { AutoSize = true, Text = "Status: stopped" };
        _referenceLabel = new Label { AutoSize = true, Text = "Reference data: waiting for game data" };
        _currentTargetLabel = new Label { AutoSize = true, Text = "Current target: none" };
        _countersLabel = new Label { AutoSize = true, Text = "Rolls: 0 | Wins: 0 | Losses: 0 | Remaining items: 0 | Remaining cards: 0" };
        status.Controls.Add(_statusLabel);
        status.Controls.Add(_referenceLabel);
        status.Controls.Add(_currentTargetLabel);
        status.Controls.Add(_countersLabel);
        root.Controls.Add(status, 0, 4);

        magicPopTab.Controls.Add(root);
        tabs.TabPages.Add(magicPopTab);
        Controls.Add(tabs);
    }

    public IReadOnlyList<GachaReward> SelectedTargets => _selectedTargets;
    public string TargetConfigurationError => _targetConfigurationError;

    public MagicPopTarget[] GetSelectedTargetsSnapshot()
    {
        if (InvokeRequired)
            return (MagicPopTarget[])Invoke(new Func<MagicPopTarget[]>(GetSelectedTargetsSnapshot));

        return _selectedTargets.Select(reward => new MagicPopTarget(reward)).ToArray();
    }

    public void BindReferenceCatalog(GachaReferenceCatalog catalog)
    {
        if (InvokeRequired)
        {
            if (IsHandleCreated && !IsDisposed && !Disposing)
                BeginInvoke(new Action<GachaReferenceCatalog>(BindReferenceCatalog), catalog);
            return;
        }

        _referenceLabel.Text = catalog.IsValid
            ? $"Reference data: ready ({catalog.Rewards.Count} rewards)"
            : $"Reference data: unavailable ({catalog.Error ?? "not loaded"})";
        _rewards = catalog.IsValid
            ? catalog.Rewards.Where(reward => reward.Category != "Unsupported").ToArray()
            : Array.Empty<GachaReward>();

        PopulateFilters();
        LoadTargets();
        RefreshLists();
    }

    public void AppendDiagnostic(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (_diagnosticLog.InvokeRequired)
        {
            if (_diagnosticLog.IsHandleCreated && !_diagnosticLog.IsDisposed && !_diagnosticLog.Disposing)
                _diagnosticLog.BeginInvoke(new Action<string>(AppendDiagnostic), message);
            return;
        }

        if (_diagnosticLog.IsDisposed || _diagnosticLog.Disposing)
            return;

        var keepAtBottom = _diagnosticLog.RowCount == 0
            || _diagnosticLog.FirstDisplayedScrollingRowIndex < 0
            || _diagnosticLog.FirstDisplayedScrollingRowIndex + _diagnosticLog.DisplayedRowCount(false) >= _diagnosticLog.RowCount;
        var rowIndex = _diagnosticLog.Rows.Add(DateTime.Now.ToString("HH:mm:ss.fff"), message);
        var row = _diagnosticLog.Rows[rowIndex];
        if (message.StartsWith("WIN:", StringComparison.OrdinalIgnoreCase))
            row.DefaultCellStyle.ForeColor = Color.Red;
        else if (message.StartsWith("LOSE:", StringComparison.OrdinalIgnoreCase))
            row.DefaultCellStyle.ForeColor = Color.Green;

        const int maximumRows = 2000;
        while (_diagnosticLog.RowCount > maximumRows)
            _diagnosticLog.Rows.RemoveAt(0);

        if (keepAtBottom && _diagnosticLog.RowCount > 0)
            _diagnosticLog.FirstDisplayedScrollingRowIndex = _diagnosticLog.RowCount - 1;
    }

    public void SetRunning(bool running)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<bool>(SetRunning), running);
            return;
        }

        _statusLabel.Text = running
            ? "Status: running"
            : "Status: stopped";
        _degreeFilter.Enabled = !running;
        _categoryFilter.Enabled = !running;
        _raceFilter.Enabled = !running;
        _rarityFilter.Enabled = !running;
        _availableGrid.Enabled = !running;
        _selectedGrid.Enabled = !running;
    }

    public void SetStatus(string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(SetStatus), status);
            return;
        }

        _statusLabel.Text = $"Status: {status}";
    }

    public void UpdateProgress(MagicPopRunState state, MagicPopTarget target, int rolls, int wins, int losses)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<MagicPopRunState, MagicPopTarget, int, int, int>(UpdateProgress), state, target, rolls, wins, losses);
            return;
        }

        _statusLabel.Text = $"Status: {state}";
        _currentTargetLabel.Text = target == null
            ? "Current target: none"
            : $"Current target: {target.DisplayName} [{target.CodeName}]";
        var remainingCards = Game.Player?.Inventory
            .Where(item => string.Equals(item.Record?.CodeName, CardCodeName, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Amount) ?? 0;
        _countersLabel.Text = $"Rolls: {rolls} | Wins: {wins} | Losses: {losses} | Remaining items: {_selectedTargets.Count} | Remaining cards: {remainingCards}";
    }

    public void RemoveCompletedTarget(uint gachaId)
    {
        if (InvokeRequired)
        {
            if (IsHandleCreated && !IsDisposed && !Disposing)
                BeginInvoke(new Action<uint>(RemoveCompletedTarget), gachaId);
            return;
        }

        var completedIndex = _selectedTargets.FindIndex(target => target.Source.GachaId == gachaId);
        if (completedIndex >= 0)
            _selectedTargets.RemoveAt(completedIndex);
        SaveTargets();
        RefreshLists();
    }

    private void PopulateFilters()
    {
        var selectedDegree = _degreeFilter.SelectedItem?.ToString();
        var selectedCategory = _categoryFilter.SelectedItem?.ToString();

        _degreeFilter.BeginUpdate();
        _degreeFilter.Items.Clear();
        _degreeFilter.Items.Add("All");
        foreach (var degree in _rewards.Select(reward => reward.Degree).Distinct().OrderBy(value => value))
            _degreeFilter.Items.Add(degree.ToString());
        _degreeFilter.SelectedItem = _degreeFilter.Items.Contains(selectedDegree) ? selectedDegree : "All";
        _degreeFilter.EndUpdate();

        _categoryFilter.BeginUpdate();
        _categoryFilter.Items.Clear();
        _categoryFilter.Items.Add("All");
        foreach (var category in _rewards.Select(reward => reward.Category).Distinct().OrderBy(value => value))
            _categoryFilter.Items.Add(category);
        _categoryFilter.SelectedItem = _categoryFilter.Items.Contains(selectedCategory) ? selectedCategory : "All";
        _categoryFilter.EndUpdate();
    }

    private void LoadTargets()
    {
        _selectedTargets.Clear();
        _targetConfigurationError = null;
        var rewardsByGachaId = _rewards.ToDictionary(reward => reward.Source.GachaId);
        var invalidEntries = new List<string>();

        foreach (var serialized in PlayerConfig.GetArray<string>(TargetsConfigKey))
        {
            var parts = serialized.Split('|');
            if (parts.Length != 3
                || !uint.TryParse(parts[0], out var gachaId)
                || !uint.TryParse(parts[1], out var refItemId)
                || !rewardsByGachaId.TryGetValue(gachaId, out var reward)
                || reward.Source.RefItemId != refItemId
                || !string.Equals(reward.CodeName, parts[2], StringComparison.Ordinal))
            {
                invalidEntries.Add(serialized);
                continue;
            }

            _selectedTargets.Add(reward);
        }

        if (invalidEntries.Count != 0)
        {
            _targetConfigurationError = $"{invalidEntries.Count} saved target(s) no longer match the loaded Gacha references. Remove or re-add the affected targets before starting.";
            Log.Warn($"[Magic POP] {_targetConfigurationError} Entries: {string.Join(", ", invalidEntries)}");
        }
    }

    private void SaveTargets()
    {
        _targetConfigurationError = null;
        global::RSBot.MagicPop.Container.Bot.ClearPendingRecovery();
        PlayerConfig.SetArray(
            TargetsConfigKey,
            _selectedTargets.Select(reward => $"{reward.Source.GachaId}|{reward.Source.RefItemId}|{reward.CodeName}")
        );
    }

    private void AddSelectedTargets()
    {
        var selected = GetSelectedRewards(_availableGrid).OrderBy(reward => reward.Name).ToArray();
        foreach (var reward in selected)
            _selectedTargets.Add(reward);

        SaveTargets();
        RefreshLists();
    }

    private void RemoveSelectedTargets()
    {
        foreach (var rowIndex in _selectedGrid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).OrderByDescending(index => index))
            _selectedTargets.RemoveAt(rowIndex);

        SaveTargets();
        RefreshLists();
    }

    private void MoveSelectedTarget(int offset)
    {
        if (_selectedGrid.SelectedRows.Count != 1)
            return;

        var reward = _selectedGrid.SelectedRows[0].Tag as GachaReward;
        var currentIndex = _selectedTargets.IndexOf(reward);
        var newIndex = currentIndex + offset;
        if (currentIndex < 0 || newIndex < 0 || newIndex >= _selectedTargets.Count)
            return;

        _selectedTargets.RemoveAt(currentIndex);
        _selectedTargets.Insert(newIndex, reward);
        SaveTargets();
        RefreshLists();
        _selectedGrid.Rows[newIndex].Selected = true;
    }

    private void RefreshLists()
    {
        RefreshAvailable();
        FillGrid(_selectedGrid, _selectedTargets);
    }

    private void RefreshAvailable()
    {
        if (_degreeFilter.SelectedItem == null || _categoryFilter.SelectedItem == null)
            return;

        var rewards = _rewards.AsEnumerable();
        if (_degreeFilter.SelectedItem.ToString() != "All"
            && int.TryParse(_degreeFilter.SelectedItem.ToString(), out var degree))
            rewards = rewards.Where(reward => reward.Degree == degree);

        var category = _categoryFilter.SelectedItem.ToString();
        if (category != "All")
            rewards = rewards.Where(reward => reward.Category == category);

        var race = _raceFilter.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(race) && race != "All")
            rewards = rewards.Where(reward => reward.Race == race);

        var rarity = _rarityFilter.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(rarity) && rarity != "All")
            rewards = rewards.Where(reward => reward.RarityFilterName == rarity);

        FillGrid(
            _availableGrid,
            rewards
                .OrderBy(reward => reward.Subtype)
                .ThenBy(GetRarityOrder)
                .ThenBy(reward => reward.Name)
        );
    }

    private static int GetRarityOrder(GachaReward reward)
    {
        if (reward.CodeName.EndsWith("_A_RARE", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (reward.CodeName.EndsWith("_B_RARE", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (reward.CodeName.EndsWith("_C_RARE", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 3;
    }

    private static IEnumerable<GachaReward> GetSelectedRewards(DataGridView grid)
    {
        return grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.Tag as GachaReward)
            .Where(reward => reward != null);
    }

    private static void FillGrid(DataGridView grid, IEnumerable<GachaReward> rewards)
    {
        grid.Rows.Clear();
        foreach (var reward in rewards)
        {
            var index = grid.Rows.Add(
                reward.Name,
                reward.Subtype
            );
            grid.Rows[index].Tag = reward;
        }
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Subtype", Width = 110 });
        return grid;
    }

    private static Control CreateListPanel(string title, Control content)
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(
            new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10.5f, FontStyle.Bold),
                Margin = new Padding(0, 2, 3, 5),
                Text = title
            },
            0,
            0
        );
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private Control CreateDiagnosticPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 4),
            MinimumSize = new Size(0, 115),
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(
            new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10.5f, FontStyle.Bold),
                Margin = new Padding(0, 6, 3, 3),
                Text = "Magic POP log"
            },
            0,
            0
        );
        var clear = new Button { AutoSize = true, Margin = new Padding(3, 0, 0, 3), Text = "Clear" };
        clear.Click += (_, _) => _diagnosticLog.Rows.Clear();
        header.Controls.Add(clear, 1, 0);

        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(_diagnosticLog, 0, 1);
        return panel;
    }

    private DataGridView CreateDiagnosticGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = true,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9),
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Time",
            Width = 135,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.False }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            HeaderText = "Message"
        });

        var contextMenu = new ContextMenuStrip();
        var copy = new ToolStripMenuItem("Copy");
        copy.Click += (_, _) => CopySelectedDiagnosticRows();
        contextMenu.Items.Add(copy);
        contextMenu.Opening += (_, _) => copy.Enabled = grid.SelectedRows.Count > 0;
        grid.ContextMenuStrip = contextMenu;
        grid.CellMouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Right || eventArgs.RowIndex < 0)
                return;
            if (!grid.Rows[eventArgs.RowIndex].Selected)
            {
                grid.ClearSelection();
                grid.Rows[eventArgs.RowIndex].Selected = true;
            }
        };
        return grid;
    }

    private void CopySelectedDiagnosticRows()
    {
        var selectedRows = _diagnosticLog.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .ToArray();
        if (selectedRows.Length == 0)
            return;

        var text = new StringBuilder();
        foreach (var row in selectedRows)
        {
            if (text.Length > 0)
                text.AppendLine();
            text.Append(row.Cells[0].Value).Append('\t').Append(row.Cells[1].Value);
        }
        Clipboard.SetText(text.ToString());
    }

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button
        {
            AutoSize = false,
            Height = 34,
            Margin = new Padding(3, 3, 3, 8),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = true,
            Width = 130
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}
