using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;
using RSBot.MagicPop.Model;
using RSBot.MagicPop.References;

namespace RSBot.MagicPop.Validation;

internal static class MagicPopPrerequisiteValidator
{
    internal const double HotanReturnPointTolerance = 100;
    // RBS move coordinates are sector offsets, not world-map X/Y values.
    // HotanTeleportToMagicPop.rbs starts at: move 1326 377 244 135 92.
    internal static readonly Position HotanReturnPoint = new(135, 92, 1326, 377, 244);

    public static MagicPopValidationResult Validate(
        GachaReferenceCatalog references,
        IReadOnlyCollection<MagicPopTarget> targets)
    {
        var result = new MagicPopValidationResult();

        if (!references.IsValid)
            result.Errors.Add($"Magic POP reference data is invalid: {references.Error ?? "not loaded"}");
        if (!Game.Ready || Game.Player == null)
            result.Errors.Add("A fully loaded character is required.");
        if (targets == null || targets.Count == 0)
            result.Errors.Add("Select at least one Magic POP target.");
        if (ScriptManager.Running)
            result.Errors.Add("Another walk script is already running.");

        ValidateScripts(result);
        ValidateTargets(references, targets, result);

        if (Game.Player != null)
        {
            var returnScrollFilter = new TypeIdFilter(3, 3, 3, 1);
            var returnScroll = Game.Player.Inventory.GetItem(item =>
                item.Record != null
                && returnScrollFilter.EqualsRefItem(item.Record)
                && item.Record.ReqLevel1 <= Game.Player.Level
            );
            if (returnScroll == null)
                result.Errors.Add("A usable return scroll is required in the character inventory.");
            else if (Game.Player.State.ScrollState != ScrollState.Cancel)
                result.Errors.Add("A return-scroll or teleport operation is already in progress.");
            else
                result.Information.Add("A return scroll will be used before switching to clientless Magic POP execution.");

            AddInventorySummary(result);
        }

        return result;
    }

    internal static bool IsAtHotanReturnPoint(out double distance)
    {
        distance = double.MaxValue;
        if (Game.Player == null)
            return false;

        distance = Game.Player.Position.DistanceTo(HotanReturnPoint);
        return distance <= HotanReturnPointTolerance;
    }

    private static void ValidateScripts(MagicPopValidationResult result)
    {
        var expectedEndpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MagicPopScriptPaths.TeleportToMachine] = "move 1596 731 243 135 92",
            [MagicPopScriptPaths.MachineToPotion] = "move 897 1053 243 135 92",
            [MagicPopScriptPaths.PotionToMachine] = "move 1596 731 243 135 92"
        };

        foreach (var script in MagicPopScriptPaths.All)
        {
            if (!File.Exists(script))
            {
                result.Errors.Add($"Required route script is missing: {script}");
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(script);
            }
            catch (Exception exception)
            {
                result.Errors.Add($"Required route script cannot be read: {script} ({exception.Message})");
                continue;
            }

            var moves = lines
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("move ", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (moves.Length == 0)
            {
                result.Errors.Add($"Required route script contains no move commands: {script}");
                continue;
            }

            if (!string.Equals(moves[^1], expectedEndpoints[script], StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(
                    $"Route script has an unexpected destination: {Path.GetFileName(script)} (last move: '{moves[^1]}')."
                );
            }
        }
    }

    private static void ValidateTargets(
        GachaReferenceCatalog references,
        IEnumerable<MagicPopTarget> targets,
        MagicPopValidationResult result)
    {
        if (targets == null)
            return;

        var rewards = references.Rewards.ToDictionary(reward => reward.Source.GachaId);
        foreach (var target in targets)
        {
            if (!rewards.TryGetValue(target.GachaId, out var reward)
                || reward.Source.RefItemId != target.RefItemId
                || !string.Equals(reward.CodeName, target.CodeName, StringComparison.Ordinal))
            {
                result.Errors.Add(
                    $"Selected target is stale or invalid: GachaID {target.GachaId}, RefItemID {target.RefItemId}, {target.CodeName}."
                );
            }
        }
    }

    private static void AddInventorySummary(MagicPopValidationResult result)
    {
        var inventory = Game.Player.Inventory;
        var cards = SumItems(inventory, "ITEM_MALL_GACHA_CARD");
        var wins = SumItems(inventory, "ITEM_MALL_GACHA_CARD_WIN");
        var losses = SumItems(inventory, "ITEM_MALL_GACHA_CARD_LOSE");

        result.Information.Add(
            $"Inventory: {cards} Magic POP cards, {wins} winning coupons, {losses} losing coupons, {inventory.FreeSlots} free slots."
        );
    }

    private static int SumItems(IEnumerable<InventoryItem> inventory, string codeName)
    {
        return inventory
            .Where(item => string.Equals(item.Record?.CodeName, codeName, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Amount);
    }
}
