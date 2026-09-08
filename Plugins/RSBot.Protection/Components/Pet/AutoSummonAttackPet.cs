using System;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.Protection.Components.Pet;

public class AutoSummonAttackPet
{
    private const int SummonConfirmationTimeout = 3_000;
    private const int RetryDelay = 1_500;
    private const int MaxConsecutiveFailures = 3;
    private const int FailureBackoff = 10_000;

    private static readonly TypeIdFilter GrowthPetFilter = new(3, 2, 1, 1);
    private static readonly TypeIdFilter FellowPetFilter = new(3, 2, 1, 3);

    private static long _summonRequestedAt;
    private static long _lastAttemptAt;
    private static long _backoffUntil;
    private static int _consecutiveFailures;

    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    public static void Initialize()
    {
        SubscribeEvents();
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnStartBot", OnStartBot);
        EventManager.SubscribeEvent("OnStopBot", Reset);
        EventManager.SubscribeEvent("OnBeforeBotTick", new Action<BotTickContext>(OnBeforeBotTick));
    }

    /// <summary>
    ///     Ensures that a configured attack pet is summoned before the botbase can select a target.
    /// </summary>
    private static void OnBeforeBotTick(BotTickContext context)
    {
        if (!Kernel.Bot.Running || !PlayerConfig.Get<bool>("RSBot.Protection.checkAutoSummonAttackPet"))
        {
            Reset();
            return;
        }

        if (Game.Player.Growth != null || Game.Player.Fellow != null)
        {
            Reset();
            return;
        }

        if (Game.Player.State.LifeState == LifeState.Dead)
        {
            Reset();
            return;
        }

        var now = Environment.TickCount64;

        // An accepted item-use response arrives before the spawned pet is necessarily present
        // in the local game state. Hold the botbase until that second update arrives.
        if (_summonRequestedAt != 0)
        {
            if (Elapsed(now, _summonRequestedAt) < SummonConfirmationTimeout)
            {
                context.Cancel = true;
                return;
            }

            _summonRequestedAt = 0;
            RegisterFailure(now, "The attack pet did not appear after the summon request.");
        }

        if (!HasSummonableAttackPet())
        {
            Reset();
            return;
        }

        if (_backoffUntil != 0 && !HasReached(now, _backoffUntil))
            return;

        if (_backoffUntil != 0)
        {
            _backoffUntil = 0;
            _consecutiveFailures = 0;
        }

        // Item use is rejected during combat or another character action. Fighting may
        // continue in that case; the next safe pre-combat tick will retry the summon.
        if (
            Game.Player.State.BattleState != BattleState.InPeace
            || Game.Player.InAction
            || Game.Player.Untouchable
            || Game.Player.State.ScrollState != ScrollState.Cancel
        )
            return;

        // At a safe point the summon takes priority over town/walk scripts and combat.
        context.Cancel = true;

        if (_lastAttemptAt != 0 && Elapsed(now, _lastAttemptAt) < RetryDelay)
            return;

        _lastAttemptAt = now;

        if (TrySummon())
        {
            _summonRequestedAt = Environment.TickCount64;
            return;
        }

        RegisterFailure(Environment.TickCount64, "The server rejected the attack pet summon request.");
    }

    /// <summary>
    ///     Resets summon coordination when a bot session starts.
    /// </summary>
    private static void OnStartBot()
    {
        Reset();
    }

    private static bool TrySummon()
    {
        var fellow = Game.Player.Inventory.GetItem(
            FellowPetFilter,
            item => item.State != InventoryItemState.Summoned && item.State != InventoryItemState.Dead
        );
        if (
            fellow != null
            && Game.Player.Inventory.GetItem(item => item.Record.IsFellowHpPotion) != null
            && Game.Player.SummonFellow()
        )
            return true;

        var growth = Game.Player.Inventory.GetItem(
            GrowthPetFilter,
            item => item.State != InventoryItemState.Summoned && item.State != InventoryItemState.Dead
        );

        return growth != null && Game.Player.SummonGrowth();
    }

    private static bool HasSummonableAttackPet()
    {
        var growth = Game.Player.Inventory.GetItem(
            GrowthPetFilter,
            item => item.State != InventoryItemState.Summoned && item.State != InventoryItemState.Dead
        );
        if (growth != null)
            return true;

        var fellow = Game.Player.Inventory.GetItem(
            FellowPetFilter,
            item => item.State != InventoryItemState.Summoned && item.State != InventoryItemState.Dead
        );

        return fellow != null && Game.Player.Inventory.GetItem(item => item.Record.IsFellowHpPotion) != null;
    }

    private static void RegisterFailure(long now, string reason)
    {
        _lastAttemptAt = now;
        _consecutiveFailures++;

        if (_consecutiveFailures < MaxConsecutiveFailures)
            return;

        _backoffUntil = now + FailureBackoff;
        _consecutiveFailures = 0;
        Log.Debug($"[AutoSummonAttackPet] {reason} Allowing bot execution and retrying later.");
    }

    private static long Elapsed(long now, long then)
    {
        return now - then;
    }

    private static bool HasReached(long now, long target)
    {
        return now >= target;
    }

    private static void Reset()
    {
        _summonRequestedAt = 0;
        _lastAttemptAt = 0;
        _backoffUntil = 0;
        _consecutiveFailures = 0;
    }
}
