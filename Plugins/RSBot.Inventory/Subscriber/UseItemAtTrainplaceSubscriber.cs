using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Event;

namespace RSBot.Inventory.Subscriber;

internal class UseItemAtTrainplaceSubscriber
{
    private const int SCAN_INTERVAL = 1_000;
    private const int RETRY_DELAY = 5 * 60 * 1_000;

    private static readonly Dictionary<string, int> _blacklistedItems = new();
    private static int _lastScanTick;

    public static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnTick", OnTick);
    }

    private static void OnTick()
    {
        if (Kernel.TickCount - _lastScanTick < SCAN_INTERVAL)
            return;

        _lastScanTick = Kernel.TickCount;

        foreach (
            var expiredItem in _blacklistedItems
                .Where(entry => Kernel.TickCount - entry.Value >= RETRY_DELAY)
                .Select(entry => entry.Key)
                .ToArray()
        )
            _blacklistedItems.Remove(expiredItem);

        if (!Kernel.Bot.Running || Kernel.Bot.Botbase.Area.Position.Region == 0)
            return;

        //Only at training place
        if (Kernel.Bot.Botbase.Area.Position.DistanceToPlayer() > 100)
            return;

        var itemsToUse = PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace");

        foreach (var item in itemsToUse)
        {
            if (_blacklistedItems.ContainsKey(item))
                continue;

            var invItem = Game.Player.Inventory.GetItem(item);
            if (invItem == null)
                continue;

            if (invItem.ItemSkillInUse)
                continue;

            Log.Notify($"Use [{invItem.Record.GetRealName()}] at training place");

            if (invItem.Use())
                continue;

            //e.g. overlapping with another buff
            _blacklistedItems[invItem.Record.CodeName] = Kernel.TickCount;

            Log.Warn(
                $"Can not use item [{invItem.Record.GetRealName()}] at training place. Blacklisting it for 5 minutes before next try."
            );
        }
    }
}
