using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using RSBot.Alchemy.Bot;
using RSBot.Alchemy.Bundle.Enhance;
using RSBot.Alchemy.Helper;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using SDUI.Controls;

namespace RSBot.Alchemy.Views.Settings;

[ToolboxItem(false)]
public partial class EnhanceSettingsView : DoubleBufferedControl
{
    #region Member

    private InventoryItem _selectedItem;
    private bool _mainFormEventsSubscribed;
    private bool _updatingView;
    private readonly NumericUpDown _luckyUseFromPlus = new()
    {
        Location = new System.Drawing.Point(315, 136),
        Minimum = 1,
        Maximum = 255,
        Size = new System.Drawing.Size(62, 23),
        Value = 6,
        TabIndex = 4,
    };

    #endregion Member

    #region Constructor

    /// <summary>
    ///     Subscribes several events
    /// </summary>
    public EnhanceSettingsView()
    {
        CheckForIllegalCrossThreadCalls = false;
        InitializeComponent();
        var availableHeader = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(225, 136),
            Text = "Available",
        };
        var thresholdHeader = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(305, 136),
            Text = "Use Lucky from +",
        };
        Controls.Add(availableHeader);
        Controls.Add(thresholdHeader);
        Controls.Add(_luckyUseFromPlus);
        _luckyUseFromPlus.Location = new System.Drawing.Point(330, 162);
        checkUseLuckyStones.Location = new System.Drawing.Point(13, 158);
        checkUseImmortalStones.Location = new System.Drawing.Point(13, 192);
        checkUseAstralStones.Location = new System.Drawing.Point(13, 226);
        checkUseSteadyStones.Location = new System.Drawing.Point(13, 260);
        _luckyUseFromPlus.ValueChanged += config_CheckedChange;
        var tips = new ToolTip();
        tips.SetToolTip(_luckyUseFromPlus, "6 = apply Lucky before attempting +6. Enabled Immortal/Astral apply before +5; enabled Steady applies from +6.");
        tips.SetToolTip(checkUseImmortalStones, "When enabled, apply Immortal before attempting +5.");
        tips.SetToolTip(checkUseAstralStones, "When enabled, apply Astral after Immortal before attempting +5.");
        tips.SetToolTip(checkUseSteadyStones, "When enabled, apply Steady before attempting +6 and above.");
        lblLuckyCount.Location = new System.Drawing.Point(245, 164);
        lblImmortalCount.Location = new System.Drawing.Point(245, 198);
        lblAstralCount.Location = new System.Drawing.Point(245, 232);
        lblSteadyStonesCount.Location = new System.Drawing.Point(245, 266);
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true
        );

        EventManager.SubscribeEvent("OnLoadCharacter", SubscribeMainFormEvents);
    }

    #endregion Constructor

    internal class ElixirComboboxItem
    {
        public ElixirComboboxItem(IEnumerable<InventoryItem> items)
        {
            Items = items;
        }

        /// <summary>
        ///     Gets or sets the inventory item
        /// </summary>
        public IEnumerable<InventoryItem> Items { get; set; }

        public override string ToString()
        {
            if (!Items.Any())
                return string.Empty;

            return $"{Items.Sum(i => i.Amount)}x {Items.First().Record.GetRealName()}";
        }
    }

    #region Methods

    /// <summary>
    ///     Subscribes the ItemChanged event
    /// </summary>
    private void SubscribeMainFormEvents()
    {
        if (Globals.View == null || _mainFormEventsSubscribed)
            return;

        Globals.View.EngineChanged += View_EngineChanged;
        Globals.View.ItemChanged += View_ItemChanged;
        _mainFormEventsSubscribed = true;
    }

    /// <summary>
    ///     General UI update logic
    /// </summary>
    private void PopulateView()
    {
        _updatingView = true;
        try
        {
        if (Globals.View == null || Globals.View.SelectedItem == null)
        {
            Enabled = false;

            return;
        }

        _selectedItem = Globals.View.SelectedItem;

        lblCurrentOptLevel.Text = _selectedItem == null ? "+0" : $"+{Globals.View.SelectedItem.OptLevel}";

        var type = AlchemyItemHelper.ElixirType.Unspecified;

        var accessorryTypeId3 = new byte[] { 5, 12 };
        var armorTypeId3 = new byte[] { 1, 2, 3, 9, 10, 11 };

        if (_selectedItem.Record.TypeID3 == 4 && _selectedItem.Record.TypeID2 == 1)
            type = AlchemyItemHelper.ElixirType.Shield;

        if (_selectedItem.Record.TypeID3 == 6 && _selectedItem.Record.TypeID2 == 1)
            type = AlchemyItemHelper.ElixirType.Weapon;

        if (accessorryTypeId3.Contains(_selectedItem.Record.TypeID3) && _selectedItem.Record.TypeID2 == 1)
            type = AlchemyItemHelper.ElixirType.Accessory;

        if (armorTypeId3.Contains(_selectedItem.Record.TypeID3) && _selectedItem.Record.TypeID2 == 1)
            type = AlchemyItemHelper.ElixirType.Protector;

        var matchingElixirs = AlchemyItemHelper.GetElixirItems(_selectedItem.Record.Degree, type);

        comboElixir.Items.Clear();

        var index = 0;
        foreach (var items in matchingElixirs.GroupBy(i => i.ItemId))
        {
            comboElixir.Items.Add(new ElixirComboboxItem(items));

            if (items.Key == Globals.Botbase.EnhanceBundleConfig?.Elixirs?.FirstOrDefault()?.ItemId)
                comboElixir.SelectedIndex = index;

            index++;
        }

        if (comboElixir.Items.Count > 0 && comboElixir.SelectedItem == null)
            comboElixir.SelectedIndex = 0;

        var luckyPowders = AlchemyItemHelper.GetLuckyPowders(_selectedItem);
        lblLuckyPowderCount.Text = $"x{luckyPowders.Sum(i => i.Amount)}";

        var luckyStones = AlchemyItemHelper.GetLuckyStone(_selectedItem);
        checkUseLuckyStones.Enabled = luckyStones != null && luckyStones.Amount > 0;
        checkUseLuckyStones.Checked = Globals.Botbase.EnhanceBundleConfig?.UseLuckyStones ?? checkUseLuckyStones.Checked;
        _luckyUseFromPlus.Value = Math.Clamp(Globals.Botbase.EnhanceBundleConfig?.LuckyUseFromPlus ?? 6, (byte)1, (byte)255);
        _luckyUseFromPlus.Enabled = checkUseLuckyStones.Checked;
        lblLuckyCount.Text = luckyStones == null ? "x0" : $"x{luckyStones.Amount}";

        var astralStones = AlchemyItemHelper.GetAstralStone(_selectedItem);
        checkUseAstralStones.Checked = Globals.Botbase.EnhanceBundleConfig?.UseAstralStones
            ?? checkUseAstralStones.Checked;
        checkUseAstralStones.Enabled = astralStones?.Amount > 0;
        lblAstralCount.Text = astralStones == null ? "x0" : $"x{astralStones.Amount}";
        lblAstralCount.ForeColor = astralStones == null ? System.Drawing.Color.Firebrick : System.Drawing.Color.Black;

        var immortalStones = AlchemyItemHelper.GetImmortalStone(_selectedItem);
        checkUseImmortalStones.Checked = Globals.Botbase.EnhanceBundleConfig?.UseImmortalStones
            ?? checkUseImmortalStones.Checked;
        checkUseImmortalStones.Enabled = immortalStones?.Amount > 0;
        lblImmortalCount.Text = immortalStones == null ? "x0" : $"x{immortalStones.Amount}";
        lblImmortalCount.ForeColor = immortalStones == null ? System.Drawing.Color.Firebrick : System.Drawing.Color.Black;

        var steadyStones = AlchemyItemHelper.GetSteadyStone(_selectedItem);
        checkUseSteadyStones.Checked = Globals.Botbase.EnhanceBundleConfig?.UseSteadyStones
            ?? checkUseSteadyStones.Checked;
        checkUseSteadyStones.Enabled = steadyStones?.Amount > 0;
        lblSteadyStonesCount.Text = steadyStones == null ? "x0" : $"x{steadyStones.Amount}";
        lblSteadyStonesCount.ForeColor = steadyStones == null ? System.Drawing.Color.Firebrick : System.Drawing.Color.Black;

        Enabled = true;
        }
        finally
        {
            _updatingView = false;
        }
        if (Enabled && _selectedItem != null)
            config_CheckedChange(this, EventArgs.Empty);
    }

    #endregion Methods

    #region Events

    private void View_EngineChanged(InventoryItem item, AlchemyEngine alchemyEngine)
    {
        PopulateView();
    }

    /// <summary>
    ///     Will be triggered when the user click on the refresh link
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void linkRefreshItemList_Click(object sender, EventArgs e)
    {
        PopulateView();
    }

    /// <summary>
    ///     Will be triggered when the selected item changed
    /// </summary>
    /// <param name="item">The new item</param>
    private void View_ItemChanged(InventoryItem item)
    {
        PopulateView();
    }

    /// <summary>
    ///     Will be triggered when the user changed a setting
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void config_CheckedChange(object sender, EventArgs e)
    {
        if (_updatingView || Globals.Botbase == null || Globals.Botbase.AlchemyEngine != AlchemyEngine.Enhance)
            return;

        _luckyUseFromPlus.Enabled = checkUseLuckyStones.Checked;

        Globals.Botbase.EnhanceBundleConfig = new EnhanceBundleConfig
        {
            Item = Globals.View.SelectedItem,
            UseAstralStones = checkUseAstralStones.Checked,
            UseLuckyStones = checkUseLuckyStones.Checked,
            UseImmortalStones = checkUseImmortalStones.Checked,
            UseSteadyStones = checkUseSteadyStones.Checked,
            LuckyUseFromPlus = (byte)_luckyUseFromPlus.Value,
            Elixirs = (comboElixir.SelectedItem as ElixirComboboxItem)?.Items,
            MaxOptLevel = (byte)numMaxEnhancement.Value,
            StopIfLuckyPowderEmpty = checkStopLuckyPowder.Checked,
        };
    }

    #endregion Events
}
