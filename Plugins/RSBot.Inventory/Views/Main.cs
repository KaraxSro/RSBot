using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Inventory;
using SDUI;
using SDUI.Controls;
using Button = SDUI.Controls.Button;
using ListViewExtensions = RSBot.Core.Extensions.ListViewExtensions;

namespace RSBot.Inventory.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    private readonly object _lock;

    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    private int _selectedIndex;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Main" /> class.
    /// </summary>
    public Main()
    {
        _lock = new object();
        InitializeComponent();
        SubscribeEvents();

        listViewMain.SmallImageList = ListViewExtensions.StaticItemsImageList;

        var backColor = ColorScheme.BorderColor.Determine().Alpha(85);
        buttonInventory.ForeColor = backColor.Determine();
        buttonInventory.Color = backColor;
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnLoadCharacter", OnLoadCharacter);
        EventManager.SubscribeEvent("OnUpdateInventoryItem", new Action<byte>(OnUpdateInventoryItem));
        EventManager.SubscribeEvent("OnUseItem", new Action<byte>(OnUpdateInventoryItem));
        EventManager.SubscribeEvent("OnInventoryUpdate", OnInventoryUpdate);
    }

    private void OnLoadCharacter()
    {
        UpdateInventoryList(true);
    }

    private void OnInventoryUpdate()
    {
        UpdateInventoryList();
    }

    /// <summary>
    ///     Calling when update inventory item
    /// </summary>
    /// <param name="slot"></param>
    private void OnUpdateInventoryItem(byte slot)
    {
        var key = slot.ToString();
        if (!listViewMain.Items.ContainsKey(key))
            return;

        lock (_lock)
        {
            var inventoryItem = Game.Player.Inventory.GetItemAt(slot);
            if (inventoryItem == null)
                return;

            var listViewItem = listViewMain.Items[key];

            UpdateListViewItem(listViewItem, inventoryItem);
        }
    }

    /// <summary>
    ///     Updates the inventory list.
    /// </summary>
    public void UpdateInventoryList(bool rebuild = false)
    {
        if (!Visible || Game.Player == null)
            return;

        lock (_lock)
        {
            var topItemKey = rebuild ? null : listViewMain.TopItem?.Name;
            var items = GetVisibleItems();

            listViewMain.BeginUpdate();
            try
            {
                if (rebuild)
                    listViewMain.Items.Clear();

                SynchronizeItems(items);
            }
            finally
            {
                listViewMain.EndUpdate();
            }

            if (topItemKey != null && listViewMain.Items.ContainsKey(topItemKey))
                listViewMain.TopItem = listViewMain.Items[topItemKey];
        }
    }

    private IReadOnlyList<InventoryItem> GetVisibleItems()
    {
        switch (_selectedIndex)
        {
            case 0:
                var inventoryItems = Game.Player.Inventory.GetNormalPartItems();
                UpdateCapacity(Game.Player.Inventory.FreeSlots, Game.Player.Inventory.NormalPartSize);
                return inventoryItems.ToList();

            case 1:
                var equippedItems = Game.Player.Inventory.GetEquippedPartItems();
                var maxSlots =
                    Game.ClientType == GameClientType.Global
                    || Game.ClientType == GameClientType.Korean
                    || Game.ClientType == GameClientType.VTC_Game
                    || Game.ClientType == GameClientType.RuSro
                    || Game.ClientType == GameClientType.Turkey
                    || Game.ClientType == GameClientType.Taiwan
                    || Game.ClientType == GameClientType.Japanese
                        ? 17
                        : 13; //4 slots for relics
                UpdateCapacity(maxSlots - equippedItems.Count, maxSlots, true);
                return equippedItems.ToList();

            case 2:
                UpdateCapacity(Game.Player.Avatars.FreeSlots, Game.Player.Avatars.Capacity, true);
                return Game.Player.Avatars.ToList();

            case 3 when Game.Player.HasActiveAbilityPet:
                UpdateCapacity(Game.Player.AbilityPet.Inventory.FreeSlots, Game.Player.AbilityPet.Inventory.Capacity);
                return Game.Player.AbilityPet.Inventory.ToList();

            case 4 when Game.Player.Storage != null:
                UpdateCapacity(Game.Player.Storage.FreeSlots, Game.Player.Storage.Capacity);
                return Game.Player.Storage.ToList();

            case 5 when Game.Player.GuildStorage != null:
                UpdateCapacity(Game.Player.GuildStorage.FreeSlots, Game.Player.GuildStorage.Capacity);
                return Game.Player.GuildStorage.ToList();

            case 6 when Game.Player.JobTransport != null:
                UpdateCapacity(
                    Game.Player.JobTransport.Inventory.FreeSlots,
                    Game.Player.JobTransport.Inventory.Capacity
                );
                return Game.Player.JobTransport.Inventory.ToList();

            case 7 when Game.Player.Job2SpecialtyBag != null:
                UpdateCapacity(Game.Player.Job2SpecialtyBag.FreeSlots, Game.Player.Job2SpecialtyBag.Capacity);
                return Game.Player.Job2SpecialtyBag.ToList();

            case 8 when Game.Player.Job2 != null:
                UpdateCapacity(Game.Player.Job2.FreeSlots, Game.Player.Job2.Capacity);
                return Game.Player.Job2.ToList();

            case 9 when Game.Player.HasActiveFellowPet:
                UpdateCapacity(Game.Player.Fellow.Inventory.FreeSlots, Game.Player.Fellow.Inventory.Capacity);
                return Game.Player.Fellow.Inventory.ToList();

            default:
                return Array.Empty<InventoryItem>();
        }
    }

    private void UpdateCapacity(int freeSlots, int capacity, bool spaced = false)
    {
        lblFreeSlots.Text = spaced ? $"{freeSlots} / {capacity}" : $"{freeSlots}/{capacity}";
        pbInventoryStatus.Maximum = capacity;
        pbInventoryStatus.Value = Math.Min(freeSlots, capacity);
    }

    private void SynchronizeItems(IReadOnlyList<InventoryItem> items)
    {
        var itemKeys = items.Select(item => item.Slot.ToString()).ToHashSet();

        for (var index = listViewMain.Items.Count - 1; index >= 0; index--)
        {
            if (!itemKeys.Contains(listViewMain.Items[index].Name))
                listViewMain.Items.RemoveAt(index);
        }

        for (var index = 0; index < items.Count; index++)
        {
            var inventoryItem = items[index];
            var key = inventoryItem.Slot.ToString();

            if (listViewMain.Items.ContainsKey(key))
            {
                var listViewItem = listViewMain.Items[key];
                UpdateListViewItem(listViewItem, inventoryItem);

                if (listViewItem.Index != index)
                {
                    listViewMain.Items.Remove(listViewItem);
                    listViewMain.Items.Insert(index, listViewItem);
                }
            }
            else
            {
                AddItem(inventoryItem, index);
            }
        }
    }

    /// <summary>
    ///     Adds the item.
    /// </summary>
    /// <param name="item">The item.</param>
    private void AddItem(InventoryItem item, int index = -1)
    {
        if (item == null)
            return;

        var name = item.Record?.GetRealName() ?? "";
        if (item.OptLevel > 0)
            name += " (+" + item.OptLevel + ")";

        var lvItem = new ListViewItem(name, 0) { Name = item.Slot.ToString() };
        lvItem.Tag = item;
        lvItem.SubItems.Add(item.Amount.ToString());
        lvItem.SubItems.Add(item.Record.IsEquip ? item.Record.GetRarityName() : string.Empty);

        if (_selectedIndex == 0)
        {
            var useItemsAtTrainingPlace = PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace");

            if (useItemsAtTrainingPlace.Contains(item.Record.CodeName))
                lvItem.Font = new Font(lvItem.Font, FontStyle.Bold);
        }

        lvItem.LoadItemImageAsync(item.Record);

        if (index < 0 || index >= listViewMain.Items.Count)
            listViewMain.Items.Add(lvItem);
        else
            listViewMain.Items.Insert(index, lvItem);
    }

    private void UpdateListViewItem(ListViewItem listViewItem, InventoryItem item)
    {
        var previousItem = listViewItem.Tag as InventoryItem;
        var name = item.Record?.GetRealName() ?? "";
        if (item.OptLevel > 0)
            name += " (+" + item.OptLevel + ")";

        while (listViewItem.SubItems.Count < 3)
            listViewItem.SubItems.Add(string.Empty);

        listViewItem.Tag = item;
        listViewItem.Text = name;
        listViewItem.SubItems[1].Text = item.Amount.ToString();
        listViewItem.SubItems[2].Text = item.Record.IsEquip ? item.Record.GetRarityName() : string.Empty;

        if (_selectedIndex == 0)
        {
            var useItemsAtTrainingPlace = PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace");
            var shouldBeBold = useItemsAtTrainingPlace.Contains(item.Record.CodeName);
            if (listViewItem.Font.Bold != shouldBeBold)
                listViewItem.Font = shouldBeBold ? new Font(listViewItem.Font, FontStyle.Bold) : listViewMain.Font;
        }

        if (previousItem?.Record?.CodeName != item.Record.CodeName)
            listViewItem.LoadItemImageAsync(item.Record);
    }

    /// <summary>
    ///     Handles the visible changed event of the parent.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private void Main_VisibleChanged(object sender, EventArgs e)
    {
        UpdateInventoryList();
    }

    /// <summary>
    ///     Handles the Click event of the btnReload control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private void buttonUseItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        inventoryItem?.Use();
    }

    /// <summary>
    ///     Handles the mouse double click event of the listviewmain control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private void listViewMain_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (listViewMain.SelectedItems.Count <= 0)
            return;

        if (!Kernel.Debug)
            return;

        var itemForm = new ItemProperties(listViewMain.SelectedItems[0].Tag as InventoryItem);
        itemForm.Show();
    }

    /// <summary>
    ///     Handles the selected index changed event of the button's control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private void ButtonSwitcher(object sender, EventArgs e)
    {
        var button = sender as Button;
        if (_selectedIndex == button.TabIndex)
            return;

        _selectedIndex = button.TabIndex;

        //Only character inventory, storage and guild storage sorting is supported for now!
        btnSort.Visible = _selectedIndex == 0;
        checkAutoSort.Visible = _selectedIndex is 0 or 4 or 5;

        foreach (var control in topPanel.Controls.OfType<Button>())
        {
            if (control.TabIndex > 9)
                continue;

            control.Color = Color.Transparent;

            if (control == button)
            {
                var backColor = ColorScheme.BorderColor.Determine().Alpha(85);
                control.ForeColor = backColor.Determine();
                control.Color = backColor;
            }

            control.Invalidate();
        }

        UpdateInventoryList(true);
    }

    private void dropToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        var cos = _selectedIndex == 3;
        inventoryItem?.Drop(cos, Game.Player.AbilityPet?.UniqueId);
    }

    private void contextMenuStrip_Opening(object sender, CancelEventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
        {
            e.Cancel = true;
            return;
        }

        var listViewItem = listViewMain.SelectedItems[0];
        if (listViewItem.Tag is not InventoryItem inventoryItem)
            return;

        if (_selectedIndex != 0)
        {
            autoUseAccordingToPurposeToolStripMenuItem.Visible = false;
            useToolStripMenuItem.Visible = false;
            moveToLastDeathPositionToolStripMenuItem.Visible = false;
            moveToLastRecallPositionToolStripMenuItem.Visible = false;
            moveToPetToolStripMenuItem.Visible = false;
            moveToPlayerToolStripMenuItem.Visible = _selectedIndex == 3;
            selectMapLocationToolStripMenuItem.Visible = false;
            return;
        }

        autoUseAccordingToPurposeToolStripMenuItem.Visible = true;
        var canUse = (inventoryItem.Record.CanUse & ObjectUseType.Yes) != 0;
        var canUseAccordingToPurpose = canUse && IsPurposeItem(inventoryItem);
        if (canUse)
        {
            var useItems = PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace");
            useItemAtTrainingPlaceMenuItem.Checked = useItems.Contains(inventoryItem.Record.CodeName);
            useItemAtTrainingPlaceMenuItem.Enabled = true;

            var purposiveItems = PlayerConfig.GetArray<string>("RSBot.Inventory.AutoUseAccordingToPurpose");
            autoUseAccordingToPurposeToolStripMenuItem.Checked = purposiveItems.Contains(inventoryItem.Record.CodeName);
            autoUseAccordingToPurposeToolStripMenuItem.Enabled = canUseAccordingToPurpose;
        }
        else
        {
            useItemAtTrainingPlaceMenuItem.Checked = false;
            useItemAtTrainingPlaceMenuItem.Enabled = false;

            autoUseAccordingToPurposeToolStripMenuItem.Checked = false;
            autoUseAccordingToPurposeToolStripMenuItem.Enabled = false;
        }

        var isReverseScroll = inventoryItem.Equals(new TypeIdFilter(3, 3, 3, 3));
        useToolStripMenuItem.Visible = !isReverseScroll;
        useToolStripMenuItem.Enabled = inventoryItem.Record.CanUse != ObjectUseType.No;
        moveToLastDeathPositionToolStripMenuItem.Visible = isReverseScroll;
        moveToLastRecallPositionToolStripMenuItem.Visible = isReverseScroll;
        selectMapLocationToolStripMenuItem.Visible = isReverseScroll;
        dropToolStripMenuItem.Visible = inventoryItem.Record.CanDrop != ObjectDropType.No;

        moveToPetToolStripMenuItem.Visible = Game.Player.AbilityPet != null && _selectedIndex != 3;
        moveToPlayerToolStripMenuItem.Visible = _selectedIndex == 3;

        if (isReverseScroll)
        {
            var tagItem = selectMapLocationToolStripMenuItem.Tag as InventoryItem;
            if (tagItem != inventoryItem)
            {
                selectMapLocationToolStripMenuItem.Tag = inventoryItem;
                selectMapLocationToolStripMenuItem.DropDownItems.Clear();

                foreach (var item in Game.ReferenceManager.OptionalTeleports)
                {
                    var mapName = Game.ReferenceManager.GetTranslation(item.Value.Region.ToString());

                    var menuItem = new ToolStripMenuItem { Text = mapName };

                    menuItem.Click += (itemSender, itemEvent) =>
                    {
                        inventoryItem.UseTo(7, item.Value.ID);
                    };

                    selectMapLocationToolStripMenuItem.DropDownItems.Add(menuItem);
                }
            }
        }
    }

    private void moveToLastRecallPositionToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        inventoryItem.UseTo(2);
    }

    private void moveToLastDeathPositionToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        inventoryItem.UseTo(3);
    }

    private void moveToPetToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        if (Game.Player.AbilityPet != null)
            return;

        var freeSlot = Game.Player.AbilityPet.Inventory.GetFreeSlot();
        if (freeSlot == 0xFF)
            return;

        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_MOVE_ITEM_PC_PET);
        packet.WriteUInt(Game.Player.AbilityPet.UniqueId);
        packet.WriteByte(inventoryItem.Slot);
        packet.WriteByte(freeSlot);
        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    private void moveToPlayerToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        if (Game.Player.AbilityPet == null)
            return;

        var freeSlot = Game.Player.Inventory.GetFreeSlot();
        if (freeSlot == 0xFF)
            return;

        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_MOVE_ITEM_PET_PC);
        packet.WriteUInt(Game.Player.AbilityPet.UniqueId);
        packet.WriteByte(inventoryItem.Slot);
        packet.WriteByte(freeSlot);
        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    private void btnSort_Click(object sender, EventArgs e)
    {
        Task.Run(() => Game.Player?.Inventory?.Sort());
    }

    private void checkAutoSort_CheckedChanged(object sender, EventArgs e)
    {
        PlayerConfig.Set("RSBot.Inventory.AutoSort", checkAutoSort.Checked);
    }

    private void useItemAtTrainingPlaceMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedItems.Count == 0)
            return;

        var lvItem = listViewMain.SelectedItems[0];
        var itemsToUse = PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace").ToList();
        var selectedItem = (InventoryItem)lvItem.Tag;
        if (selectedItem == null)
            return;

        var useSelectedItem = itemsToUse.Contains(selectedItem.Record.CodeName);

        if (useSelectedItem)
        {
            lvItem.Font = Font;
            itemsToUse.Remove(selectedItem.Record.CodeName);
        }
        else
        {
            lvItem.Font = new Font(lvItem.Font, FontStyle.Bold);
            itemsToUse.Add(selectedItem.Record.CodeName);
        }

        useItemAtTrainingPlaceMenuItem.Checked = !useItemAtTrainingPlaceMenuItem.Checked;
        PlayerConfig.SetArray("RSBot.Inventory.ItemsAtTrainplace", itemsToUse);
    }

    private void autoUseAccordingToPurposeToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedItems.Count == 0)
            return;

        var lvItem = listViewMain.SelectedItems[0];

        var itemsToUse = PlayerConfig.GetArray<string>("RSBot.Inventory.AutoUseAccordingToPurpose").ToList();
        var selectedItem = (InventoryItem)lvItem.Tag;
        if (selectedItem == null || !IsPurposeItem(selectedItem))
            return;

        var useSelectedItem = itemsToUse.Contains(selectedItem.Record.CodeName);

        if (useSelectedItem)
        {
            lvItem.Font = Font;
            itemsToUse.Remove(selectedItem.Record.CodeName);
        }
        else
        {
            lvItem.Font = new Font(lvItem.Font, FontStyle.Bold);
            itemsToUse.Add(selectedItem.Record.CodeName);
        }

        autoUseAccordingToPurposeToolStripMenuItem.Checked = !autoUseAccordingToPurposeToolStripMenuItem.Checked;
        PlayerConfig.SetArray("RSBot.Inventory.AutoUseAccordingToPurpose", itemsToUse);
    }

    private static bool IsPurposeItem(InventoryItem item)
    {
        return item.Equals(new TypeIdFilter(3, 3, 13, 6)) || item.Equals(new TypeIdFilter(3, 3, 13, 7));
    }

    /// <summary>
    ///     Occurs before Main form is displayed.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Main_Load(object sender, EventArgs e)
    {
        checkAutoSort.Checked = PlayerConfig.Get("RSBot.Inventory.AutoSort", false);
    }
}
