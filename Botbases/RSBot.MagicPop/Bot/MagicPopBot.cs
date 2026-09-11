using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;
using RSBot.MagicPop.Model;
using RSBot.MagicPop.Protocol;
using RSBot.MagicPop.Validation;

namespace RSBot.MagicPop.Bot;

internal sealed class MagicPopBot
{
    private const string PendingRollConfigKey = "RSBot.MagicPop.PendingRoll";
    private const string MachineCodeName = "NPC_CH_GACHA_MACHINE";
    private const string PotionNpcCodeName = "NPC_KT_POTION";
    private const string CardCodeName = "ITEM_MALL_GACHA_CARD";
    private const string LosingCouponCodeName = "ITEM_MALL_GACHA_CARD_LOSE";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReturnScrollTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReturnTeleportSettleTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumPurchaseInterval = TimeSpan.Zero;
    private static readonly TimeSpan MinimumRollInterval = TimeSpan.Zero;

    private readonly object _syncRoot = new();
    private readonly List<MagicPopTarget> _targets = new();
    private MagicPopRunState _state = MagicPopRunState.Stopped;
    private MagicPopRollCorrelation _roll;
    private Task<bool> _task;
    private MagicPopRunState _taskSuccessState;
    private DateTime _purchaseStartedUtc;
    private MagicPopCardPurchaseResult _purchaseResult;
    private bool _returnScrollRequested;
    private bool _returnTeleportStarted;
    private bool _returnTeleportCompleted;
    private DateTime _returnScrollStartedUtc;
    private DateTime _returnTeleportSettleUntilUtc;
    private DateTime _nextPurchaseUtc;
    private DateTime _nextRollUtc;
    private bool _waitingForPurchase;
    private bool _purchasesAvailable;
    private bool _atPotionShop;
    private bool _atMachine;
    private string _fault;
    private int _rollCount;
    private int _winCount;
    private int _lossCount;
    private int _purchasedCardCount;
    private uint? _startingSilk;

    public MagicPopBot()
    {
        EventManager.SubscribeEvent("MagicPop.OnPlayResult", new Action<MagicPopPlayResult>(OnPlayResult));
        EventManager.SubscribeEvent("MagicPop.OnPlayProtocolError", new Action<string>(OnProtocolError));
        EventManager.SubscribeEvent("MagicPop.OnCardPurchased", new Action<MagicPopCardPurchaseResult>(OnCardPurchased));
        EventManager.SubscribeEvent("MagicPop.OnCardPurchaseError", new Action<string>(OnCardPurchaseError));
        EventManager.SubscribeEvent("OnUpdateInventoryItem", new Action<byte>(OnInventoryItemUpdated));
    }

