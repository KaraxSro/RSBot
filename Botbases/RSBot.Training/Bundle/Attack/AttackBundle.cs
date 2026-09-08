using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.Training.Bundle.Attack;

internal class AttackBundle : IBundle
{
    private const int ATTACK_DECISION_INTERVAL = 100;

    /// <summary>
    ///     The last tick count for checking func call
    /// </summary>
    private int _lastTick = Kernel.TickCount;

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (Game.SelectedEntity == null || !Game.Player.CanAttack)
            return;

        if (Game.SelectedEntity.IsBehindObstacle)
        {
            Log.Debug("Deselecting entity because it moved behind an obstacle!");

            if (Game.Player.InAction)
                SkillManager.CancelAction();

            // Use the common obstacle handler so the target is blacklisted and
            // movement is allowed to reposition instead of selecting it again.
            EventManager.FireEvent("OnTargetBehindObstacle");

            return;
        }

        bool dontFollowMobs = PlayerConfig.Get<bool>("RSBot.Training.checkBoxDontFollowMobs");
        if (dontFollowMobs && !Kernel.Bot.Botbase.Area.IsInSight(Game.SelectedEntity))
        {
            Log.Debug("Deselecting entity because it moved far away from training area!");

            if (Game.Player.InAction)
                SkillManager.CancelAction();

            Game.SelectedEntity?.TryDeselect();
            Game.SelectedEntity = null;

            double distance = Game.Player.Position.DistanceTo(Container.Bot.Area.Position);
            bool hasCollision = Game.Player.Position.HasCollisionBetween(Container.Bot.Area.Position);

            if (distance > Container.Bot.Area.Radius && !hasCollision)
                Game.Player.MoveTo(Container.Bot.Area.Position, false);

            return;
        }

        if (SkillManager.CastPending)
            return;

        if (
            SkillManager.ImbueSkill != null
            && !Game.Player.State.HasActiveBuff(SkillManager.ImbueSkill, out _)
            && SkillManager.ImbueSkill.CanBeCasted
        )
        {
            SkillManager.CastBuff(SkillManager.ImbueSkill, awaitBuffResponse: false);
            return;
        }

        // A non-basic action must finish before another combat skill request is sent.
        // Imbue is handled above because it can be refreshed without interrupting the current action.
        if (Game.Player.InAction && !SkillManager.IsLastCastedBasic)
            return;

        if (Kernel.TickCount - _lastTick < ATTACK_DECISION_INTERVAL)
            return;

        _lastTick = Kernel.TickCount;

        var useTeleportSkill = PlayerConfig.Get("RSBot.Skills.checkUseTeleportSkill", false);
        if (useTeleportSkill && CastTeleportation())
            return;

        //var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var skill = SkillManager.GetNextSkill();

        //Log.Debug($"Getnextskill: {stopwatch.ElapsedMilliseconds} Action:{Game.Player.InAction} Entity:{Game.SelectedEntity != null} LA:{SkillManager.IsLastCastedBasic} Skill:{skill}");

        if (!Game.Player.InAction)
            Log.Status("Attacking");

        if (skill == null)
        {
            if (Game.Player.InAction)
                return;

            if (PlayerConfig.Get("RSBot.Skills.checkUseDefaultAttack", true))
                SkillManager.CastAutoAttack();

            return;
        }

        if (Game.Player.InAction && SkillManager.IsLastCastedBasic)
        {
            SkillManager.CancelAction();
            return;
        }

        var uniqueId = Game.SelectedEntity?.UniqueId;
        if (uniqueId == null)
            return;

        skill?.Cast(uniqueId.Value);
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        //Nothing to do here
    }

    public void Stop()
    {
        _lastTick = Kernel.TickCount;
    }

    /// <summary>
    ///     Casts the teleportation skill if it's set up.
    /// </summary>
    /// <returns></returns>
    private bool CastTeleportation()
    {
        if (SkillManager.TeleportSkill?.CanBeCasted != true || Game.SelectedEntity?.State.LifeState != LifeState.Alive)
            return false;

        var distanceToMonster = Game.SelectedEntity?.DistanceToPlayer;
        var availableDistance = SkillManager.TeleportSkill.Record.Params[3] / 10;

        if (availableDistance <= 0)
        {
            Log.Warn("The selected teleportation skill does not have a distance. Is this really a teleport skill?");
        }
        else
        {
            var distanceAfterCasting = distanceToMonster - availableDistance;
            if (distanceAfterCasting < 0)
                distanceAfterCasting *= -1;

            if (distanceAfterCasting < distanceToMonster)
            {
                SkillManager.TeleportSkill.CastAt(Game.SelectedEntity.Position);

                Log.Debug(
                    $"Used teleportation skill [{SkillManager.TeleportSkill.Record.GetRealName()}] (before: {distanceToMonster}m, after: {distanceAfterCasting}m, traveled: {availableDistance}m)"
                );

                return true;
            }
        }

        return false;
    }
}
