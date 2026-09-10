using RSBot.Core;
using RSBot.Core.Components;
using System.Linq;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Skill;

namespace RSBot.Training.Bundle.Attack;

internal class AttackBundle : IBundle
{
    private const int ATTACK_DECISION_INTERVAL = 100;
    private const int WAIT_LOG_THRESHOLD = 750;
    private const int WAIT_LOG_INTERVAL = 2_000;

    /// <summary>
    ///     The last tick count for checking func call
    /// </summary>
    private int _lastTick = Kernel.TickCount;

    private string _waitReason;
    private uint _waitTargetId;
    private int _waitStartedTick;
    private int _lastWaitLogTick;

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        var selectedEntity = Game.SelectedEntity;
        if (selectedEntity == null)
        {
            ResetWaitTrace();
            return;
        }

        if (!Game.Player.CanAttack)
        {
            TraceWait("player-cannot-attack", includeSkillStatus: false);
            return;
        }

        if (selectedEntity.IsBehindObstacle)
        {
            ResetWaitTrace();
            Log.Debug("Deselecting entity because it moved behind an obstacle!");

            if (Game.Player.InAction)
                SkillManager.CancelAction();

            // Use the common obstacle handler so the target is blacklisted and
            // movement is allowed to reposition instead of selecting it again.
            EventManager.FireEvent("OnTargetBehindObstacle");

            return;
        }

        bool dontFollowMobs = PlayerConfig.Get<bool>("RSBot.Training.checkBoxDontFollowMobs");
        if (dontFollowMobs && !Kernel.Bot.Botbase.Area.IsInSight(selectedEntity))
        {
            ResetWaitTrace();
            Log.Debug("Deselecting entity because it moved far away from training area!");

            if (Game.Player.InAction)
                SkillManager.CancelAction();

            selectedEntity.TryDeselect();
            if (object.ReferenceEquals(Game.SelectedEntity, selectedEntity))
                Game.SelectedEntity = null;

            double distance = Game.Player.Position.DistanceTo(Container.Bot.Area.Position);
            bool hasCollision = Game.Player.Position.HasCollisionBetween(Container.Bot.Area.Position);

            if (distance > Container.Bot.Area.Radius && !hasCollision)
                Game.Player.MoveTo(Container.Bot.Area.Position, false);

            return;
        }

        if (SkillManager.IsCancellationSettling)
        {
            TraceWait("cancel-action-settling", includeSkillStatus: false);
            return;
        }

        if (SkillManager.CastPending)
        {
            TraceWait("skill-request-pending", includeSkillStatus: false);
            return;
        }

        if (
            SkillManager.ImbueSkill != null
            && !SkillManager.IsImbuePending
            && !Game.Player.State.HasActiveBuff(SkillManager.ImbueSkill, out _)
            && SkillManager.ImbueSkill.CanBeCasted
        )
        {
            ResetWaitTrace();
            SkillManager.CastBuff(SkillManager.ImbueSkill, awaitBuffResponse: false);
            return;
        }

        if (
            Game.Player.InAction
            && SkillManager.CurrentCombatAction == CombatActionType.Skill
            && SkillManager
                .GetCurrentAttackSkills()
                .Any(candidate => candidate.Id == SkillManager.LastCastedSkillId)
        )
        {
            if (SkillManager.TryDispatchQueuedCombatSkillEarly())
            {
                ResetWaitTrace();
                return;
            }

            if (
                SkillManager.QueuedCombatSkillId == 0
                && Kernel.TickCount - _lastTick >= ATTACK_DECISION_INTERVAL
            )
            {
                _lastTick = Kernel.TickCount;
                var queuedSkill = SkillManager.GetNextSkill();
                if (queuedSkill != null)
                {
                    TraceDecision("queue-next-skill", queuedSkill, includeSkillStatus: false);
                    SkillManager.QueueNextCombatSkill(queuedSkill, selectedEntity.UniqueId);
                    return;
                }
            }

            TraceWait("skill-action-active", includeSkillStatus: true);
            return;
        }

        // Other non-basic actions must finish before another combat skill request is sent.
        // Imbue is handled above because it can be refreshed without interrupting the current action.
        if (Game.Player.InAction && !SkillManager.IsCurrentActionBasic)
        {
            TraceWait("non-basic-action-active", includeSkillStatus: true);
            return;
        }

        if (Kernel.TickCount - _lastTick < ATTACK_DECISION_INTERVAL)
            return;

        _lastTick = Kernel.TickCount;

        var useTeleportSkill = PlayerConfig.Get("RSBot.Skills.checkUseTeleportSkill", false);
        if (useTeleportSkill && CastTeleportation())
        {
            ResetWaitTrace();
            return;
        }

        //var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var skill = SkillManager.GetNextSkill();

        //Log.Debug($"Getnextskill: {stopwatch.ElapsedMilliseconds} Action:{Game.Player.InAction} Entity:{Game.SelectedEntity != null} LA:{SkillManager.IsLastCastedBasic} Skill:{skill}");

        if (!Game.Player.InAction)
            Log.Status("Attacking");

        if (skill == null)
        {
            if (Game.Player.InAction)
            {
                TraceWait("basic-attack-active-no-skill-ready", includeSkillStatus: true);
                return;
            }

            if (PlayerConfig.Get("RSBot.Skills.checkUseDefaultAttack", true))
            {
                TraceDecision("normal-attack-no-skill-ready", null, includeSkillStatus: true);
                SkillManager.CastAutoAttack();
            }
            else
            {
                TraceWait("no-skill-ready-default-attack-disabled", includeSkillStatus: true);
            }

            return;
        }