    public void Start(IEnumerable<MagicPopTarget> targets)
    {
        Container.View.SetRunning(true);
        lock (_syncRoot)
        {
            _targets.Clear();
            _targets.AddRange(targets);
            _roll = null;
            _task = null;
            _fault = null;
            _waitingForPurchase = false;
            _purchaseResult = null;
            _purchasesAvailable = Container.References.CardPackage != null;
            _atPotionShop = false;
            _atMachine = false;
            _rollCount = 0;
            _winCount = 0;
            _lossCount = 0;
            _purchasedCardCount = 0;
            _startingSilk = Container.SilkBalance.Current?.Silk;
            _returnScrollRequested = false;
            _returnTeleportStarted = false;
            _returnTeleportCompleted = false;
            _returnScrollStartedUtc = DateTime.MinValue;
            _returnTeleportSettleUntilUtc = DateTime.MinValue;
            _nextPurchaseUtc = DateTime.MinValue;
            _nextRollUtc = DateTime.MinValue;
            ReconcilePendingRoll();
            SetState(MagicPopRunState.ReturningToHotan);
        }

        Log.Notify($"[Magic POP] Live run started with {_targets.Count} target(s).");
        Container.AppendDiagnostic($"Live run started with {_targets.Count} target(s).");
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            ScriptManager.Stop();
            _targets.Clear();
            _roll = null;
            _task = null;
            _waitingForPurchase = false;
            _purchaseResult = null;
            _returnScrollRequested = false;
            _returnTeleportStarted = false;
            _returnTeleportCompleted = false;
            _returnScrollStartedUtc = DateTime.MinValue;
            _returnTeleportSettleUntilUtc = DateTime.MinValue;
            _nextPurchaseUtc = DateTime.MinValue;
            _fault = null;
            SetState(MagicPopRunState.Stopped);
        }
    }

    public void Tick()
    {
        lock (_syncRoot)
        {
            if (_fault != null)
            {
                var fault = _fault;
                _fault = null;
                Log.Error($"[Magic POP] Stopped safely: {fault}");
                Container.AppendDiagnostic($"Stopped safely: {fault}");
                SetState(MagicPopRunState.Faulted);
                Kernel.Bot.Stop();
                return;
            }

            switch (_state)
            {
                case MagicPopRunState.ReturningToHotan:
                    TickReturningToHotan();
                    break;
                case MagicPopRunState.BuyingCards:
                    TickBuyingCards();
                    break;
                case MagicPopRunState.MovingToMachine:
                case MagicPopRunState.ReturningToMachine:
                case MagicPopRunState.MovingToPotionShop:
                    TickTask();
                    break;
                case MagicPopRunState.OpeningMachine:
                    TickOpenMachine();
                    break;
                case MagicPopRunState.Rolling:
                    TickRolling();
                    break;
                case MagicPopRunState.WaitingForRollResult:
                    TickWaitingForRoll();
                    break;
                case MagicPopRunState.SellingLosingCoupons:
                    TickSellingCoupons();
                    break;
                case MagicPopRunState.Completed:
                    Kernel.Bot.Stop();
                    break;
            }
        }
    }

    private void TickReturningToHotan()
    {
        if (!_returnScrollRequested)
        {
            _returnScrollRequested = true;
            _returnScrollStartedUtc = DateTime.UtcNow;
            if (!Game.Player.UseReturnScroll())
            {
                Fail("The return scroll could not be used.");
                return;
            }

            Log.Notify("[Magic POP] Return scroll accepted; waiting for the Hotan teleport.");
            Container.AppendDiagnostic("Return scroll accepted; waiting for teleport start and completion.");
            return;
        }

        if (DateTime.UtcNow - _returnScrollStartedUtc >= ReturnScrollTimeout)
        {
            Fail("Return scroll timed out before the Hotan teleport completed.");
            return;
        }

        // Return scrolls do not reliably produce OnTeleportStart: the core only
        // raises it when a Teleportation object exists. OnTeleportComplete is the
        // authoritative signal, matching the Training bot's proven behavior.
        if (!_returnTeleportCompleted || DateTime.UtcNow < _returnTeleportSettleUntilUtc)
            return;

        if (!MagicPopPrerequisiteValidator.IsAtHotanReturnPoint(out var distance))
        {
            Fail(
                $"Return scroll did not arrive at the configured Hotan return point " +
                $"(current {Game.Player.Position}, expected {MagicPopPrerequisiteValidator.HotanReturnPoint}, " +
                $"distance {distance:0.0}, allowed {MagicPopPrerequisiteValidator.HotanReturnPointTolerance:0})."
            );
            return;
        }

        if (!Game.Clientless)
        {
            ClientlessManager.GoClientless();
            Log.Notify("[Magic POP] Switched to clientless mode before Item Mall and Magic POP operations.");
            Container.AppendDiagnostic("Hotan return point confirmed; switched to clientless mode.");
        }
        else
            Container.AppendDiagnostic("Hotan return point confirmed; already running clientless.");

        SetState(MagicPopRunState.BuyingCards);
    }

    private void TickBuyingCards()
    {
        if (_waitingForPurchase)
        {
            if (_purchaseResult != null && PurchasedCardsArePresent(_purchaseResult))
            {
                var purchasedQuantity = _purchaseResult.Quantity;
                _purchasedCardCount += purchasedQuantity;
                _purchaseResult = null;
                _waitingForPurchase = false;
                _nextPurchaseUtc = DateTime.UtcNow + MinimumPurchaseInterval;
                Log.Notify($"[Magic POP] Purchased {purchasedQuantity} card(s); inventory update confirmed.");
                Container.AppendDiagnostic(
                    $"Purchased {purchasedQuantity} card(s); inventory update confirmed. " +
                    $"Waiting {MinimumPurchaseInterval.TotalMilliseconds:0} ms before the next purchase."
                );
                return;
            }

            if (DateTime.UtcNow - _purchaseStartedUtc >= OperationTimeout)
            {
                _waitingForPurchase = false;
                _purchaseResult = null;
                _purchasesAvailable = false;
                Log.Warn("[Magic POP] Card purchase or its inventory confirmation timed out; no purchase will be retried in this run.");
                Container.AppendDiagnostic("Card purchase timed out; continuing with existing cards only.");
            }
            return;
        }

        if (DateTime.UtcNow < _nextPurchaseUtc)
            return;

        var balance = Container.SilkBalance.Current;
        if (_purchasesAvailable && Game.Player.Inventory.FreeSlots > 0 && balance?.Silk > 0)
        {
            var request = MagicPopCardPurchaseProtocol.CreateRequest(Container.References.CardPackage, 1);
            _waitingForPurchase = true;
            _purchaseResult = null;
            _purchaseStartedUtc = DateTime.UtcNow;
            Log.Debug($"[Magic POP] Requested one card; free slots={Game.Player.Inventory.FreeSlots}, Silk={balance.Silk}.");
            Container.AppendDiagnostic(
                $"Card purchase sent: shop=0x{Container.References.CardPackage.EncodedShopTabId:X8}, " +
                $"slot={Container.References.CardPackage.ShopGood.SlotIndex}, " +
                $"package=0x{Container.References.CardPackage.PackageId:X8}, Silk={balance.Silk}."
            );
            PacketManager.SendPacket(request, PacketDestination.Server);
            return;
        }

        if (balance == null)
            Log.Warn("[Magic POP] Silk balance is unknown; automatic card purchase is skipped.");
        else if (balance.Silk == 0)
            Log.Notify("[Magic POP] Silk is exhausted; continuing without further purchases.");

        if (FindCard() != null)
        {
            StartRoute(
                _atPotionShop ? MagicPopScriptPaths.PotionToMachine : MagicPopScriptPaths.TeleportToMachine,
                _atPotionShop ? MagicPopRunState.ReturningToMachine : MagicPopRunState.MovingToMachine,
                MagicPopRunState.OpeningMachine
            );
            return;
        }

        BeginCleanupOrFinish();
    }

    private void TickOpenMachine()
    {
        if (_task == null)
        {
            _task = Task.Run(OpenMachine);
            _taskSuccessState = MagicPopRunState.Rolling;
            return;
        }

        TickTask();
    }

    private void TickRolling()
    {
        if (_targets.Count == 0 || FindCard() == null)
        {
            BeginCleanupOrFinish();
            return;
        }

        if (DateTime.UtcNow < _nextRollUtc)
            return;

        if (!TryFindNpc(MachineCodeName, out var machine))
        {
            Fail("Magic POP machine is not available after completing the route.");
            return;
        }

        var card = FindCard();
        var operation = new MagicPopOperation(machine.UniqueId, _targets[0], card.Slot, DateTime.UtcNow);
        _roll = new MagicPopRollCorrelation(operation);
        PersistPendingRoll(operation);
        PacketManager.SendPacket(
            MagicPopPlayProtocol.CreateRequest(operation.NpcUniqueId, operation.Target.GachaId, operation.CardSlot),
            PacketDestination.Server
        );
        SetState(MagicPopRunState.WaitingForRollResult);
        Log.Notify(
            $"[Magic POP] Roll sent: {operation.Target.DisplayName}, GachaID {operation.Target.GachaId}, card slot {operation.CardSlot}."
        );
        Container.AppendDiagnostic($"Roll sent: {operation.Target.DisplayName} (GachaID {operation.Target.GachaId}).");
    }

    private void TickWaitingForRoll()
    {
        if (_roll == null)
        {
            Fail("The in-flight roll state was lost.");
            return;
        }

        if (_roll.TryComplete(out var completion, out var error))
        {
            _rollCount++;
            if (completion.Outcome == MagicPopOutcome.Win)
            {
                _winCount++;
                Log.Notify($"[Magic POP] Target completed: {completion.Operation.Target.DisplayName}.");
                Container.AppendDiagnostic($"WIN: {completion.Operation.Target.DisplayName}; target removed from the queue.");
                var completedTarget = _targets[0];
                _targets.RemoveAt(0);
                Container.View.RemoveCompletedTarget(completedTarget.GachaId);
            }
            else
            {
                _lossCount++;
                Log.Notify($"[Magic POP] Roll lost for {completion.Operation.Target.DisplayName}.");
                Container.AppendDiagnostic($"LOSE: {completion.Operation.Target.DisplayName}.");
            }

            _roll = null;
            ClearPendingRoll(true);
            _nextRollUtc = DateTime.UtcNow + MinimumRollInterval;
            SetState(MagicPopRunState.Rolling);
            return;
        }

        if (error != null)
        {
            Fail(error);
            return;
        }

        if (_roll.HasTimedOut(DateTime.UtcNow, OperationTimeout))
            Fail("Roll timed out before both the play result and inventory update were received.");
    }

    private void TickSellingCoupons()
    {
        if (_task != null)
        {
            TickTask();
            return;
        }

        var coupon = FindLosingCoupon();
        if (coupon != null)
        {
            _task = Task.Run(() => SellCoupon(coupon));
            _taskSuccessState = MagicPopRunState.SellingLosingCoupons;
            return;
        }

        if (_targets.Count == 0)
        {
            LogRunSummary("all selected targets are complete and losing coupons were cleaned up");
            SetState(MagicPopRunState.Completed);
            return;
        }

        _atPotionShop = true;
        SetState(MagicPopRunState.BuyingCards);
    }

    private void BeginCleanupOrFinish()
    {
        if (FindLosingCoupon() != null)
        {
            if (_atPotionShop)
            {
                SetState(MagicPopRunState.SellingLosingCoupons);
                return;
            }

            if (!_atMachine)
            {
                StartRouteSequence(
                    new[] { MagicPopScriptPaths.TeleportToMachine, MagicPopScriptPaths.MachineToPotion },
                    MagicPopRunState.MovingToPotionShop,
                    MagicPopRunState.SellingLosingCoupons
                );
                return;
            }

            StartRoute(
                MagicPopScriptPaths.MachineToPotion,
                MagicPopRunState.MovingToPotionShop,
                MagicPopRunState.SellingLosingCoupons
            );
            return;
        }

        if (_targets.Count == 0)
        {
            LogRunSummary("all selected targets are complete");
            SetState(MagicPopRunState.Completed);
            return;
        }


        if (CanBuyMoreCards())
        {
            Log.Notify("[Magic POP] Cards are exhausted; returning to card purchase for the remaining targets.");
            SetState(MagicPopRunState.BuyingCards);
            return;
        }

        var exhaustionReason = GetResourceExhaustionReason();
        Log.Notify(exhaustionReason);
        LogRunSummary("resources are exhausted before all targets were completed");
        SetState(MagicPopRunState.Completed);
    }

    private void StartRoute(string path, MagicPopRunState routeState, MagicPopRunState successState)
    {
        StartRouteSequence(new[] { path }, routeState, successState);
    }

    private void StartRouteSequence(
        IEnumerable<string> paths,
        MagicPopRunState routeState,
        MagicPopRunState successState)
    {
        var routePaths = paths.ToArray();
        SetState(routeState);
        _taskSuccessState = successState;
        _task = Task.Run(() =>
        {
            if (!CloseActiveNpcInteraction())
                return false;

            foreach (var routePath in routePaths)
            {
                ScriptManager.Load(routePath);
                if (!ScriptManager.RunScriptWithResult(false))
                    return false;
            }

            return true;
        });
        Log.Notify(
            $"[Magic POP] Running route {string.Join(" -> ", routePaths.Select(System.IO.Path.GetFileName))}."
        );
        Container.AppendDiagnostic($"Running route: {string.Join(" -> ", routePaths.Select(System.IO.Path.GetFileName))}.");
    }

    private static bool CloseActiveNpcInteraction()
    {
        var selectedEntity = Game.SelectedEntity;
        if (selectedEntity == null)
            return true;

        Log.Debug($"[Magic POP] Closing active NPC interaction with entity {selectedEntity.UniqueId} before movement.");
        Container.AppendDiagnostic("Closing the active NPC interaction before starting the route.");
        if (!selectedEntity.TryDeselect())
        {
            Log.Warn($"[Magic POP] NPC interaction close was not acknowledged for entity {selectedEntity.UniqueId}.");
            Container.AppendDiagnostic("NPC interaction close was not acknowledged; route cannot start safely.");
            return false;
        }

        if (Game.SelectedEntity?.UniqueId == selectedEntity.UniqueId)
            Game.SelectedEntity = null;

        return true;
    }

    private void TickTask()
    {
        if (_task == null || !_task.IsCompleted)
            return;

        if (_task.IsFaulted)
        {
            Fail(_task.Exception?.GetBaseException().Message ?? "Background operation failed.");
            _task = null;
            return;
        }

        if (!_task.Result)
        {
            Fail($"Operation in state {_state} failed or timed out.");
            _task = null;
            return;
        }

        _task = null;
        if (_taskSuccessState == MagicPopRunState.OpeningMachine)
        {
            _atMachine = true;
            _atPotionShop = false;
        }
        else if (_taskSuccessState == MagicPopRunState.SellingLosingCoupons)
        {
            _atMachine = false;
            _atPotionShop = true;
        }
        SetState(_taskSuccessState);
    }

    private static bool OpenMachine()
    {
        if (!TryFindNpc(MachineCodeName, out var machine) || !machine.TrySelect())
            return false;

        var request = new Packet(0x7046);
        request.WriteUInt(machine.UniqueId);
        request.WriteByte(TalkOption.MagicPopPlay);
        var callback = new AwaitCallback(
            response => response.ReadByte() == 1 && response.ReadByte() == (byte)TalkOption.MagicPopPlay
                ? AwaitCallbackResult.Success
                : AwaitCallbackResult.Fail,
            0xB046
        );
        PacketManager.SendPacket(request, PacketDestination.Server, callback);
        callback.AwaitResponse(3_000);
        return callback.IsCompleted;
    }

    private static bool SellCoupon(InventoryItem coupon)
    {
        if (!TryFindNpc(PotionNpcCodeName, out var potionNpc))
        {
            Log.Warn($"[Magic POP] Cannot sell losing coupon: Hotan potion NPC {PotionNpcCodeName} is not spawned nearby.");
            Container.AppendDiagnostic($"Cannot sell losing coupon: {PotionNpcCodeName} is not nearby.");
            return false;
        }

        if (!potionNpc.TrySelect())
        {
            Log.Warn($"[Magic POP] Cannot sell losing coupon: selecting {PotionNpcCodeName} ({potionNpc.UniqueId}) failed.");
            Container.AppendDiagnostic("Cannot sell losing coupon: potion NPC selection failed.");
            return false;
        }

        var request = MagicPopCouponSellProtocol.CreateRequest(coupon, potionNpc.UniqueId);
        var callback = new AwaitCallback(
            response => MagicPopCouponSellProtocol.TryParseResponse(
                response,
                coupon.Slot,
                coupon.Amount,
                potionNpc.UniqueId,
                out _)
                ? AwaitCallbackResult.Success
                : AwaitCallbackResult.Fail,
            MagicPopCouponSellProtocol.ResponseOpcode
        );
        PacketManager.SendPacket(request, PacketDestination.Server, callback);
        callback.AwaitResponse(5_000);
        if (callback.IsCompleted)
        {
            Log.Notify($"[Magic POP] Sold losing coupon from slot {coupon.Slot}.");
            Container.AppendDiagnostic($"Sold losing coupon from inventory slot {coupon.Slot}.");
        }
        else
        {
            Log.Warn($"[Magic POP] Losing-coupon sale was not confirmed for slot {coupon.Slot} at NPC {potionNpc.UniqueId}.");
            Container.AppendDiagnostic($"Losing-coupon sale was not confirmed for inventory slot {coupon.Slot}.");
        }
        return callback.IsCompleted;
    }

    internal bool IsReturningToHotan
    {
        get
        {
            lock (_syncRoot)
                return _state == MagicPopRunState.ReturningToHotan;
        }
    }

    internal bool HandleTeleportStart()
    {
        lock (_syncRoot)
        {
            if (_state != MagicPopRunState.ReturningToHotan || !_returnScrollRequested)
                return false;

            _returnTeleportStarted = true;
            Container.AppendDiagnostic("Return-scroll teleport started.");
            return true;
        }
    }

    internal void HandleTeleportComplete()
    {
        lock (_syncRoot)
        {
            if (_state != MagicPopRunState.ReturningToHotan || !_returnScrollRequested)
                return;

            _returnTeleportCompleted = true;
            _returnTeleportSettleUntilUtc = DateTime.UtcNow + ReturnTeleportSettleTime;
            if (!_returnTeleportStarted)
                Container.AppendDiagnostic("Return teleport completion received without a separate start event.");
            Container.AppendDiagnostic("Return-scroll teleport completed; waiting for position data to settle.");
        }
    }

    private void OnPlayResult(MagicPopPlayResult result)
    {
        lock (_syncRoot)
        {
            if (_state != MagicPopRunState.WaitingForRollResult || _roll == null)
                return;
            if (!_roll.ObservePlayResult(result, out var error))
                Fail(error);
        }
    }

    private void OnInventoryItemUpdated(byte slot)
    {
        lock (_syncRoot)
        {
            if (_state == MagicPopRunState.BuyingCards && _waitingForPurchase && _purchaseResult != null)
                return;

            if (_state != MagicPopRunState.WaitingForRollResult || _roll == null || slot != _roll.Operation.CardSlot)
                return;

            var item = Game.Player?.Inventory.GetItemAt(slot);
            if (!_roll.ObserveInventoryItem(item, out var error))
                Fail(error);
        }
    }

    private void OnProtocolError(string error)
    {
        lock (_syncRoot)
            Fail(error);
    }

    private void OnCardPurchased(MagicPopCardPurchaseResult result)
    {
        lock (_syncRoot)
        {
            if (_state != MagicPopRunState.BuyingCards || !_waitingForPurchase)
                return;

            _purchaseResult = result;
            Log.Debug($"[Magic POP] Card purchase response received for {result.DestinationSlots.Length} destination slot(s); awaiting inventory confirmation.");
        }
    }

    private void OnCardPurchaseError(string error)
    {
        lock (_syncRoot)
        {
            if (_state != MagicPopRunState.BuyingCards || !_waitingForPurchase)
                return;

            _waitingForPurchase = false;
            _purchaseResult = null;
            _purchasesAvailable = false;
            Log.Warn($"[Magic POP] Card purchase stopped: {error} Continuing with existing cards.");
            Container.AppendDiagnostic($"Card purchase unavailable: {error} Continuing with existing cards.");
        }
    }

    private void SetState(MagicPopRunState state)
    {
        if (_state != state)
        {
            Log.Debug($"[Magic POP] State {_state} -> {state}.");
            Container.AppendDiagnostic($"State: {_state} -> {state}.");
        }
        _state = state;
        Container.View.UpdateProgress(
            state,
            _targets.FirstOrDefault(),
            _rollCount,
            _winCount,
            _lossCount
        );
    }

    private void Fail(string error)
    {
        if (!string.IsNullOrWhiteSpace(error) && _fault == null)
            _fault = error;
    }

    public void ClearPendingRecovery()
    {
        lock (_syncRoot)
        {
            if (_roll == null)
                ClearPendingRoll(false);
        }
    }

    private void PersistPendingRoll(MagicPopOperation operation)
    {
        PlayerConfig.Set(
            PendingRollConfigKey,
            $"{operation.Target.GachaId}|{operation.Target.RefItemId}|{operation.CardSlot}"
        );
        PlayerConfig.Save(false);
    }

    private static void ClearPendingRoll(bool save)
    {
        PlayerConfig.Set(PendingRollConfigKey, string.Empty);
        if (save)
            PlayerConfig.Save(false);
    }

    private void ReconcilePendingRoll()
    {
        var serialized = PlayerConfig.Get(PendingRollConfigKey, string.Empty);
        if (string.IsNullOrWhiteSpace(serialized))
            return;

        var parts = serialized.Split('|');
        if (parts.Length != 3
            || !uint.TryParse(parts[0], out var gachaId)
            || !uint.TryParse(parts[1], out var refItemId)
            || !byte.TryParse(parts[2], out var slot))
        {
            Fail("Saved pending-roll recovery data is malformed. Modify the target list to discard it before retrying.");
            return;
        }

        var target = _targets.FirstOrDefault(candidate =>
            candidate.GachaId == gachaId && candidate.RefItemId == refItemId
        );
        if (target == null)
        {
            Fail("Saved pending-roll recovery data does not match the current target queue. Modify the target list to discard it before retrying.");
            return;
        }

        var item = Game.Player?.Inventory?.GetItemAt(slot);
        var codeName = item?.Record?.CodeName;
        if (string.Equals(codeName, CardCodeName, StringComparison.OrdinalIgnoreCase))
        {
            Log.Notify("[Magic POP] Recovered an unconsumed pending card; the interrupted roll will be attempted again normally.");
            ClearPendingRoll(true);
            return;
        }

        if (string.Equals(codeName, LosingCouponCodeName, StringComparison.OrdinalIgnoreCase))
        {
            _rollCount++;
            _lossCount++;
            Log.Notify($"[Magic POP] Recovered a completed losing roll for {target.DisplayName}.");
            ClearPendingRoll(true);
            return;
        }

        if (string.Equals(codeName, "ITEM_MALL_GACHA_CARD_WIN", StringComparison.OrdinalIgnoreCase)
            && item.GachaParameters?.Any(parameter => parameter.Id == refItemId) == true)
        {
            _rollCount++;
            _winCount++;
            _targets.Remove(target);
            Container.View.RemoveCompletedTarget(target.GachaId);
            Log.Notify($"[Magic POP] Recovered completed target {target.DisplayName} from its verified winning coupon.");
            ClearPendingRoll(true);
            return;
        }

        Fail($"Pending roll slot {slot} contains an unknown or mismatched result; no paid operation will be started.");
    }

    private static InventoryItem FindCard()
    {
        return FindInventoryItem(CardCodeName);
    }

    private static InventoryItem FindLosingCoupon()
    {
        return FindInventoryItem(LosingCouponCodeName);
    }

    private static InventoryItem FindInventoryItem(string codeName)
    {
        return Game.Player?.Inventory.FirstOrDefault(item =>
            string.Equals(item.Record?.CodeName, codeName, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static bool PurchasedCardsArePresent(MagicPopCardPurchaseResult result)
    {
        return Game.Player?.Inventory != null
            && result.DestinationSlots.All(slot =>
                string.Equals(
                    Game.Player.Inventory.GetItemAt(slot)?.Record?.CodeName,
                    CardCodeName,
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    private bool CanBuyMoreCards()
    {
        var balance = Container.SilkBalance.Current;
        return _targets.Count > 0
            && _purchasesAvailable
            && Game.Player?.Inventory?.FreeSlots > 0
            && balance?.Silk > 0;
    }

    private string GetResourceExhaustionReason()
    {
        if (Game.Player?.Inventory?.FreeSlots == 0)
            return "[Magic POP] Stopped: no cards or losing coupons remain, and the inventory has no free slot for another card.";
        if (!_purchasesAvailable)
            return "[Magic POP] Stopped: no cards remain and automatic card purchase is unavailable for this run.";

        var balance = Container.SilkBalance.Current;
        return balance == null
            ? "[Magic POP] Stopped: no cards remain and the Silk balance is unknown."
            : "[Magic POP] Stopped: no cards remain and Silk is exhausted.";
    }

    private void LogRunSummary(string reason)
    {
        var currentSilk = Container.SilkBalance.Current?.Silk;
        var silkSpent = _startingSilk.HasValue && currentSilk.HasValue && _startingSilk.Value >= currentSilk.Value
            ? (_startingSilk.Value - currentSilk.Value).ToString()
            : "unknown";
        var summary = $"Run complete: {reason}. Purchased cards={_purchasedCardCount}, rolls={_rollCount}, wins={_winCount}, losses={_lossCount}, Silk spent={silkSpent}.";
        Log.Notify($"[Magic POP] {summary}");
        Container.AppendDiagnostic(summary);
    }

    private static bool TryFindNpc(string codeName, out SpawnedNpcNpc npc)
    {
        return SpawnManager.TryGetEntity<SpawnedNpcNpc>(
            entity => string.Equals(entity.Record?.CodeName, codeName, StringComparison.OrdinalIgnoreCase),
            out npc
        );
    }
}
