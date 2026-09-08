using System;
using System.Collections.Generic;
using System.Linq;
using RSBot.Core.Network;

namespace RSBot.Core.Objects;

/// <summary>
///     The Character's Invetory with EquippedPart and NormalPart.
/// </summary>
public class CharacterInventory : InventoryItemCollection
{
    /// <summary>
    ///     Minimum slot of NormalPart.
    /// </summary>
    public static byte NORMAL_PART_MIN_SLOT
    {
        get
        {
            return (
                Game.ClientType == GameClientType.Global
                || Game.ClientType == GameClientType.Korean
                || Game.ClientType == GameClientType.VTC_Game
                || Game.ClientType == GameClientType.RuSro
                || Game.ClientType == GameClientType.Turkey
                || Game.ClientType == GameClientType.Taiwan
                || Game.ClientType == GameClientType.Japanese
            )
                ? (byte)17 //4 slots for relics
                : (byte)13;
        }
    }

    /// <summary>
    ///     The constructor.
    /// </summary>
    /// <param name="size">The size.</param>
    public CharacterInventory(Packet packet)
        : base(packet) { }

    /// <summary>
    ///     Gets the size of NormalPart.
    /// </summary>
    public byte NormalPartSize => (byte)(Capacity - NORMAL_PART_MIN_SLOT);

    /// <summary>
    ///     Gets a value indicating whether the NormalPart is full.
    /// </summary>
    /// <value>
    ///     <c>true</c> if the NormalPart is full; otherwise, <c>false</c>.
    /// </value>
    public override bool Full => GetNormalPartItems().Count >= NormalPartSize;

    /// <summary>
    ///     Gets a value indicating whether this instance is sorting.
    /// </summary>
    /// <value>
    ///     <c>true</c> if this instance is sorting; otherwise, <c>false</c>.
    /// </value>
    public bool IsSorting { get; private set; }

    /// <summary>
    ///     Gets the number of free slots in NormalPart inventory.
    /// </summary>
    public new byte FreeSlots => (byte)(NormalPartSize - GetNormalPartItems().Count);

    /// <summary>
    ///     Gets the first free slot number inside NormalPart.
    /// </summary>
    /// <returns>if found: the first free slot number; otherwise: 0</returns>
    public override byte GetFreeSlot()
    {
        for (var slot = NORMAL_PART_MIN_SLOT; slot < Capacity; slot++)
            if (GetItemAt(slot) == null)
                return slot;

        return 0;
    }

    /// <summary>
    ///     Gets items of EquippedPart, ordered by slot.
    /// </summary>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetEquippedPartItems()
    {
        return GetItems(item => item.Slot < NORMAL_PART_MIN_SLOT);
    }

    /// <summary>
    ///     Gets items of NormalPart, ordered by slot.
    /// </summary>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetNormalPartItems()
    {
        return GetItems(item => item.Slot >= NORMAL_PART_MIN_SLOT);
    }

    /// <summary>
    ///     Gets items of NormalPart, ordered by slot.
    /// </summary>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetNormalPartItems(Predicate<InventoryItem> predicate)
    {
        return GetItems(item => item.Slot >= NORMAL_PART_MIN_SLOT && predicate(item));
    }

    /// <summary>
    ///     Gets items of NormalPart by ItemId, ordered by slot.
    /// </summary>
    /// <param name="itemId">The identifier of item.</param>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetNormalPartItems(uint itemId)
    {
        return GetItems(item => item.Slot >= NORMAL_PART_MIN_SLOT && item.ItemId == itemId);
    }

    /// <summary>
    ///     Moves the item inside Character's Inventory.
    /// </summary>
    /// <param name="sourceSlot">The source slot.</param>
    /// <param name="destinationSlot">The destination slot.</param>
    /// <param name="amount">The amount.</param>
    /// <returns><c>true</c> if successfully moved; otherwise, <c>false</c>.</returns>
    public bool MoveItem(byte sourceSlot, byte destinationSlot, ushort amount = 0)
    {
        var itemAtSource = GetItemAt(sourceSlot);
        if (itemAtSource == null)
            return false;

        if (amount == 0)
            amount = itemAtSource.Amount;

        var packet = new Packet(0x7034);
        packet.WriteByte(0); //kinda flag
        packet.WriteByte(sourceSlot);
        packet.WriteByte(destinationSlot);
        packet.WriteUShort(amount);

        var asyncResult = new AwaitCallback(
            response =>
            {
                var result = response.ReadByte();
                if (result == 0x01)
                {
                    var operation = response.ReadByte();
                    if (operation != 0)
                        return AwaitCallbackResult.ConditionFailed;

                    var source = response.ReadByte();
                    var destination = response.ReadByte();
                    if (source == sourceSlot && destination == destinationSlot)
                        return AwaitCallbackResult.Success;
                }

                return AwaitCallbackResult.Fail;
            },
            0xB034
        );

        PacketManager.SendPacket(packet, PacketDestination.Server, asyncResult);
        asyncResult.AwaitResponse(500);

        return asyncResult.IsCompleted;
    }

