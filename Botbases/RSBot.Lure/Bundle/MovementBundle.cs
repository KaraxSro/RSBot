using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Lure.Components;

namespace RSBot.Lure.Bundle;

internal static class MovementBundle
{
    private const int MAX_DESTINATION_ATTEMPTS = 12;

    public static void Tick()
    {
        if (
            LureConfig.UseSpeedDrug
            && Game.Player.State.ActiveBuffs.FindIndex(p => p.Record.Params.Contains(1752396901)) < 0
        )
        {
            var item = Game.Player.Inventory.GetItem(
                new TypeIdFilter(3, 3, 13, 1),
                p => p.Record.Desc1.Contains("_SPEED_")
            );
            item?.Use();
        }

        //Return to center?
        if (LureConfig.Area.Position.DistanceToPlayer() > 5 && LureConfig.StayAtCenterFor && !ScriptManager.Running)
        {
            Log.Status("Walking back to center...");

            Log.Debug("[Lure] Walking back to center");

            var moved = Game.Player.MoveTo(LureConfig.Area.Position, false);
            if (!moved)
                return;

            Log.Debug($"[Lure] Waiting at the center for {LureConfig.StayAtCenterForSeconds}s");

            EventManager.FireEvent(
                "OnChangeStatusText",
                $"Waiting for {LureConfig.StayAtCenterForSeconds}s at center..."
            );
            Thread.Sleep(LureConfig.StayAtCenterForSeconds * 1000);

            return;
        }

        if (LureConfig.UseScript)
        {
            if (!File.Exists(LureConfig.SelectedScriptPath))
            {
                Log.Error($"[Lure] The file for the script {LureConfig.SelectedScriptPath} does not exist!");

                Kernel.Bot.Stop();

                return;
            }

            if (ScriptManager.Running)
                return;

            Log.Status("Running lure script...");
            ScriptManager.Load(LureConfig.SelectedScriptPath);
            Task.Run(() => ScriptManager.RunScript(false));
        }

        if (LureConfig.StayAtCenter || Game.Player.Movement.Moving)
            return;

        var minDistance = LureConfig.Area.Radius / 1.5f;
        var destination = default(Position);
        var destinationFound = false;
        for (var attempt = 0; attempt < MAX_DESTINATION_ATTEMPTS; attempt++)
        {
            var candidate = LureConfig.Area.GetRandomPosition();
            if (candidate.DistanceToPlayer() < minDistance || Game.Player.Position.HasCollisionBetween(candidate))
                continue;

            destination = candidate;
            destinationFound = true;
            break;
        }

        if (!destinationFound)
        {
            Log.Debug($"[Lure] Could not find a collision-free random position after {MAX_DESTINATION_ATTEMPTS} attempts.");
            return;
        }

        Log.Status("Walking to random position...");
        Log.Debug(
            $"[Lure] Moving to random position {destination} (distance={destination.DistanceToPlayer()}, min. distance={minDistance})"
        );
        Game.Player.MoveTo(destination);
    }
}
