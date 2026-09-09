using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using WinCheckBox = System.Windows.Forms.CheckBox;
using WinGroupBox = System.Windows.Forms.GroupBox;
using WinLabel = System.Windows.Forms.Label;
using SduiGroupBox = SDUI.Controls.GroupBox;

namespace RSBot.Items.Views;

public partial class Main
{
    private readonly Dictionary<string, WinCheckBox> _pickupCategoryChecks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WinCheckBox> _storeCategoryChecks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WinCheckBox> _pickupRareRuleChecks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WinCheckBox> _storeRareRuleChecks = new(StringComparer.Ordinal);
    private readonly Dictionary<int, WinCheckBox> _storeEquipmentDegreeChecks = new();
    private WinCheckBox _checkSellUnselectedEquipment;
    private WinCheckBox _checkStoreMale;
    private WinCheckBox _checkStoreFemale;
    private FlowLayoutPanel _supplyRulesPanel;
    private readonly List<ItemRuleTestOption> _itemTestOptions = new();
    private ComboBox _itemTestCodeName;
    private ItemRuleTestOption _selectedItemTestOption;
    private WinLabel _itemTestResult;
    private Timer _itemTestDebounceTimer;
    private string[] _visibleItemTestSuggestions = Array.Empty<string>();
    private bool _updatingItemTestOptions;
    private bool _loadingCategoryRules;

    private void InitializeCategoryRulesUi()
    {
        // Replaced by the detailed rare rule matrix below. The legacy value is
        // still read once for migration, but no longer drives pickup decisions.
        checkPickupRare.Visible = false;

        var optionsLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(16, 18, 16, 8),
            RowCount = 2,
        };
        for (var column = 0; column < optionsLayout.ColumnCount; column++)
            optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var optionChecks = new[]
        {
            checkPickupGold,
            checkPickupBlue,
            checkQuestItems,
            checkAllEquips,
            checkEverything,
        };
        foreach (var checkBox in optionChecks)
        {
            checkBox.Anchor = AnchorStyles.Left;
            checkBox.Margin = new Padding(3);
        }

        groupBoxOptions.Controls.Clear();
        groupBoxOptions.Controls.Add(optionsLayout);
        optionsLayout.Controls.Add(checkPickupGold, 0, 0);
        optionsLayout.Controls.Add(checkPickupBlue, 1, 0);
        optionsLayout.Controls.Add(checkQuestItems, 2, 0);
        optionsLayout.Controls.Add(checkAllEquips, 0, 1);
        optionsLayout.Controls.Add(checkEverything, 1, 1);