    public void Sort()
    {
        if (IsSorting || Game.Player.InAction)
            return;

        IsSorting = true;
        Log.Debug("Sorting the character inventory...");
        var operations = 0;

        try
        {
            operations += ConsolidateStacks();
            operations += ArrangeItemsByReferenceId();
        }
        finally
        {
            IsSorting = false;
            Log.Debug($"Sorting finished after {operations} move operations");
        }
    }

    private int ConsolidateStacks()
    {
        var operations = 0;
        var itemIds = GetNormalPartItems(item => item.Record.IsStackable)
            .Select(item => item.ItemId)
            .Distinct()
            .ToArray();

        foreach (var itemId in itemIds)
        {
            while (!Game.Player.InAction)
            {
                var stacks = GetNormalPartItems(item => item.ItemId == itemId)
                    .OrderBy(item => item.Slot)
                    .ToArray();
                if (stacks.Length < 2)
                    break;

                // Fill the earliest non-full stack from the last later stack. This
                // leaves full stacks first and at most one partial stack at the end.
                var destination = stacks.FirstOrDefault(item => item.Amount < item.Record.MaxStack);
                if (destination == null)
                    break;

                var source = stacks.LastOrDefault(item => item.Slot > destination.Slot);
                if (source == null)
                    break;

                var freeAmount = destination.Record.MaxStack - destination.Amount;
                var moveAmount = (ushort)Math.Min(source.Amount, freeAmount);
                if (moveAmount == 0)
                    break;

                var destinationAmountBefore = destination.Amount;
                if (!MoveItem(source.Slot, destination.Slot, moveAmount))
                {
                    Log.Warn($"Could not consolidate inventory item {itemId}; leaving its remaining stacks unchanged.");
                    break;
                }

                operations++;
                var updatedDestination = GetItemAt(destination.Slot);
                if (
                    updatedDestination?.ItemId != itemId
                    || updatedDestination.Amount <= destinationAmountBefore
                )
                {
                    Log.Warn(
                        $"Inventory item {itemId} did not change after a successful stack operation; stopping this group."
                    );
                    break;
                }
            }
        }

        return operations;
    }

    private int ArrangeItemsByReferenceId()
    {
        var operations = 0;
        var itemCount = GetNormalPartItems().Count;
        var firstSlot = NORMAL_PART_MIN_SLOT;

        for (var offset = 0; offset < itemCount && !Game.Player.InAction; offset++)
        {
            var targetSlot = (byte)(firstSlot + offset);
            var nextItem = GetNormalPartItems(item => item.Slot >= targetSlot)
                .OrderBy(item => item.ItemId)
                .ThenBy(item => item.Amount < item.Record.MaxStack)
                .ThenBy(item => item.Slot)
                .FirstOrDefault();
            if (nextItem == null)
                break;

            var currentItem = GetItemAt(targetSlot);
            if (currentItem?.ItemId == nextItem.ItemId)
            {
                var currentIsFull = currentItem.Amount >= currentItem.Record.MaxStack;
                var nextIsFull = nextItem.Amount >= nextItem.Record.MaxStack;
                if (currentIsFull || !nextIsFull)
                    continue;

                // Moving just the missing amount from the later full stack makes
                // the current slot full and moves the partial amount behind it.
                var missingAmount = currentItem.Record.MaxStack - currentItem.Amount;
                if (!MoveItem(nextItem.Slot, targetSlot, (ushort)missingAmount))
                {
                    Log.Warn(
                        $"Could not move the full inventory stack {nextItem.ItemId} before its partial stack."
                    );
                    continue;
                }

                operations++;
                continue;
            }

            if (!MoveItem(nextItem.Slot, targetSlot))
            {
                Log.Warn($"Could not move inventory item {nextItem.ItemId} to slot {targetSlot} while sorting.");
                continue;
            }

            operations++;
        }

        return operations;
    }
}
