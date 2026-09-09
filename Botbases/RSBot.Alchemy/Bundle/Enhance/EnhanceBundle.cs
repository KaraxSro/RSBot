using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using RSBot.Alchemy.Bot;
using RSBot.Alchemy.Extension;
using RSBot.Alchemy.Helper;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.Alchemy.Bundle.Enhance;

internal class EnhanceBundle : IAlchemyBundle
{
    private EnhanceBundleConfig _config;

    private bool _isStoneFusing;

    #region Constructor

    /// <summary>
    ///     Subscribes events
    /// </summary>
    public EnhanceBundle()
    {
        SubscribeEvents();

        _shouldRun = true;
    }

    #endregion Constructor

    #region Members

    private bool _shouldRun;

    private IEnumerable<InventoryItem> _luckyPowders;

    #endregion Members

    #region Methods

    public void Stop()
    {
        _shouldRun = false;
        _config = null;

        AlchemyManager.CancelPending();
    }

    /// <summary>
    ///     Starts this manager
    /// </summary>
    public void Start()
    {
        _shouldRun = true;
    }

    /// <summary>
    ///     Subscribes all required events
    /// </summary>
    private void SubscribeEvents()
    {
        EventManager.SubscribeEvent(
            "OnAlchemyDestroyed",
            new Action<InventoryItem, AlchemyType>(OnElixirAlchemyDestroyed)
        );
        EventManager.SubscribeEvent(
            "OnAlchemySuccess",
            new Action<InventoryItem, InventoryItem, AlchemyType>(OnElixirAlchemySuccess)
        );
        EventManager.SubscribeEvent("OnAlchemy", OnElixirAlchemy);
        EventManager.SubscribeEvent(
            "OnAlchemyFailed",
            new Action<InventoryItem, InventoryItem, AlchemyType>(OnElixirAlchemyFailed)
        );
        EventManager.SubscribeEvent("OnFuseRequest", new Action<AlchemyAction, AlchemyType>(OnFuseRequest));
    }

    /// <summary>
    ///     Runs a new tick of this manager
    /// </summary>
    /// <param name="engineConfig"></param>
    public void Run<T>(T engineConfig)
    {
        if (engineConfig is not EnhanceBundleConfig config)
            return;

        if (config.Item == null)
        {
            Log.Warn("[Alchemy] No item configured");
            Kernel.Bot.Stop();

            return;
        }

        //Item still there and available?
        var item = Game.Player.Inventory.GetItemAt(config.Item.Slot);
        if (item == null || item.Amount == 0)
        {
            Log.Warn("[Alchemy] Item to enhance is unavailable");
            Kernel.Bot.Stop();

            return;
        }

        //Config incomplete?
        if (!_shouldRun || Globals.Botbase.AlchemyEngine != AlchemyEngine.Enhance)
            return;

        if (config.Elixirs == null || !config.Elixirs.Any() || config.Elixirs.Sum(i => i.Amount) == 0)
        {
            Log.Warn("[Alchemy] No enhancement elixir selected");
            Kernel.Bot.Stop();

            return;
        }

        _config = config;
        _config.Item = item;
        _luckyPowders = AlchemyItemHelper.GetLuckyPowders(item);
        var nextPlusValue = item.OptLevel + 1;
        Log.Debug($"[Alchemy] Tick: slot={item.Slot}; itemId={item.ItemId}; current=+{item.OptLevel}; max=+{config.MaxOptLevel}; target=+{nextPlusValue}; config={config.GetHashCode():X}");

        //Max opt level reached?
        if (item.OptLevel >= config.MaxOptLevel)
        {
            Log.Warn($"[Alchemy] Reached max enhancement +{config.MaxOptLevel}; no +{nextPlusValue} request sent.");

            Globals.View.AddLog(
                item.Record.GetRealName(),
                $"Alchemy stopped: target plus reached (+{item.OptLevel}, configured target +{config.MaxOptLevel})."
            );
            Kernel.Bot.Stop();

            return;
        }

        // Fixed safety state machine. Required stones always precede optional Lucky.
        if (nextPlusValue >= 5 && config.UseImmortalStones
            && !EnsureRequiredStone(item, AlchemyItemHelper.GetImmortalStone(item), RefMagicOpt.MaterialImmortal, "Immortal", nextPlusValue))
            return;
        if (nextPlusValue >= 5 && config.UseAstralStones
            && !AlchemyItemHelper.HasMagicOption(item, RefMagicOpt.MaterialImmortal))
        {
            BlockEnhancement(
                item,
                $"Astral is enabled for +{nextPlusValue}, but the item has no matching Immortal option. Enable/use Immortal first"
            );
            return;
        }
        if (nextPlusValue >= 5 && config.UseAstralStones
            && !EnsureRequiredStone(item, AlchemyItemHelper.GetAstralStone(item), RefMagicOpt.MaterialAstral, "Astral", nextPlusValue))
            return;
        if (nextPlusValue >= 6 && config.UseSteadyStones
            && !EnsureRequiredStone(item, AlchemyItemHelper.GetSteadyStone(item), RefMagicOpt.MaterialSteady, "Steady", nextPlusValue))
            return;

        if (config.UseLuckyStones && nextPlusValue >= Math.Max(1, (int)config.LuckyUseFromPlus)
            && !AlchemyItemHelper.HasMagicOption(item, RefMagicOpt.MaterialLuck))
        {
            var luckyStone = AlchemyItemHelper.GetLuckyStone(item);
            if (luckyStone?.Amount > 0)
            {
                if (AlchemyManager.TryFuseMagicStone(item, luckyStone))
                {
                    _shouldRun = false;
                    _isStoneFusing = true;
                }
                return;
            }

            Log.Warn($"[Alchemy] Lucky is enabled from +{config.LuckyUseFromPlus}, but no D{item.Record.Degree} Lucky stone is available; continuing because Lucky is optional.");
        }

        Log.Notify($"[Alchemy] Attempting +{nextPlusValue}...");

        SendFusePacket();

        _shouldRun = false;
    }

