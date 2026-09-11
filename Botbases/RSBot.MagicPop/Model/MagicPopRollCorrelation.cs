using System;
using System.Linq;
using RSBot.Core.Objects;
using RSBot.MagicPop.Protocol;

namespace RSBot.MagicPop.Model;

internal sealed class MagicPopRollCorrelation
{
    private const string WinningCouponCodeName = "ITEM_MALL_GACHA_CARD_WIN";
    private const string LosingCouponCodeName = "ITEM_MALL_GACHA_CARD_LOSE";

    private MagicPopPlayResult _playResult;
    private string _couponCodeName;
    private uint[] _couponParameterIds;

    public MagicPopRollCorrelation(MagicPopOperation operation)
    {
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }

    public MagicPopOperation Operation { get; }
    public bool HasPlayResult => _playResult != null;
    public bool HasInventoryUpdate => _couponCodeName != null;

    public bool ObservePlayResult(MagicPopPlayResult result, out string error)
    {
        error = null;
        if (result == null)
        {
            error = "Magic POP play result is null.";
            return false;
        }

        if (_playResult != null)
        {
            error = "A second play result arrived for the same in-flight roll.";
            return false;
        }

        _playResult = result;
        return true;
    }

    public bool ObserveInventoryItem(InventoryItem item, out string error)
    {
        error = null;
        if (item == null || item.Record == null)
        {
            error = "Magic POP inventory update did not contain a known item.";
            return false;
        }

        if (item.Slot != Operation.CardSlot)
        {
            error = $"Inventory update arrived for slot {item.Slot}, expected card slot {Operation.CardSlot}.";
            return false;
        }

        if (!IsCoupon(item.Record.CodeName))
        {
            error = $"Card slot {item.Slot} changed to unexpected item '{item.Record.CodeName}'.";
            return false;
        }

        if (_couponCodeName != null)
        {
            error = "A second coupon update arrived for the same in-flight roll.";
            return false;
        }

        _couponCodeName = item.Record.CodeName;
        _couponParameterIds = item.GachaParameters?.Select(parameter => parameter.Id).ToArray()
            ?? Array.Empty<uint>();
        return true;
    }

    public bool TryComplete(out MagicPopRollCompletion completion, out string error)
    {
        completion = null;
        error = null;
        if (_playResult == null || _couponCodeName == null)
            return false;

        var expectedCodeName = _playResult.Outcome == MagicPopOutcome.Win
            ? WinningCouponCodeName
            : LosingCouponCodeName;
        if (!string.Equals(_couponCodeName, expectedCodeName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Play result {_playResult.Outcome} contradicts coupon '{_couponCodeName}'.";
            return false;
        }

        if (_playResult.Outcome == MagicPopOutcome.Win
            && !_couponParameterIds.Contains(Operation.Target.RefItemId))
        {
            error = $"Winning coupon does not identify expected RefItemID {Operation.Target.RefItemId}.";
            return false;
        }

        completion = new MagicPopRollCompletion(Operation, _playResult.Outcome);
        return true;
    }

    public bool HasTimedOut(DateTime utcNow, TimeSpan timeout)
    {
        return utcNow - Operation.StartedUtc >= timeout;
    }

    private static bool IsCoupon(string codeName)
    {
        return string.Equals(codeName, WinningCouponCodeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(codeName, LosingCouponCodeName, StringComparison.OrdinalIgnoreCase);
    }
}
