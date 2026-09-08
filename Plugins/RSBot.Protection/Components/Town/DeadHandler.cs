using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;

namespace RSBot.Protection.Components.Town;

public class DeadHandler : AbstractTownHandler
{
    private static int _handlingDeath;

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
        EventManager.SubscribeEvent("OnPlayerDied", OnPlayerDied);
    }

    /// <summary>
    ///     Cores the entity life state changed.
    /// </summary>
    /// <param name="uniqueId">The unique identifier.</param>
    private static async void OnPlayerDied()
    {
        if (!Kernel.Bot.Running)
            return;

        if (Interlocked.CompareExchange(ref _handlingDeath, 1, 0) != 0)
            return;

        try
        {
            if (Game.Player.Level < 10)
            {
                await Task.Delay(5000);
                ResurrectAtSpawnPoint(2);
                return;
            }

            if (!PlayerConfig.Get<bool>("RSBot.Protection.checkDead"))
                return;

            if (Game.Player.State.LifeState != LifeState.Dead)
                return;

            var itemsToUse = PlayerConfig.GetArray<string>("RSBot.Inventory.AutoUseAccordingToPurpose");
            var inventoryItem = Game.Player.Inventory.GetItem(
                new TypeIdFilter(3, 3, 13, 6),
                p => itemsToUse.Contains(p.Record.CodeName)
            );
            if (inventoryItem != null)
            {
                var result = await inventoryItem.UseAsync();
                if (await WaitForResurrection(5_000))
                    return;

                Log.Warn(
                    $"Resurrection item [{inventoryItem.Record.GetRealName()}] did not resurrect the player ({result})."
                );
            }

            var timeOut = PlayerConfig.Get("RSBot.Protection.numDeadTimeout", 30);
            Log.WarnLang("ResurrectSPointSeconds", timeOut);
            await Task.Delay(timeOut * 1000);

            if (Game.Player?.State.LifeState != LifeState.Dead)
                return;

            ResurrectAtSpawnPoint(1);
        }
        finally
        {
            Interlocked.Exchange(ref _handlingDeath, 0);
        }
    }

    private static async Task<bool> WaitForResurrection(int timeout)
    {
        var deadline = Environment.TickCount64 + timeout;
        while (Game.Player?.State.LifeState == LifeState.Dead && Environment.TickCount64 < deadline)
            await Task.Delay(100);

        return Game.Player?.State.LifeState == LifeState.Alive;
    }

    private static void ResurrectAtSpawnPoint(byte mode)
    {
        var packet = new Packet(0x3053);
        packet.WriteByte(mode);
        PacketManager.SendPacket(packet, PacketDestination.Server); //Only works if not teleporting at that moment
    }
}