        tabPage1.Controls.Clear();
        var pageContent = new TableLayoutPanel
        {
            AutoScroll = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            RowCount = 3,
        };
        pageContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, groupBoxGeneral.Height + 10));
        pageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, groupBoxOptions.Height + 10));
        pageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 810));
        tabPage1.Controls.Add(pageContent);

        groupBoxGeneral.Dock = DockStyle.Fill;
        groupBoxOptions.Dock = DockStyle.Fill;
        groupBoxGeneral.Margin = new Padding(0, 0, 0, 8);
        groupBoxOptions.Margin = new Padding(0, 0, 0, 8);
        pageContent.Controls.Add(groupBoxGeneral, 0, 0);
        pageContent.Controls.Add(groupBoxOptions, 0, 1);

        var rulesFrame = new SduiGroupBox
        {
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Name = "groupItemRules",
            Padding = new Padding(8, 18, 8, 8),
            Radius = 10,
            ShadowDepth = 4,
            Text = "Item rules",
        };
        pageContent.Controls.Add(rulesFrame, 0, 2);

        var rulesLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3,
        };
        rulesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        rulesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        rulesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rulesFrame.Controls.Add(rulesLayout);
        rulesLayout.Controls.Add(CreateItemRuleTester(), 0, 0);
        rulesLayout.Controls.Add(CreateEquipmentStoreBehaviorPanel(), 0, 1);

        var categoryTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 4),
        };
        var rareTab = new TabPage("Rare items") { AutoScroll = true, BackColor = Color.White };
        var equipmentTab = new TabPage("Equipment") { AutoScroll = true, BackColor = Color.White };
        var suppliesTab = new TabPage("Potions / supplies") { AutoScroll = true, BackColor = Color.White };
        categoryTabs.TabPages.Add(rareTab);
        categoryTabs.TabPages.Add(equipmentTab);
        categoryTabs.TabPages.Add(suppliesTab);
        rulesLayout.Controls.Add(categoryTabs, 0, 2);

        BuildRareRulesUi(rareTab);
        BuildEquipmentRulesUi(equipmentTab);

        _supplyRulesPanel = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8),
            WrapContents = false,
        };
        suppliesTab.Controls.Add(_supplyRulesPanel);
        _supplyRulesPanel.SizeChanged += (_, _) => ResizeSupplyRuleGroups();
        BuildSupplyRulesUi();

    }

    private Control CreateEquipmentStoreBehaviorPanel()
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 0, 3, 4),
            RowCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _checkSellUnselectedEquipment = CreatePlainCheckBox(
            "Sell equipment not selected for storage (including rare items)",
            Point.Empty
        );
        _checkSellUnselectedEquipment.Anchor = AnchorStyles.Left;
        _checkSellUnselectedEquipment.Margin = new Padding(3, 0, 0, 0);
        _checkSellUnselectedEquipment.CheckedChanged += CategoryRuleSettingsChanged;
        layout.Controls.Add(_checkSellUnselectedEquipment, 0, 0);
        return layout;
    }

    private Control CreateItemRuleTester()
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var instructions = CreateGridLabel(
            "Search by item codename or display name, then press Test to see what happens to an item",
            ContentAlignment.MiddleLeft
        );
        instructions.Margin = new Padding(3, 0, 3, 2);
        layout.Controls.Add(instructions, 0, 0);
        layout.SetColumnSpan(instructions, 3);

        var codeNameLabel = new WinLabel
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(3, 0, 8, 0),
            Text = "Item:",
        };
        layout.Controls.Add(codeNameLabel, 0, 1);

        _itemTestCodeName = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownHeight = 360,
            DropDownStyle = ComboBoxStyle.DropDown,
            IntegralHeight = false,
            Margin = new Padding(3, 5, 6, 5),
            MaxDropDownItems = 16,
        };
        _itemTestDebounceTimer = new Timer { Interval = 250 };
        _itemTestDebounceTimer.Tick += (_, _) =>
        {
            _itemTestDebounceTimer.Stop();
            UpdateItemRuleSuggestions(openDropDown: true);
        };
        _itemTestCodeName.Disposed += (_, _) =>
        {
            _itemTestDebounceTimer?.Stop();
            _itemTestDebounceTimer?.Dispose();
            _itemTestDebounceTimer = null;
        };
        _itemTestCodeName.DropDown += (_, _) => UpdateItemRuleSuggestions(openDropDown: false);
        _itemTestCodeName.TextUpdate += (_, _) =>
        {
            if (_updatingItemTestOptions)
                return;
            _selectedItemTestOption = null;
            if (_itemTestResult != null)
            {
                _itemTestResult.ForeColor = Color.DimGray;
                _itemTestResult.Text = string.Empty;
            }

            _itemTestDebounceTimer.Stop();
            _itemTestDebounceTimer.Start();
        };
        _itemTestCodeName.SelectionChangeCommitted += (_, _) =>
        {
            _selectedItemTestOption = _itemTestCodeName.SelectedItem as ItemRuleTestOption;
            TestItemRules();
        };
        _itemTestCodeName.SizeChanged += (_, _) =>
            _itemTestCodeName.DropDownWidth = Math.Max(_itemTestCodeName.Width, 650);
        _itemTestCodeName.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            TestItemRules();
            e.SuppressKeyPress = true;
        };
        layout.Controls.Add(_itemTestCodeName, 1, 1);

        var testButton = new Button
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 4, 2, 4),
            MinimumSize = new Size(128, 42),
            Text = "Test",
        };
        testButton.Click += (_, _) => TestItemRules();
        layout.Controls.Add(testButton, 2, 1);

        _itemTestResult = CreateGridLabel(
            string.Empty,
            ContentAlignment.MiddleLeft
        );
        _itemTestResult.AutoEllipsis = true;
        _itemTestResult.ForeColor = Color.DimGray;
        _itemTestResult.Margin = new Padding(3, 9, 3, 0);
        layout.Controls.Add(_itemTestResult, 0, 2);
        layout.SetColumnSpan(_itemTestResult, 3);
        return layout;
    }

    private void TestItemRules()
    {
        var query = _itemTestCodeName.Text.Trim();
        var selected = _itemTestCodeName.SelectedItem as ItemRuleTestOption;
        var rememberedSelection = _selectedItemTestOption;
        var exactMatches = _itemTestOptions.Where(option => option.Matches(query)).ToArray();
        var item = selected?.Item
            ?? (rememberedSelection?.Matches(query) == true ? rememberedSelection.Item : null)
            ?? (exactMatches.Length == 1 ? exactMatches[0].Item : null);
        if (item == null)
        {
            if (string.IsNullOrEmpty(query))
            {
                _itemTestResult.ForeColor = Color.DimGray;
                _itemTestResult.Text = string.Empty;
                return;
            }

            _itemTestResult.ForeColor = Color.Firebrick;
            _itemTestResult.Text = exactMatches.Length > 1
                ? "Multiple items have this name; select the exact codename from the list"
                : "Select a matching item from the list";
            return;
        }

        var pickup = PickupManager.WouldPickupByItemRules(item) ? "Pickup" : "Ignore";
        var townAction = ShoppingManager.WouldStore(item)
            ? "Store"
            : ShoppingManager.WouldSell(item) ? "Sell" : "Keep";
        _itemTestResult.ForeColor = townAction switch
        {
            "Store" => Color.ForestGreen,
            "Sell" => Color.Firebrick,
            _ => Color.FromArgb(45, 100, 190),
        };
        _itemTestResult.Text = $"{pickup} → {townAction}  |  D{item.Degree}  |  {item.GetRealName(true)}";
    }

    private void RefreshItemRuleTestOptions()
    {
        if (_itemTestCodeName == null)
            return;

        _itemTestOptions.Clear();
        _itemTestOptions.AddRange(
            (Game.ReferenceManager?.ItemData.Values ?? Enumerable.Empty<RefObjItem>())
                .Where(item => !string.IsNullOrWhiteSpace(item.CodeName))
                .Select(item => new ItemRuleTestOption(item, item.GetRealName(true)))
                .OrderBy(option => option.DisplayName)
                .ThenBy(option => option.CodeName)
        );
        _visibleItemTestSuggestions = Array.Empty<string>();
        UpdateItemRuleSuggestions(openDropDown: false);
    }

    private void UpdateItemRuleSuggestions(bool openDropDown)
    {
        if (_itemTestCodeName == null || _updatingItemTestOptions)
            return;

        var enteredText = _itemTestCodeName.Text;
        var query = enteredText.Trim();
        var typedSelectionStart = _itemTestCodeName.SelectionStart;
        var typedSelectionLength = _itemTestCodeName.SelectionLength;
        var matchingOptions = _itemTestOptions
            .Where(option =>
                string.IsNullOrEmpty(query)
                || option.CodeName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || option.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            )
            .Take(250)
            .ToArray();
        var suggestionKeys = matchingOptions.Select(option => option.CodeName).ToArray();
        if (_visibleItemTestSuggestions.SequenceEqual(suggestionKeys, StringComparer.Ordinal))
            return;
        var matches = matchingOptions.Cast<object>().ToArray();

        _updatingItemTestOptions = true;
        try
        {
            _itemTestCodeName.BeginUpdate();
            _itemTestCodeName.Items.Clear();
            _itemTestCodeName.Items.AddRange(matches);
            _itemTestCodeName.SelectedIndex = -1;
            _itemTestCodeName.Text = enteredText;
            _itemTestCodeName.SelectionStart = Math.Min(typedSelectionStart, enteredText.Length);
            _itemTestCodeName.SelectionLength = Math.Min(typedSelectionLength, enteredText.Length - _itemTestCodeName.SelectionStart);
            _itemTestCodeName.EndUpdate();
            _visibleItemTestSuggestions = suggestionKeys;
            _itemTestCodeName.DropDownHeight = 360;
            if (openDropDown && _itemTestCodeName.Focused && matches.Length > 0 && !_itemTestCodeName.DroppedDown)
                _itemTestCodeName.DroppedDown = true;
        }
        finally
        {
            _updatingItemTestOptions = false;
        }
    }

    private void BuildRareRulesUi(TabPage rareTab)
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Height = 660,
            Padding = new Padding(8),
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var explanation = CreateGridLabel("Rare equipment is controlled exclusively by this tab.", ContentAlignment.MiddleLeft);
        explanation.ForeColor = Color.FromArgb(45, 100, 190);
        layout.Controls.Add(explanation, 0, 0);
        layout.Controls.Add(CreateRareRuleSelector("Pickup", _pickupRareRuleChecks), 0, 1);
        layout.Controls.Add(CreateRareRuleSelector("Store", _storeRareRuleChecks), 0, 2);
        rareTab.Controls.Add(layout);
    }

    private Control CreateRareRuleSelector(
        string title,
        IDictionary<string, WinCheckBox> target
    )
    {
        var group = new WinGroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5),
            Padding = new Padding(8),
            Text = title,
        };
        var table = new TableLayoutPanel { ColumnCount = 11, Dock = DockStyle.Fill, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        for (var degree = 1; degree <= 10; degree++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var degreeLabel = CreateGridLabel("Degree", ContentAlignment.MiddleLeft);
        degreeLabel.Padding = new Padding(0, 0, 0, 6);
        table.Controls.Add(degreeLabel, 0, 0);
        for (var degree = 1; degree <= 10; degree++)
        {
            var degreeNumber = CreateGridLabel(degree.ToString());
            degreeNumber.Padding = new Padding(0, 0, 0, 6);
            table.Controls.Add(degreeNumber, degree, 0);
        }

        var normalTypes = new[]
        {
            (Label: "Seal of Star", Key: "Star"),
            (Label: "Seal of Moon", Key: "Moon"),
            (Label: "Seal of Sun", Key: "Sun"),
        };
        for (var row = 0; row < normalTypes.Length; row++)
        {
            table.Controls.Add(CreateGridLabel(normalTypes[row].Label, ContentAlignment.MiddleLeft), 0, row + 1);
            for (var degree = 1; degree <= 10; degree++)
            {
                var check = CreateRareRuleCheck(target, $"{normalTypes[row].Key}.{degree}");
                check.Dock = DockStyle.Fill;
                check.CheckAlign = ContentAlignment.MiddleCenter;
                table.Controls.Add(check, degree, row + 1);
            }
        }

        var special = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 7, 0, 0),
            WrapContents = true,
        };
        special.Controls.Add(CreateRareRuleCheck(target, "RocSet", "Roc Set"));
        special.Controls.Add(CreateRareRuleCheck(target, "Nova", "11D Seal of Nova"));
        special.Controls.Add(CreateRareRuleCheck(target, "NovaSetA", "11D Seal of Nova (A)"));
        special.Controls.Add(CreateRareRuleCheck(target, "NovaSetB", "11D Seal of Nova (B)"));
        table.Controls.Add(special, 0, 4);
        table.SetColumnSpan(special, 11);
        group.Controls.Add(table);
        return group;
    }

    private WinCheckBox CreateRareRuleCheck(
        IDictionary<string, WinCheckBox> target,
        string key,
        string text = ""
    )
    {
        var check = CreatePlainCheckBox(text, Point.Empty);
        check.Tag = key;
        check.CheckedChanged += CategoryRuleSettingsChanged;
        target[key] = check;
        return check;
    }

    private void BuildEquipmentRulesUi(TabPage equipmentTab)
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Height = 1358,
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 560));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 350));
        equipmentTab.Controls.Add(layout);

        var explanation = CreateGridLabel(
            "Choose which equipment to store.\r\n"
            + "With only Degrees selected, every equipment item of those degrees is stored.\r\n"
            + "Selecting types narrows this to those types; Male/Female further narrows clothes. These rules apply only to normal equipment; rare equipment is controlled exclusively by the Rare items tab.",
            ContentAlignment.MiddleLeft
        );
        explanation.AutoEllipsis = false;
        explanation.ForeColor = Color.FromArgb(45, 100, 190);
        explanation.Margin = new Padding(14, 6, 14, 4);
        layout.Controls.Add(explanation, 0, 0);

        var equipmentRules = new WinGroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 6, 8, 4),
            Text = "Equipment rules",
        };
        var equipmentRuleLayout = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, RowCount = 1 };
        equipmentRuleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        equipmentRuleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        equipmentRuleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        equipmentRuleLayout.Controls.Add(CreateGridLabel("Degrees", ContentAlignment.MiddleLeft), 0, 0);
        var degreePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        AddDegreeChecks(degreePanel, _storeEquipmentDegreeChecks);
        equipmentRuleLayout.Controls.Add(degreePanel, 1, 0);
        equipmentRules.Controls.Add(equipmentRuleLayout);
        layout.Controls.Add(equipmentRules, 0, 1);

        var clothes = new WinGroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 4, 8, 4),
            Text = "Clothes",
        };
        var clothesLayout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 3 };
        clothesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        clothesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        clothesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        clothesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var genderPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _checkStoreMale = CreatePlainCheckBox("Male", Point.Empty);
        _checkStoreFemale = CreatePlainCheckBox("Female", Point.Empty);
        _checkStoreMale.CheckedChanged += CategoryRuleSettingsChanged;
        _checkStoreFemale.CheckedChanged += CategoryRuleSettingsChanged;
        genderPanel.Controls.Add(_checkStoreMale);
        genderPanel.Controls.Add(_checkStoreFemale);
        clothesLayout.Controls.Add(genderPanel, 0, 0);
        clothesLayout.Controls.Add(
            CreateClothesMatrix(
                "Chinese",
                new[] { ("Protector", "Protector"), ("Armor", "Armor"), ("Garment", "Garment") }
            ),
            0,
            1
        );
        clothesLayout.Controls.Add(
            CreateClothesMatrix(
                "European",
                new[] { ("Light Armor", "LightArmor"), ("Heavy Armor", "HeavyArmor"), ("Robe", "Robe") }
            ),
            0,
            2
        );
        clothes.Controls.Add(clothesLayout);
        layout.Controls.Add(clothes, 0, 2);

        var accessory = new WinGroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 4, 8, 4),
            Text = "Accessory",
        };
        var accessoryLayout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 2 };
        accessoryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        accessoryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        accessoryLayout.Controls.Add(CreateAccessoryPanel("Chinese"), 0, 0);
        accessoryLayout.Controls.Add(CreateAccessoryPanel("European"), 0, 1);
        accessory.Controls.Add(accessoryLayout);
        layout.Controls.Add(accessory, 0, 3);

        var weapons = new WinGroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 4, 8, 8),
            Text = "Weapons",
        };
        var weaponLayout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 2 };
        weaponLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        weaponLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        weaponLayout.Controls.Add(
            CreateWeaponPanel(
                "Chinese",
                new[] { "Sword", "Blade", "Spear", "Glaive", "Bow", "Shield" }
            ),
            0,
            0
        );
        weaponLayout.Controls.Add(
            CreateWeaponPanel(
                "European",
                new[]
                {
                    "Sword", "Two-Handed Sword", "Axe", "Warlock Rod", "Staff", "Crossbow", "Dagger", "Harp",
                    "Cleric Rod", "Shield",
                }
            ),
            0,
            1
        );
        weapons.Controls.Add(weaponLayout);
        layout.Controls.Add(weapons, 0, 4);
        equipmentTab.AutoScrollMinSize = new Size(0, 1358);
    }

    private void AddDegreeChecks(Control parent, IDictionary<int, WinCheckBox> target)
    {
        for (var degree = 1; degree <= 12; degree++)
        {
            var check = CreatePlainCheckBox(degree.ToString(), Point.Empty);
            check.Margin = new Padding(1, 6, 1, 8);
            check.Tag = degree;
            check.CheckedChanged += CategoryRuleSettingsChanged;
            target[degree] = check;
            parent.Controls.Add(check);
        }
    }

    private Control CreateClothesMatrix(string country, (string Label, string Key)[] types)
    {
        var group = new WinGroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5),
            Text = country,
        };
        var slots = new[] { "Head", "Shoulder", "Chest", "Pants", "Bracer", "Boots" };
        var table = new TableLayoutPanel
        {
            ColumnCount = 7,
            Dock = DockStyle.Fill,
            Padding = new Padding(5),
            RowCount = 4,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        for (var i = 0; i < slots.Length; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        for (var row = 0; row < types.Length; row++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / types.Length));

        table.Controls.Add(CreateGridLabel("Type", ContentAlignment.MiddleLeft), 0, 0);
        for (var column = 0; column < slots.Length; column++)
            table.Controls.Add(CreateGridLabel(slots[column]), column + 1, 0);

        for (var row = 0; row < types.Length; row++)
        {
            var rowChecks = new List<WinCheckBox>();
            var rowSelector = CreatePlainCheckBox(types[row].Label, Point.Empty);
            rowSelector.Anchor = AnchorStyles.Left;
            rowSelector.AutoCheck = false;
            rowSelector.ThreeState = true;
            rowSelector.Margin = new Padding(3);
            table.Controls.Add(rowSelector, 0, row + 1);
            for (var column = 0; column < slots.Length; column++)
            {
                var key = $"Clothes.{country}.{types[row].Key}.{slots[column]}";
                var slotCheck = CreateCategoryCheckBox(key, false);
                rowChecks.Add(slotCheck);
                table.Controls.Add(slotCheck, column + 1, row + 1);
            }

            ConfigureThreeStateRowSelector(rowSelector, rowChecks);
        }

        group.Controls.Add(table);
        return group;
    }

    private void ConfigureThreeStateRowSelector(
        WinCheckBox rowSelector,
        IReadOnlyCollection<WinCheckBox> rowChecks
    )
    {
        void UpdateSelector()
        {
            var checkedCount = rowChecks.Count(check => check.Checked);
            rowSelector.CheckState = checkedCount switch
            {
                0 => CheckState.Unchecked,
                _ when checkedCount == rowChecks.Count => CheckState.Checked,
                _ => CheckState.Indeterminate,
            };
        }

        foreach (var check in rowChecks)
            check.CheckedChanged += (_, _) => UpdateSelector();

        rowSelector.Click += (_, _) =>
        {
            var shouldCheck = rowSelector.CheckState != CheckState.Checked;
            _loadingCategoryRules = true;
            try
            {
                foreach (var check in rowChecks)
                    check.Checked = shouldCheck;
                UpdateSelector();
            }
            finally
            {
                _loadingCategoryRules = false;
            }

            CategoryRuleSettingsChanged(rowSelector, EventArgs.Empty);
        };
        UpdateSelector();
    }

    private Control CreateAccessoryPanel(string country)
    {
        var group = new WinGroupBox { Dock = DockStyle.Fill, Margin = new Padding(5), Text = country };
        var table = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, RowCount = 1 };
        var types = new[] { "Ring", "Earring", "Necklace" };
        for (var index = 0; index < types.Length; index++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        for (var index = 0; index < types.Length; index++)
        {
            var check = CreateCategoryCheckBox($"Accessory.{country}.{types[index]}", false, types[index]);
            check.Dock = DockStyle.Fill;
            check.Margin = new Padding(10, 5, 5, 3);
            table.Controls.Add(check, index, 0);
        }

        group.Controls.Add(table);
        return group;
    }

    private Control CreateWeaponPanel(string country, string[] labels)
    {
        var group = new WinGroupBox { Dock = DockStyle.Fill, Margin = new Padding(5), Text = country };
        var columnCount = country == "Chinese" ? 3 : 5;
        var rowCount = (int)Math.Ceiling(labels.Length / (double)columnCount);
        var table = new TableLayoutPanel
        {
            ColumnCount = columnCount,
            Dock = DockStyle.Fill,
            Padding = new Padding(5),
            RowCount = rowCount,
        };
        for (var column = 0; column < columnCount; column++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columnCount));
        for (var row = 0; row < rowCount; row++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rowCount));
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            var keyPart = label.Replace("-", string.Empty).Replace(" ", string.Empty);
            var check = CreateCategoryCheckBox($"Weapon.{country}.{keyPart}", false, label);
            check.Dock = DockStyle.Fill;
            check.Margin = new Padding(8, 3, 5, 3);
            table.Controls.Add(check, index % columnCount, index / columnCount);
        }

        group.Controls.Add(table);
        return group;
    }

    private void BuildSupplyRulesUi()
    {
        if (_supplyRulesPanel == null)
            return;

        foreach (var key in _pickupCategoryChecks.Keys.Where(key => key.StartsWith("Supply.")).ToArray())
            _pickupCategoryChecks.Remove(key);
        foreach (var key in _storeCategoryChecks.Keys.Where(key => key.StartsWith("Supply.")).ToArray())
            _storeCategoryChecks.Remove(key);

        _supplyRulesPanel.SuspendLayout();
        _supplyRulesPanel.Controls.Clear();

        var potionRows = (Game.ReferenceManager?.ItemData.Values ?? Enumerable.Empty<RefObjItem>())
            .Where(item => ItemCategoryRules.GetSupplyGroup(item) is "HP" or "MP" or "Vigor" or "Pill")
            .Where(item => item.CashItem == 0 && !item.CodeName.Contains("_EVENT_", StringComparison.OrdinalIgnoreCase))
            .Select(item => new
            {
                Item = item,
                Key = ItemCategoryRules.GetSupplyKey(item),
                Group = ItemCategoryRules.GetSupplyGroup(item),
                Label = item.GetRealName(),
                item.ReqLevel1,
            })
            .Where(item => item.Key != null && !string.IsNullOrWhiteSpace(item.Label))
            .GroupBy(item => item.Key)
            .Select(group => group.OrderBy(item => item.ReqLevel1).First())
            .Where(item => ItemCategoryRules.GetPotionItemOrder(item.Item) >= 0)
            .OrderBy(item => Array.IndexOf(new[] { "HP", "MP", "Vigor", "Pill" }, item.Group))
            .ThenBy(item => ItemCategoryRules.GetPotionItemOrder(item.Item))
            .ThenBy(item => item.ReqLevel1)
            .ThenBy(item => item.Label)
            .Select(item => (item.Group, item.Label, item.Key))
            .ToArray();

        if (potionRows.Length == 0)
        {
            _supplyRulesPanel.Controls.Add(
                new WinLabel
                {
                    AutoSize = true,
                    Margin = new Padding(8),
                    Text = "Potion categories become available after the game data has loaded.",
                }
            );
        }
        else
        {
            _supplyRulesPanel.Controls.Add(CreatePotionRuleTabs(potionRows));
        }

        _supplyRulesPanel.Controls.Add(
            CreatePickupStoreGrid(
                "Elixirs",
                new[]
                {
                    ("Weapon Elixir", "Supply.Elixir.Weapon"),
                    ("Shield Elixir", "Supply.Elixir.Shield"),
                    ("Protector Elixir", "Supply.Elixir.Protector"),
                    ("Accessory Elixir", "Supply.Elixir.Accessory"),
                }
            )
        );
        _supplyRulesPanel.Controls.Add(
            CreatePickupStoreGrid(
                "Arrow / Bolt",
                new[] { ("Arrow", "Supply.Arrow"), ("Bolt", "Supply.Bolt") }
            )
        );

        LoadCategoryRuleSettings();
        RefreshItemRuleTestOptions();
        _supplyRulesPanel.ResumeLayout();
        ResizeSupplyRuleGroups();
    }

    private void ResizeSupplyRuleGroups()
    {
        if (_supplyRulesPanel == null)
            return;

        var width = Math.Max(
            500,
            _supplyRulesPanel.ClientSize.Width
                - _supplyRulesPanel.Padding.Horizontal
                - SystemInformation.VerticalScrollBarWidth
                - 8
        );
        foreach (Control control in _supplyRulesPanel.Controls)
        {
            if (control is WinGroupBox)
                control.Width = width;
        }
    }

    private Control CreatePickupStoreGrid(string title, (string Label, string Key)[] rows)
    {
        var group = new WinGroupBox
        {
            Margin = new Padding(4, 4, 4, 10),
            Size = new Size(835, 90 + rows.Length * 36),
            Text = title,
        };
        group.Controls.Add(CreatePickupStoreTable(rows));
        return group;
    }

    private Control CreatePotionRuleTabs((string Group, string Label, string Key)[] rows)
    {
        var group = new WinGroupBox
        {
            Margin = new Padding(4, 4, 4, 10),
            Size = new Size(835, 390),
            Text = "Potions",
        };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var groupName in new[] { "HP", "MP", "Vigor", "Pill" })
        {
            var page = new TabPage(groupName) { AutoScroll = true, BackColor = Color.White };
            var groupRows = rows
                .Where(row => row.Group == groupName)
                .Select(row => (row.Label, row.Key))
                .ToArray();
            var table = CreatePickupStoreTable(groupRows);
            table.Dock = DockStyle.Top;
            table.Height = 58 + groupRows.Length * 36;
            page.Controls.Add(table);
            tabs.TabPages.Add(page);
        }

        group.Controls.Add(tabs);
        return group;
    }

    private Control CreatePickupStoreTable((string Label, string Key)[] rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 5, 8, 5),
            RowCount = rows.Length + 1,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        for (var row = 0; row < rows.Length; row++)
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var pickupChecks = new List<WinCheckBox>();
        var storeChecks = new List<WinCheckBox>();

        for (var row = 0; row < rows.Length; row++)
        {
            table.Controls.Add(CreateGridLabel(rows[row].Label, ContentAlignment.MiddleLeft), 0, row + 1);
            var pickupCheck = CreateCategoryCheckBox(rows[row].Key, true);
            var storeCheck = CreateCategoryCheckBox(rows[row].Key, false);
            pickupChecks.Add(pickupCheck);
            storeChecks.Add(storeCheck);
            table.Controls.Add(pickupCheck, 1, row + 1);
            table.Controls.Add(storeCheck, 2, row + 1);
        }

        table.Controls.Add(CreateRuleHeader("Pickup", pickupChecks), 1, 0);
        table.Controls.Add(CreateRuleHeader("Store", storeChecks), 2, 0);

        return table;
    }

    private Control CreateRuleHeader(string text, IReadOnlyCollection<WinCheckBox> children)
    {
        var toggle = new WinCheckBox
        {
            Anchor = AnchorStyles.None,
            AutoCheck = false,
            AutoSize = true,
            CheckAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(3),
            Text = text,
            ThreeState = true,
        };
        var updatingHeader = false;

        void updateHeader()
        {
            updatingHeader = true;
            try
            {
                var checkedCount = children.Count(child => child.Checked);
                toggle.CheckState = checkedCount switch
                {
                    0 => CheckState.Unchecked,
                    _ when checkedCount == children.Count => CheckState.Checked,
                    _ => CheckState.Indeterminate,
                };
            }
            finally
            {
                updatingHeader = false;
            }
        }

        foreach (var child in children)
            child.CheckedChanged += (_, _) => updateHeader();

        toggle.Click += (_, _) =>
        {
            if (updatingHeader)
                return;

            var shouldCheck = toggle.CheckState != CheckState.Checked;
            _loadingCategoryRules = true;
            try
            {
                foreach (var child in children)
                    child.Checked = shouldCheck;
                updateHeader();
            }
            finally
            {
                _loadingCategoryRules = false;
            }

            CategoryRuleSettingsChanged(toggle, EventArgs.Empty);
        };

        updateHeader();
        return toggle;
    }

    private WinCheckBox CreateCategoryCheckBox(string key, bool pickup, string text = "")
    {
        var check = new WinCheckBox
        {
            AutoSize = true,
            CheckAlign = string.IsNullOrEmpty(text)
                ? ContentAlignment.MiddleCenter
                : ContentAlignment.MiddleLeft,
            Dock = string.IsNullOrEmpty(text) ? DockStyle.Fill : DockStyle.None,
            Tag = key,
            Text = text,
        };
        check.CheckedChanged += CategoryRuleSettingsChanged;

        var target = pickup ? _pickupCategoryChecks : _storeCategoryChecks;
        target[key] = check;
        return check;
    }

    private static WinCheckBox CreatePlainCheckBox(string text, Point location)
    {
        return new WinCheckBox
        {
            AutoSize = true,
            Location = location,
            Text = text,
            UseVisualStyleBackColor = true,
        };
    }

    private static WinLabel CreateGridLabel(string text, ContentAlignment alignment = ContentAlignment.MiddleCenter)
    {
        return new WinLabel
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = alignment,
        };
    }

    private void LoadCategoryRuleSettings()
    {
        if (_checkSellUnselectedEquipment == null)
            return;

        _loadingCategoryRules = true;
        try
        {
            ItemCategoryRules.Load();
            var pickup = new HashSet<string>(ItemCategoryRules.PickupCategories, StringComparer.Ordinal);
            var store = new HashSet<string>(ItemCategoryRules.StoreCategories, StringComparer.Ordinal);
            var pickupRare = new HashSet<string>(ItemCategoryRules.PickupRareRules, StringComparer.Ordinal);
            var storeRare = new HashSet<string>(ItemCategoryRules.StoreRareRules, StringComparer.Ordinal);
            var equipmentDegrees = new HashSet<int>(ItemCategoryRules.StoreEquipmentDegrees);

            _checkSellUnselectedEquipment.Checked = ItemCategoryRules.SellUnselectedEquipment;
            _checkStoreMale.Checked = ItemCategoryRules.StoreMaleClothes;
            _checkStoreFemale.Checked = ItemCategoryRules.StoreFemaleClothes;
            foreach (var entry in _pickupCategoryChecks)
                entry.Value.Checked = pickup.Contains(entry.Key);
            foreach (var entry in _storeCategoryChecks)
                entry.Value.Checked = store.Contains(entry.Key);
            foreach (var entry in _pickupRareRuleChecks)
                entry.Value.Checked = pickupRare.Contains(entry.Key);
            foreach (var entry in _storeRareRuleChecks)
                entry.Value.Checked = storeRare.Contains(entry.Key);
            foreach (var entry in _storeEquipmentDegreeChecks)
                entry.Value.Checked = equipmentDegrees.Contains(entry.Key);
        }
        finally
        {
            _loadingCategoryRules = false;
        }
    }

    private void CategoryRuleSettingsChanged(object sender, EventArgs e)
    {
        if (_loadingSettings || _loadingCategoryRules)
            return;

        ItemCategoryRules.Save(
            _pickupCategoryChecks.Where(entry => entry.Value.Checked).Select(entry => entry.Key),
            _storeCategoryChecks.Where(entry => entry.Value.Checked).Select(entry => entry.Key),
            _pickupRareRuleChecks.Where(entry => entry.Value.Checked).Select(entry => entry.Key),
            _storeRareRuleChecks.Where(entry => entry.Value.Checked).Select(entry => entry.Key),
            _storeEquipmentDegreeChecks.Where(entry => entry.Value.Checked).Select(entry => entry.Key),
            _checkSellUnselectedEquipment.Checked,
            _checkStoreMale.Checked,
            _checkStoreFemale.Checked
        );
        RefreshSelectedItemRuleTestResult();
    }

    private void RefreshSelectedItemRuleTestResult()
    {
        if (_selectedItemTestOption != null && _selectedItemTestOption.Matches(_itemTestCodeName.Text.Trim()))
            TestItemRules();
    }

    private void RefreshSupplyRulesUi()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(RefreshSupplyRulesUi);
            return;
        }

        BuildSupplyRulesUi();
    }

    private sealed class ItemRuleTestOption
    {
        public ItemRuleTestOption(RefObjItem item, string displayName)
        {
            Item = item;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? item.CodeName : displayName;
        }

        public RefObjItem Item { get; }

        public string CodeName => Item.CodeName;

        public string DisplayName { get; }

        public bool Matches(string value)
        {
            return string.Equals(CodeName, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(DisplayName, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ToString(), value, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => $"{CodeName} — {DisplayName}";
    }
}