        if (
            Game.Player.InAction
            && SkillManager.CurrentCombatAction == CombatActionType.RecurringBasicAttack
        )
        {
            // The server automatically resumes bow basic attacks when the previous skill
            // did not kill the current target. A separate cancel round-trip leaves the
            // following skill in the tail of that action and can make the server silently
            // discard it. Casting the ready skill directly replaces the recurring attack,
            // just as selecting a skill in the client does.
            TraceDecision("replace-recurring-basic-with-skill", skill, includeSkillStatus: false);
            skill.Cast(selectedEntity.UniqueId);
            return;
        }

        if (Game.Player.InAction && SkillManager.IsCurrentActionBasic)
        {
            TraceDecision("cancel-basic-attack-for-skill", skill, includeSkillStatus: false);
            SkillManager.CancelAction();
            return;
        }

        ResetWaitTrace();
        skill.Cast(selectedEntity.UniqueId);
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
        ResetWaitTrace();
    }

    private void TraceWait(string reason, bool includeSkillStatus)
    {
        var targetId = Game.SelectedEntity?.UniqueId ?? 0;
        var now = Kernel.TickCount;

        if (_waitReason != reason || _waitTargetId != targetId)
        {
            _waitReason = reason;
            _waitTargetId = targetId;
            _waitStartedTick = now;
            _lastWaitLogTick = 0;
            return;
        }

        var elapsed = now - _waitStartedTick;
        if (
            elapsed < WAIT_LOG_THRESHOLD
            || (_lastWaitLogTick != 0 && now - _lastWaitLogTick < WAIT_LOG_INTERVAL)
        )
            return;

        _lastWaitLogTick = now;
        WriteCombatTrace("WAIT", reason, elapsed, null, includeSkillStatus);
    }

    private void TraceDecision(string reason, SkillInfo skill, bool includeSkillStatus)
    {
        WriteCombatTrace("DECISION", reason, 0, skill, includeSkillStatus);
        ResetWaitTrace();
    }

    private static void WriteCombatTrace(
        string kind,
        string reason,
        int elapsed,
        SkillInfo selectedSkill,
        bool includeSkillStatus
    )
    {
        var target = Game.SelectedEntity;
        var lastSkill = Game.Player.Skills.GetSkillInfoById(SkillManager.LastCastedSkillId);
        var lastSkillName = lastSkill?.Record?.GetRealName() ?? "unknown";
        var selected = selectedSkill == null
            ? "none"
            : $"{selectedSkill.Record.GetRealName()}({selectedSkill.Id})";
        var pending = SkillManager.PendingSkillId == 0
            ? "none"
            : $"{SkillManager.PendingSkillId}@{SkillManager.PendingSkillTargetId}:{SkillManager.PendingSkillElapsedMilliseconds}/{SkillManager.PendingSkillTimeoutMilliseconds}ms";
        var imbuePending = SkillManager.PendingImbueSkillId == 0
            ? "none"
            : SkillManager.PendingImbueSkillId.ToString();
        var retry = SkillManager.RetryCombatSkillId == 0
            ? "none"
            : $"{SkillManager.RetryCombatSkillId}@{SkillManager.RetryCombatTargetId}";
        var queued = SkillManager.QueuedCombatSkillId == 0
            ? "none"
            : $"{SkillManager.QueuedCombatSkillId}@{SkillManager.QueuedCombatTargetId}";
        var skills = includeSkillStatus ? $" skills=[{DescribeSkills()}]" : string.Empty;
        var wait = elapsed > 0 ? $" waited={elapsed}ms" : string.Empty;

        Log.Append(
            LogLevel.Debug,
            $"{kind} reason={reason}{wait} target={target?.UniqueId ?? 0} distance={target?.DistanceToPlayer:F1} "
                + $"inAction={Game.Player.InAction} lastSkill={lastSkillName}({SkillManager.LastCastedSkillId}) "
                + $"lastIsBasic={SkillManager.IsLastCastedBasic} currentAction={SkillManager.CurrentCombatAction} "
                + $"pending={pending} imbuePending={imbuePending} retry={retry} queued={queued} selected={selected} "
                + $"currentSkillRemaining={SkillManager.CurrentCombatSkillRemainingMilliseconds}ms "
                + $"playerState=life:{Game.Player.State.LifeState},body:{Game.Player.State.BodyState},motion:{Game.Player.State.MotionState},"
                + $"hit:{Game.Player.State.HitState},scroll:{Game.Player.State.ScrollState},vehicle:{Game.Player.HasActiveVehicle},badEffect:{Game.Player.BadEffect}"
                + skills,
            "CombatTrace"
        );
    }

    private static string DescribeSkills()
    {
        return string.Join(
            "; ",
            SkillManager
                .GetCurrentAttackSkills()
                .Select(skill =>
                    $"{skill.Record.GetRealName()}({skill.Id})={SkillManager.GetCastAvailability(skill)}"
                )
        );
    }

    private void ResetWaitTrace()
    {
        _waitReason = null;
        _waitTargetId = 0;
        _waitStartedTick = 0;
        _lastWaitLogTick = 0;
    }

    /// <summary>
    ///     Casts the teleportation skill if it's set up.
    /// </summary>
    /// <returns></returns>
    private bool CastTeleportation()
    {
        var selectedEntity = Game.SelectedEntity;
        if (SkillManager.TeleportSkill?.CanBeCasted != true || selectedEntity?.State.LifeState != LifeState.Alive)
            return false;

        var distanceToMonster = selectedEntity.DistanceToPlayer;
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
                SkillManager.TeleportSkill.CastAt(selectedEntity.Position);

                Log.Debug(
                    $"Used teleportation skill [{SkillManager.TeleportSkill.Record.GetRealName()}] (before: {distanceToMonster}m, after: {distanceAfterCasting}m, traveled: {availableDistance}m)"
                );

                return true;
            }
        }

        return false;
    }
}