    /// <summary>
    ///     Sends the fuse packet to the server
    /// </summary>
    private void SendFusePacket()
    {
        if (_config == null || !_shouldRun || !_config.Elixirs.Any())
            return;

        var refreshedItem = Game.Player.Inventory.GetItemAt(_config.Item.Slot);
        if (refreshedItem == null)
            return;
        _config.Item = refreshedItem;
        if (refreshedItem.OptLevel >= _config.MaxOptLevel)
        {
            Log.Warn($"[Alchemy] Reached max enhancement +{_config.MaxOptLevel}; no +{refreshedItem.OptLevel + 1} request sent.");
            _shouldRun = false;
            Kernel.Bot.Stop();
            return;
        }

        //Bot should stop without lucky powder?
        if (!_luckyPowders.Any() && _config.StopIfLuckyPowderEmpty)
        {
            Log.Warn("[Alchemy] No lucky powder left; no enhancement packet sent.");

            Kernel.Bot.Stop();
            MessageBox.Show(
                "No more lucky powder left in the inventory.",
                "Lucky powder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        var powder = _luckyPowders.FirstOrDefault();
        var elixir = Game.Player.Inventory.GetItem(_config.Elixirs.First().ItemId);
        if (elixir == null)
        {
            Log.Warn("[Alchemy] Selected elixir is no longer available; no packet sent.");
            return;
        }
        AlchemyManager.TryFuseElixir(refreshedItem, elixir, powder);
    }

    private bool EnsureRequiredStone(InventoryItem item, InventoryItem stone, string option, string name, int targetPlus)
    {
        if (AlchemyItemHelper.HasMagicOption(item, option))
            return true;

        if (stone?.Amount > 0)
        {
            Log.Notify($"[Alchemy] Preparing required {name} protection before +{targetPlus}.");
            if (AlchemyManager.TryFuseMagicStone(item, stone))
            {
                _shouldRun = false;
                _isStoneFusing = true;
            }
            return false;
        }

        BlockEnhancement(item, $"{name} is enabled and required before +{targetPlus}, but no matching D{item.Record.Degree} stone is available");
        return false;
    }

    private void BlockEnhancement(InventoryItem item, string detail)
    {
        var reason = $"[Alchemy] Blocked at +{item.OptLevel}: {detail}; no enhancement packet sent.";
        Log.Warn(reason);
        Globals.View.AddLog(item.Record.GetRealName(), reason);
        _shouldRun = false;
        Kernel.Bot.Stop();
    }

    #endregion Methods

    #region Events

    /// <summary>
    ///     Will be triggered if any elixir alchemy operation was completed
    /// </summary>
    private void OnElixirAlchemy()
    {
        _shouldRun = true;
    }

    /// <summary>
    ///     Will be triggered if any elixir alchemy operation was successful
    /// </summary>
    /// <param name="newItem"></param>
    private void OnElixirAlchemySuccess(InventoryItem oldItem, InventoryItem newItem, AlchemyType type)
    {
        if (Globals.Botbase.AlchemyEngine != AlchemyEngine.Enhance)
            return;

        if (_config != null && newItem != null)
        {
            _config.Item = newItem;
            Log.Debug($"[Alchemy] Refreshed item after success: slot={newItem.Slot}; opt=+{newItem.OptLevel}; durability={newItem.Durability}");
        }

        //After fusing a magic stone, reevaluate all requirements from the refreshed item.
        if (Bootstrap.IsActive && _isStoneFusing)
        {
            _shouldRun = true;
            _isStoneFusing = false;
        }

        if (type != AlchemyType.Elixir)
            return;

        var message = Game
            .ReferenceManager.GetTranslation("UIIT_MSG_REINFORCERR_SUCCESS")
            .JoymaxFormat(newItem.OptLevel);

        Log.Notify(message);
        Globals.View.AddLog(newItem.Record.GetRealName(), message);

        _shouldRun = true;
    }

    /// <summary>
    ///     Will be triggered if the selected item was destroyed. Logs a message and stops the bot
    /// </summary>
    /// <param name="oldItem">The the item that has been destroyed</param>
    /// <param name="type">The type of alchemy that was triggered</param>
    private void OnElixirAlchemyDestroyed(InventoryItem oldItem, AlchemyType type)
    {
        _shouldRun = false;
    }

    /// <summary>
    ///     Will be triggered if any elixir alchemy operation has failed. Logs a message and resets the current item
    /// </summary>
    /// <param name="newItem">The new item after the action has failed</param>
    /// <param name="type">The type of alchemy that was triggered</param>
    private void OnElixirAlchemyFailed(InventoryItem oldItem, InventoryItem newItem, AlchemyType type)
    {
        if (_config != null && newItem != null)
            _config.Item = newItem;
        if (_isStoneFusing)
        {
            _isStoneFusing = false;
            _shouldRun = true;
        }
        if (type != AlchemyType.Elixir)
            return;

        _shouldRun = true;
        var message = Game.ReferenceManager.GetTranslation("UIIT_MSG_REINFORCERR_FAIL");
        Log.Warn(message);
        Globals.View.AddLog(newItem.Record.GetRealName(), message);

        if (oldItem == null)
            return;

        message = string.Empty;
        if (newItem.Durability < oldItem.Durability)
            message = Game
                .ReferenceManager.GetTranslation("UIIT_MSG_REINFORCERR_FAILDOWN_DURABILITY")
                .JoymaxFormat(newItem.Durability);

        if (oldItem.OptLevel > 0 && newItem.OptLevel == 0)
            message = Game.ReferenceManager.GetTranslation("UIIT_MSG_REINFORCERR_FAIL_RESULT_OPTLV_ZERO");

        if (oldItem.OptLevel > 0 && oldItem.OptLevel < newItem.OptLevel)
            message = Game
                .ReferenceManager.GetTranslation("UIIT_MSG_REINFORCERR_FAIL_RESULT_OPTLV_DOWN")
                .JoymaxFormat(newItem.OptLevel, _config.Item.OptLevel - newItem.OptLevel);

        //Additional message
        if (message != string.Empty)
        {
            Log.Debug(message);
            Globals.View.AddLog(newItem.Record.GetRealName(), message);
        }

        Log.Debug($"[Alchemy] Refreshed item after failure: slot={newItem.Slot}; opt=+{newItem.OptLevel}; durability={newItem.Durability}");
    }

    /// <summary>
    ///     Called when [fuse request].
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="type">The type.</param>
    private void OnFuseRequest(AlchemyAction action, AlchemyType type)
    {
        _shouldRun = false;
    }

    #endregion Events
}
