using System;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Objects;

namespace RSBot.Alchemy.Bundle.Attribute;

internal class AttributeBundle : IAlchemyBundle
{
    #region Members

    private bool _shouldRun;

    #endregion Members

    #region Constructor

    public AttributeBundle()
    {
        SubscribeEvents();
    }

    #endregion Constructor

    #region Methods

    public void SubscribeEvents()
    {
        EventManager.SubscribeEvent(
            "OnAlchemySuccess",
            new Action<InventoryItem, InventoryItem, AlchemyType>(OnStoneAlchemySuccess)
        );
        EventManager.SubscribeEvent(
            "OnAlchemyFailed",
            new Action<InventoryItem, InventoryItem, AlchemyType>(OnStoneAlchemyFailed)
        );
        EventManager.SubscribeEvent("OnAlchemyError", new Action<ushort, AlchemyType>(OnStoneAlchemyError));
        EventManager.SubscribeEvent("OnAlchemy", new Action<AlchemyType>(OnStoneAlchemy));
        EventManager.SubscribeEvent("OnFuseRequest", new Action<AlchemyAction, AlchemyType>(OnFuseRequest));
    }

    /// <summary>
    ///     Runs the attribute granter using the specified configuration.
    /// </summary>
    /// <param name="engineConfig">The configuration.</param>
    public void Run<T>(T engineConfig)
    {
        if (engineConfig is not AttributeBundleConfig config)
            return;

        //Wait for the next tick to operate
        if (!_shouldRun)
            return;

        var currentItem = Game.Player.Inventory.GetItemAt(config.Item.Slot);
        if (currentItem?.ItemId != config.Item.ItemId)
        {
            Log.Error("[Alchemy] The selected item is no longer available in its inventory slot.");
            Kernel.Bot.Stop();

            return;
        }

        config.Item = currentItem;

        var attributes = config.Attributes.ToList();
        if (!attributes.Any())
        {
            Log.Error("[Alchemy] No attribute stone fusion configured!");

            Kernel.Bot.Stop();

            return;
        }

        foreach (var attribute in attributes)
        {
            if (_shouldRun == false)
                break;

            var slot = ItemAttributesInfo.GetAttributeSlotForItem(attribute.Group, config.Item.Record);
            var currentValue = config.Item.Attributes.GetPercentage(slot);

            if (currentValue >= attribute.MaxValue && attribute.MaxValue > 0)
                continue;

            if (!AlchemyManager.TryFuseAttributeStone(config.Item, attribute.Stone))
                continue;

            _shouldRun = false;
        }

        if (_shouldRun)
        {
            var targetStatus = string.Join(
                ", ",
                attributes.Select(attribute =>
                {
                    var slot = ItemAttributesInfo.GetAttributeSlotForItem(attribute.Group, config.Item.Record);
                    var currentValue = config.Item.Attributes.GetPercentage(slot);
                    return $"{attribute.Group.GetTranslation()} {currentValue}%/{attribute.MaxValue}%";
                })
            );
            var allTargetsReached = attributes.All(attribute =>
            {
                var slot = ItemAttributesInfo.GetAttributeSlotForItem(attribute.Group, config.Item.Record);
                return config.Item.Attributes.GetPercentage(slot) >= attribute.MaxValue;
            });
            var message = allTargetsReached
                ? $"Alchemy stopped: attribute targets reached ({targetStatus})."
                : $"Alchemy stopped before all attribute targets were reached ({targetStatus}); no further configured fusion could be started.";

            Globals.View.AddLog(config.Item.Record.GetRealName(), message);

            Kernel.Bot.Stop();
        }
    }

    /// <summary>
    ///     Stops this instance.
    /// </summary>
    public void Stop()
    {
        _shouldRun = false;
    }

    /// <summary>
    ///     Starts this instance.
    /// </summary>
    public void Start()
    {
        _shouldRun = true;
    }

    #endregion Methods

    #region Events

    /// <summary>
    ///     Will be triggered if any fuse request was sent to the server
    /// </summary>
    /// <param name="action"></param>
    /// <param name="type"></param>
    private void OnFuseRequest(AlchemyAction action, AlchemyType type)
    {
        if (type != AlchemyType.AttributeStone || !Bootstrap.IsActive)
            return;

        _shouldRun = false;
    }

    /// <summary>
    ///     Will be triggered if any stone alchemy action response was sent from the server
    /// </summary>
    private void OnStoneAlchemy(AlchemyType type)
    {
        if (type != AlchemyType.AttributeStone || !Bootstrap.IsActive)
            return;

        _shouldRun = true;
    }

    /// <summary>
    ///     Will be triggered if a stone alchemy operation was successful
    /// </summary>
    /// <param name="oldItem"></param>
    /// <param name="newItem">The new item after the successful action</param>
    /// <param name="type"></param>
    private void OnStoneAlchemySuccess(InventoryItem oldItem, InventoryItem newItem, AlchemyType type)
    {
        if (type != AlchemyType.AttributeStone || !Bootstrap.IsActive)
            return;

        RefreshConfiguredItem(newItem);

        var changedAttributeSlots = oldItem.Attributes.CompareSlots(newItem.Attributes);

        foreach (var slot in changedAttributeSlots)
        {
            var attributeGroup = ItemAttributesInfo.GetAttributeGroupBySlot(newItem.Record, slot);
            var attributeValue = newItem.Attributes.GetPercentage(slot);

            Globals.View.AddLog(
                newItem.Record.GetRealName(),
                $"The attribute [{attributeGroup.GetTranslation()}] changed to [+{attributeValue}%]"
            );
        }

        _shouldRun = true;
    }

    /// <summary>
    ///     Will be triggered if a stone alchemy action failed
    /// </summary>
    /// <param name="oldItem"></param>
    /// <param name="newItem">The new item after the operation failed</param>
    /// <param name="type"></param>
    private void OnStoneAlchemyFailed(InventoryItem oldItem, InventoryItem newItem, AlchemyType type)
    {
        if (type != AlchemyType.AttributeStone || !Bootstrap.IsActive)
            return;

        RefreshConfiguredItem(newItem);

        Globals.View.AddLog(
            newItem.Record.GetRealName(),
            Game.ReferenceManager.GetTranslation("UIIT_MSG_REINFORCERR_FAIL")
        );

        _shouldRun = true;
    }

    private static void RefreshConfiguredItem(InventoryItem newItem)
    {
        var config = Globals.Botbase.AttributeBundleConfig;
        if (config?.Item == null || newItem == null)
            return;

        if (config.Item.Slot == newItem.Slot && config.Item.ItemId == newItem.ItemId)
            config.Item = newItem;
    }

    /// <summary>
    ///     Will be triggered if an stone alchemy error
    /// </summary>
    /// <param name="errorCode">The error code</param>
    /// <param name="type"></param>
    private void OnStoneAlchemyError(ushort errorCode, AlchemyType type)
    {
        if (type != AlchemyType.AttributeStone || !Bootstrap.IsActive)
            return;

        _shouldRun = true;

        Globals.View.AddLog(
            Globals.Botbase.AttributeBundleConfig?.Item?.Record?.GetRealName(),
            Game.ReferenceManager.GetTranslation("UIIT_MSG_REINFORCERR_FAIL")
        );
    }

    #endregion Events
}
