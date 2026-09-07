using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Training.Bundle.Movement;

internal class MovementBundle : IBundle
{
    /// <summary>
    ///     Gets or sets the configuration.
    /// </summary>
    /// <value>
    ///     The configuration.
    /// </value>
    public MovementConfig Config { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether [last entity was behind obstacle].
    ///     Used to move around even though the player is being attacked.
    /// </summary>
    /// <value>
    ///     <c>true</c> if [last entity was behind obstacle]; otherwise, <c>false</c>.
    /// </value>
    public bool LastEntityWasBehindObstacle { get; set; }

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (Game.SelectedEntity != null && !LastEntityWasBehindObstacle)
            return;

        var playerUnderAttack = SpawnManager.Any<SpawnedMonster>(m =>
            m.AttackingPlayer && Container.Bot.Area.IsInSight(m)
        );
        if (playerUnderAttack && !LastEntityWasBehindObstacle)
            return;

        if (Game.Player.Movement.Moving)
            return;

        if (
            PlayerConfig.Get("RSBot.Party.AlwaysFollowPartyMaster", false)
            && Game.Party.IsInParty
            && !Game.Party.IsLeader
        )
        {
            if (Game.Player.InAction)
                return;

            var player = Game.Party.Leader?.Player;
            if (player != null && player.Position.DistanceToPlayer() >= 10)
                Game.Player.MoveTo(player.Position);

            return;
        }

        var distance = Game.Player.Position.DistanceTo(Container.Bot.Area.Position);
        var hasCollision = Game.Player.Position.HasCollisionBetween(Container.Bot.Area.Position);

        //Go back if the player is out of the radius
        if ((distance > Container.Bot.Area.Radius || (Config.WalkToCenter && distance > 3)) && !hasCollision)
        {
            Log.Status("Walking to center");
            Game.Player.MoveTo(Container.Bot.Area.Position);

            return;
        }

        if (Config.WalkToCenter)
            return;

        Log.Status("Walking around");

        const int maxDestinationAttempts = 6;
        for (var attempt = 0; attempt < maxDestinationAttempts; attempt++)
        {
            var destination = Container.Bot.Area.GetRandomPosition();
            if (distance >= Container.Bot.Area.Radius || !Game.Player.Position.HasCollisionBetween(destination))
            {
                Game.Player.MoveTo(destination, false);
                return;
            }
        }

        Log.Debug($"Could not find a collision-free random position after {maxDestinationAttempts} attempts.");
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        Config = new MovementConfig
        {
            WalkAround = PlayerConfig.Get("RSBot.Training.radioWalkAround", true),
            WalkToCenter = PlayerConfig.Get<bool>("RSBot.Training.radioCenter"),
        };
    }

    public void Stop()
    {
        LastEntityWasBehindObstacle = false;
    }
}
