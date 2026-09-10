using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Skill;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Components;

public enum CombatActionType
{
    None,
    Skill,
    BasicAttack,
    RecurringBasicAttack,
    Cancelling,
}

public static class SkillManager
{
    private const int CAST_REJECTION_BACKOFF = 750;
    private const int CAST_STALL_TIMEOUT = 1_500;
    private const float CAST_PROGRESS_DISTANCE = 0.5f;
    private const int CANCEL_SETTLE_INTERVAL = 600;
    private const int CANCEL_COLLISION_WINDOW = 2_000;
    private const int COMBAT_SKILL_QUEUE_LEAD_TIME = 300;

    /// <summary>
    ///     Get the skill using index
    /// </summary>
    private static int _lastIndex;

    /// <summary>
    ///     The last casted skill id
    /// </summary>
    public static uint LastCastedSkillId;

    private static volatile uint _pendingSkillId;
    private static volatile uint _pendingSkillTargetId;
    private static volatile int _pendingSkillTick;
    private static volatile int _pendingSkillTimeout;
    private static volatile int _pendingSkillLastProgressTick;
    private static volatile float _pendingSkillLastDistance;
    private static volatile bool _pendingSkillAccepted;
    private static volatile uint _pendingImbueSkillId;
    private static volatile int _pendingImbueTick;
    private static volatile int _pendingImbueTimeout;
    private static volatile bool _lastRequestWasImbue;
    private static volatile CombatActionType _currentCombatAction;
    private static volatile int _cancelSettleUntilTick;
    private static volatile int _lastCancelTick;
    private static volatile uint _retryCombatSkillId;
    private static volatile uint _retryCombatTargetId;
    private static volatile uint _queuedCombatSkillId;
    private static volatile uint _queuedCombatTargetId;
    private static volatile int _currentCombatSkillStartedTick;
    private static volatile int _currentCombatSkillDuration;

    /// <summary>
    ///     Basic skills
    /// </summary>
    private static IEnumerable<uint> _baseSkills;

    /// <summary>
    ///     Gets or sets the skills organized by their mob priority.
    /// </summary>
    /// <value>
    ///     The skills.
    /// </value>
    public static Dictionary<MonsterRarity, List<SkillInfo>> Skills { get; set; }

    /// <summary>
    ///     Gets or sets the resurrection skill.
    /// </summary>
    /// <value>
    ///     The resurrection skill.
    /// </value>
    public static SkillInfo ResurrectionSkill { get; set; }

    /// <summary>
    ///     Gets or sets the imbue skill.
    /// </summary>
    /// <value>
    ///     The imbue skill.
    /// </value>
    public static SkillInfo ImbueSkill { get; set; }

    /// <summary>
    ///     Gets or sets the buffs.
    /// </summary>
    /// <value>
    ///     The buffs.
    /// </value>
    public static List<SkillInfo> Buffs { get; set; }

    /// <summary>
    ///     Gets or sets the teleport skill.
    /// </summary>
    public static SkillInfo TeleportSkill { get; set; }

    /// <summary>
    ///     Gets the config to always use skills in order.
    /// </summary>
    public static bool UseSkillsInOrder => PlayerConfig.Get("RSBot.Skills.checkUseSkillsInOrder", false);

    /// <summary>
    ///     Is the last action basic skill (Auto attack) <c>true</c>; otherwise <c>false</c>
    /// </summary>
    public static bool IsLastCastedBasic => IsBasicSkill(LastCastedSkillId);

    /// <summary>
    ///     Gets the current combat action independently from overlay buffs such as imbue.
    /// </summary>
    public static CombatActionType CurrentCombatAction => _currentCombatAction;

    /// <summary>
    ///     Gets whether the current action can be interrupted for a configured combat skill.
    /// </summary>
    public static bool IsCurrentActionBasic =>
        _currentCombatAction is CombatActionType.BasicAttack or CombatActionType.RecurringBasicAttack
        || (_currentCombatAction == CombatActionType.None && IsLastCastedBasic);

    /// <summary>
    ///     Gets whether the supplied skill is one of the client's basic attacks.
    /// </summary>
    public static bool IsBasicSkill(uint skillId) => _baseSkills?.Contains(skillId) == true;

    /// <summary>
    ///     Gets the skill request currently waiting for a server response.
    /// </summary>
    public static uint PendingSkillId => _pendingSkillId;

    /// <summary>
    ///     Gets the entity targeted by the pending skill request, or zero for a non-targeted skill.
    /// </summary>
    public static uint PendingSkillTargetId => _pendingSkillTargetId;

    public static uint RetryCombatSkillId => _retryCombatSkillId;

    public static uint RetryCombatTargetId => _retryCombatTargetId;

    public static uint QueuedCombatSkillId => _queuedCombatSkillId;

    public static uint QueuedCombatTargetId => _queuedCombatTargetId;

    public static int CurrentCombatSkillRemainingMilliseconds =>
        _currentCombatSkillDuration <= 0
            ? int.MaxValue
            : Math.Max(
                0,
                _currentCombatSkillDuration
                    - (Kernel.TickCount - _currentCombatSkillStartedTick)
            );

    /// <summary>
    ///     Gets how long the current skill request has been pending.
    /// </summary>
    public static int PendingSkillElapsedMilliseconds =>
        _pendingSkillId == 0 ? 0 : Math.Max(0, Kernel.TickCount - _pendingSkillTick);

    /// <summary>
    ///     Gets the timeout assigned to the current skill request.
    /// </summary>
    public static int PendingSkillTimeoutMilliseconds => _pendingSkillTimeout;

    /// <summary>
    ///     Gets whether action-end packets from a cancelled basic attack are still settling.
    /// </summary>
    public static bool IsCancellationSettling
    {
        get
        {
            if (_currentCombatAction == CombatActionType.Cancelling)
                return true;

            var settleUntil = _cancelSettleUntilTick;
            if (settleUntil == 0)
                return false;

            if (unchecked(settleUntil - Kernel.TickCount) > 0)
                return true;

            _cancelSettleUntilTick = 0;
            return false;
        }
    }

    public static uint PendingImbueSkillId => IsImbuePending ? _pendingImbueSkillId : 0;

    public static bool IsImbuePending
    {
        get
        {
            if (_pendingImbueSkillId == 0)
                return false;

            if (Kernel.TickCount - _pendingImbueTick < _pendingImbueTimeout)
                return true;

            _pendingImbueSkillId = 0;
            return false;
        }
    }

    /// <summary>
    ///     Gets whether a skill request is waiting for the server to accept or reject it.
    /// </summary>
    public static bool CastPending
    {
        get
        {
            if (_pendingSkillId != 0)
            {
                var now = Kernel.TickCount;
                if (_pendingSkillAccepted)
                {
                    if (now - _pendingSkillTick < _pendingSkillTimeout)
                        return true;

                    ClearPendingSkill();
                }
                else if (!ValidatePendingTarget())
                {
                    ClearPendingSkill("target-changed");
                    ClearCombatRetry();
                }
                else if (_pendingSkillTargetId != 0 && HasPendingCastProgress(now))
                {
                    return true;
                }
                else if (
                    _pendingSkillTargetId != 0
                    && now - _pendingSkillLastProgressTick >= CAST_STALL_TIMEOUT
                )
                {
                    ClearPendingSkill("no-cast-progress", retryCombatSkill: true);
                }
                else if (now - _pendingSkillTick < _pendingSkillTimeout)
                {
                    return true;
                }
                else
                {
                    ClearPendingSkill("request-timeout", retryCombatSkill: true);
                }
            }

            return IsImbuePending;
        }
    }

    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    internal static void Initialize()
    {
        Skills = Enum.GetValues(typeof(MonsterRarity))
            .Cast<MonsterRarity>()
            .ToDictionary(v => v, v => new List<SkillInfo>());
        Buffs = new List<SkillInfo>();

        EventManager.SubscribeEvent("OnLoadGameData", OnLoadGamedData);
        EventManager.SubscribeEvent("OnCastSkill", new Action<uint>(OnCastSkill));
        EventManager.SubscribeEvent("OnLoadCharacter", ResetCombatState);
        EventManager.SubscribeEvent("OnAgentServerDisconnected", ResetCombatState);

        Log.Debug($"Initialized [SkillManager] for [{Skills.Count}] different mob rarities!");
    }

    private static void OnLoadGamedData()
    {
        _baseSkills = Game.ReferenceManager.GetBaseSkills();
    }

    private static void ResetCombatState()
    {
        LastCastedSkillId = 0;
        _pendingSkillId = 0;
        _pendingSkillTargetId = 0;
        _pendingSkillAccepted = false;
        _pendingImbueSkillId = 0;
        _lastRequestWasImbue = false;
        _currentCombatAction = CombatActionType.None;
        _cancelSettleUntilTick = 0;
        _lastCancelTick = 0;
        _currentCombatSkillStartedTick = 0;
        _currentCombatSkillDuration = 0;
        ClearCombatRetry();
        ClearQueuedCombatSkill();
    }

    /// <summary>
    ///     Call after casted skill
    /// </summary>
    /// <param name="skillId">The casted skill id</param>
    private static void OnCastSkill(uint skillId)
    {
        // Imbue is an overlay for the current combat action. It must not replace the last combat skill,
        // otherwise an active basic attack can no longer be recognized and interrupted for the next skill.
        if (ImbueSkill?.Id == skillId)
            return;

        LastCastedSkillId = skillId;
        if (IsBasicSkill(skillId))
        {
            _currentCombatAction = CombatActionType.BasicAttack;
            _currentCombatSkillStartedTick = 0;
            _currentCombatSkillDuration = 0;
            return;
        }

        _currentCombatAction = CombatActionType.Skill;
        var skill = Game.Player.Skills.GetSkillInfoById(skillId);
        _currentCombatSkillStartedTick = Kernel.TickCount;
        _currentCombatSkillDuration = GetTotalActionDuration(skill);
    }

    /// <summary>
    ///     Completes the pending request after the server accepted the cast.
    /// </summary>
    public static void CompleteCastRequest(uint skillId)
    {
        if (_pendingImbueSkillId == skillId)
        {
            // Keep the request guarded until the corresponding buff-add packet arrives.
            _pendingImbueTick = Kernel.TickCount;
            _pendingImbueTimeout = 1_500;
        }

        if (_pendingSkillId == skillId)
        {
            // Keep a short guard after acceptance so the action-state packet can arrive
            // before the next training tick considers another skill.
            _pendingSkillTick = Kernel.TickCount;
            _pendingSkillTimeout = 250;
            _pendingSkillAccepted = true;
        }

        if (_retryCombatSkillId == skillId)
            ClearCombatRetry();
    }

    /// <summary>
    ///     Releases and briefly backs off the last request after a server rejection.
    /// </summary>
    public static void RejectCastRequest()
    {
        uint rejectedSkillId;
        if (_lastRequestWasImbue && _pendingImbueSkillId != 0)
        {
            rejectedSkillId = _pendingImbueSkillId;
            _pendingImbueSkillId = 0;
        }
        else
        {
            rejectedSkillId = _pendingSkillId;
            ClearPendingSkill();
        }

        if (rejectedSkillId == 0)
            return;

        if (_retryCombatSkillId == rejectedSkillId)
            ClearCombatRetry();

        var skill = Game.Player?.Skills?.GetSkillInfoById(rejectedSkillId);
        skill ??= Buffs?.Find(candidate => candidate.Id == rejectedSkillId);
        skill ??= ImbueSkill?.Id == rejectedSkillId ? ImbueSkill : null;
        skill?.DeferRetry(CAST_REJECTION_BACKOFF);
    }

    private static void BeginCastRequest(
        SkillInfo skill,
        uint targetId = 0,
        double targetDistance = 0
    )
    {
        if (
            targetId != 0
            && _currentCombatAction == CombatActionType.RecurringBasicAttack
            && !IsCancellationSettling
        )
        {
            // This request intentionally replaces the server's recurring basic attack;
            // it is unrelated to any older cancel tail and must not be invalidated by it.
            _lastCancelTick = 0;
        }

        _lastRequestWasImbue = false;
        _pendingSkillId = skill.Id;
        _pendingSkillTargetId = targetId;
        _pendingSkillTick = Kernel.TickCount;
        _pendingSkillLastProgressTick = _pendingSkillTick;
        _pendingSkillLastDistance = (float)targetDistance;
        _pendingSkillAccepted = false;

        var movementDuration = 0;
        var range = skill.Record.Action_Range / 10d;
        if (targetDistance > range && Game.Player.ActualSpeed > 0)
            movementDuration = (int)((targetDistance - range) / Game.Player.ActualSpeed * 10_000d);

        _pendingSkillTimeout = Math.Clamp(
            skill.Record.Action_PreparingTime
                + skill.Record.Action_CastingTime
                + skill.Record.Action_ActionDuration
                + movementDuration
                + 1_000,
            1_000,
            15_000
        );
    }

    private static bool ValidatePendingTarget()
    {
        if (_pendingSkillTargetId == 0)
            return true;

        var target = Game.SelectedEntity;
        return target != null
            && target.UniqueId == _pendingSkillTargetId
            && target.State.LifeState == LifeState.Alive;
    }

    private static bool HasPendingCastProgress(int now)
    {
        if (_pendingSkillTargetId == 0)
            return false;

        var target = Game.SelectedEntity;
        if (target == null || target.UniqueId != _pendingSkillTargetId)
            return false;

        var distance = target.DistanceToPlayer;
        if (_pendingSkillLastDistance - distance < CAST_PROGRESS_DISTANCE)
            return false;

        _pendingSkillLastDistance = (float)distance;
        _pendingSkillLastProgressTick = now;
        return true;
    }

    private static void ClearPendingSkill(string reason = null, bool retryCombatSkill = false)
    {
        var skillId = _pendingSkillId;
        var targetId = _pendingSkillTargetId;
        var elapsed = skillId == 0 ? 0 : Math.Max(0, Kernel.TickCount - _pendingSkillTick);

        if (retryCombatSkill && skillId != 0 && targetId != 0)
        {
            _retryCombatSkillId = skillId;
            _retryCombatTargetId = targetId;
        }

        _pendingSkillId = 0;
        _pendingSkillTargetId = 0;
        _pendingSkillTick = 0;
        _pendingSkillTimeout = 0;
        _pendingSkillLastProgressTick = 0;
        _pendingSkillLastDistance = 0;
        _pendingSkillAccepted = false;

        if (reason != null && skillId != 0)
        {
            Log.Append(
                LogLevel.Debug,
                $"PENDING_CLEARED reason={reason} skill={skillId} target={targetId} elapsed={elapsed}ms",
                "CombatTrace"
            );
        }
    }

    private static void ClearCombatRetry()
    {
        _retryCombatSkillId = 0;
        _retryCombatTargetId = 0;
    }

    /// <summary>
    ///     Buffers the next attack skill while the current attack animation is still running.
    /// </summary>
    public static bool QueueNextCombatSkill(SkillInfo skill, uint targetId)
    {
        if (skill == null || targetId == 0)
            return false;

        if (_queuedCombatSkillId == skill.Id && _queuedCombatTargetId == targetId)
            return true;

        _queuedCombatSkillId = skill.Id;
        _queuedCombatTargetId = targetId;

        Log.Append(
            LogLevel.Debug,
            $"SKILL_QUEUED skill={skill.Record.GetRealName()}({skill.Id}) target={targetId}",
            "CombatTrace"
        );
        return true;
    }

    private static void ClearQueuedCombatSkill()
    {
        _queuedCombatSkillId = 0;
        _queuedCombatTargetId = 0;
    }

    private static bool TryDispatchQueuedCombatSkill(string reason)
    {
        var skillId = _queuedCombatSkillId;
        var targetId = _queuedCombatTargetId;
        if (skillId == 0 || targetId == 0)
            return false;

        var target = Game.SelectedEntity;
        var skill = target?.UniqueId == targetId
            ? GetCurrentAttackSkills().FirstOrDefault(candidate => candidate.Id == skillId)
            : null;

        ClearQueuedCombatSkill();

        if (
            target == null
            || target.UniqueId != targetId
            || target.State.LifeState != LifeState.Alive
            || skill == null
            || !skill.CanBeCasted
            || !CheckSkillRequired(skill.Record)
        )
            return false;

        Log.Append(
            LogLevel.Debug,
            $"SKILL_QUEUE_DISPATCH reason={reason} skill={skill.Record.GetRealName()}({skill.Id}) target={targetId}",
            "CombatTrace"
        );

        return CastSkill(skill, targetId);
    }

    /// <summary>
    ///     Sends a buffered attack shortly before the current animation finishes, so it
    ///     reaches the server before the automatic recurring basic attack is scheduled.
    /// </summary>
    public static bool TryDispatchQueuedCombatSkillEarly()
    {
        if (
            _currentCombatAction != CombatActionType.Skill
            || _queuedCombatSkillId == 0
            || CurrentCombatSkillRemainingMilliseconds > COMBAT_SKILL_QUEUE_LEAD_TIME
        )
            return false;

        return TryDispatchQueuedCombatSkill(
            $"action-ending remaining={CurrentCombatSkillRemainingMilliseconds}ms"
        );
    }

    private static int GetTotalActionDuration(SkillInfo skill)
    {
        var record = skill?.Record;
        var duration = 0;
        var visited = new HashSet<uint>();

        while (record != null && visited.Add(record.ID))
        {
            duration +=
                record.Action_PreparingTime
                + record.Action_CastingTime
                + record.Action_ActionDuration;
            record = record.Basic_ChainCode == 0
                ? null
                : Game.ReferenceManager.GetRefSkill(record.Basic_ChainCode);
        }

        return duration;
    }

    private static bool BeginImbueRequest(SkillInfo skill)
    {
        if (IsImbuePending)
            return false;

        _lastRequestWasImbue = true;
        _pendingImbueSkillId = skill.Id;
        _pendingImbueTick = Kernel.TickCount;
        _pendingImbueTimeout = Math.Clamp(
            skill.Record.Action_PreparingTime
                + skill.Record.Action_CastingTime
                + skill.Record.Action_ActionDuration
                + 2_000,
            2_000,
            5_000
        );
        return true;
    }

    public static void CompleteImbueRequest(uint skillId)
    {
        if (_pendingImbueSkillId == skillId)
            _pendingImbueSkillId = 0;
    }

    public static void UpdateActionState(byte state, byte recurring)
    {
        if (recurring == 0)
        {
            var now = Kernel.TickCount;
            if (
                _pendingSkillId != 0
                && !_pendingSkillAccepted
                && _pendingSkillTargetId != 0
                && _lastCancelTick != 0
                && now - _lastCancelTick < CANCEL_COLLISION_WINDOW
            )
            {
                ClearPendingSkill("cancel-tail-action-end", retryCombatSkill: true);
                _cancelSettleUntilTick = unchecked(now + CANCEL_SETTLE_INTERVAL);
            }

            if (_currentCombatAction == CombatActionType.Cancelling || IsCancellationSettling)
                _cancelSettleUntilTick = unchecked(now + CANCEL_SETTLE_INTERVAL);

            _currentCombatAction = CombatActionType.None;
            TryDispatchQueuedCombatSkill("action-ended");
            return;
        }

        if (state is 0x02 or 0x03)
        {
            _currentCombatAction = CombatActionType.RecurringBasicAttack;
            TryDispatchQueuedCombatSkill("recurring-basic-started");
            return;
        }

        if (_pendingSkillId != 0)
            _currentCombatAction = CombatActionType.Skill;
    }

    /// <summary>
    ///     Sets the skills.
    /// </summary>
    /// <param name="monsterRarity">The monster rarity.</param>
    /// <param name="skills">The skills.</param>
    public static void SetSkills(MonsterRarity monsterRarity, List<SkillInfo> skills)
    {
        if (Skills == null || !Skills.ContainsKey(monsterRarity))
            return;

        Skills[monsterRarity] = skills;
    }

    /// <summary>
    ///     Gets the next skill.
    /// </summary>
    /// <returns></returns>
    public static SkillInfo GetNextSkill()
    {
        var entity = Game.SelectedEntity;
        if (entity == null)
            return null;

        var rarity = MonsterRarity.General;

        if (entity is SpawnedMonster monster)
            if (Skills[monster.Rarity].Count > 0)
                rarity = monster.Rarity;

        if (_retryCombatSkillId != 0)
        {
            if (_retryCombatTargetId != entity.UniqueId)
            {
                ClearCombatRetry();
            }
            else
            {
                var retrySkill = Skills[rarity].Find(skill => skill.Id == _retryCombatSkillId);
                if (retrySkill == null)
                {
                    ClearCombatRetry();
                }
                else if (retrySkill.CanBeCasted)
                {
                    return retrySkill;
                }
            }
        }

        var distance = Game.Player.Movement.Source.DistanceTo(entity.Movement.Source);

        var minDifference = int.MaxValue;
        //var weaponRange = 0;
        var closestSkill = default(SkillInfo);

        if (entity.State.HitState == ActionHitStateFlag.KnockDown)
        {
            // try to get attack skill for only knockdown states
            closestSkill = Skills[rarity].Find(p => p.Record.Params.Contains(25697));
        }
        else if (UseSkillsInOrder || distance < 10)
        {
            var counter = -1;
            var skillCount = Skills[rarity].Count;
            while (skillCount > 0 && counter < skillCount)
            {
                counter++;
                _lastIndex++;

                if (_lastIndex > Skills[rarity].Count - 1)
                    _lastIndex = 0;

                if (!Skills.ContainsKey(rarity) || Skills[rarity].Count < _lastIndex)
                    continue;

                var selectedSkill = Skills[rarity][_lastIndex];
                if (!selectedSkill.CanBeCasted)
                    continue;

                closestSkill = selectedSkill;
                break;
            }

            //Debug.WriteLine($"while loop: {stopwatch.ElapsedMilliseconds}");
        }
        else
        {
            /*var weapon = Game.Player.Inventory.GetItemAt(6);
            if (weapon != null)
                weaponRange = weapon.Record.Range / 10;
            */

            for (var i = 0; i < Skills[rarity].Count; i++)
            {
                var s = Skills[rarity][i];
                if (!s.CanBeCasted)
                    continue;

                var difference = Math.Abs(
                    s.Record.Action_Range / 10 - distance /* + weaponRange*/
                );
                if (minDifference > difference)
                {
                    minDifference = (short)difference;
                    closestSkill = s;
                }
            }
            //Debug.WriteLine($"for loop: {stopwatch.ElapsedMilliseconds}");
        }

        return closestSkill;
    }

    /// <summary>
    ///     Gets the configured attack skills used for the currently selected target.
    /// </summary>
    public static IReadOnlyList<SkillInfo> GetCurrentAttackSkills()
    {
        var entity = Game.SelectedEntity;
        var rarity = MonsterRarity.General;

        if (entity is SpawnedMonster monster && Skills[monster.Rarity].Count > 0)
            rarity = monster.Rarity;

        return Skills[rarity];
    }

    /// <summary>
    ///     Describes why a configured attack skill cannot currently be used.
    /// </summary>
    public static string GetCastAvailability(SkillInfo skill)
    {
        if (skill == null)
            return "missing";

        var record = skill.Record;
        if (record == null)
            return "missing-reference";

        if (skill.RetryRemainingMilliseconds > 0)
            return $"retry-backoff:{skill.RetryRemainingMilliseconds}ms";

        if (skill.CooldownRemainingMilliseconds > 0)
            return $"cooldown:{skill.CooldownRemainingMilliseconds}ms";

        if (Game.Player.Mana < record.Consume_MP)
            return $"mana:{Game.Player.Mana}/{record.Consume_MP}";

        if (skill.CanNotBeCasted)
            return $"active-duration:{skill.RemainingMilliseconds}ms";

        if (!CheckSkillRequired(record))
            return "weapon-requirement";

        return "ready";
    }

    /// <summary>
    ///     Check required of the using skill
    /// </summary>
    /// <param name="skill">The using skill</param>
    public static bool CheckSkillRequired(RefSkill skill)
    {
        if (skill.ReqCommon_Mastery1 == 1)
            return true;

        var currentWeapon = Game.Player.Inventory.GetItemAt(6);
        if (skill.ReqCast_Weapon1 == WeaponType.Any)
        {
            var list = new List<TypeIdFilter>(8);

            for (var i = 0; i < skill.Params.Count; i++)
            {
                var param = skill.Params[i];
                if (param != 1919250793)
                    continue;

                var paramTypeId3 = (byte)skill.Params[++i];
                var paramTypeId4 = (byte)skill.Params[++i];
                list.Add(new TypeIdFilter(3, 1, paramTypeId3, paramTypeId4));
            }

            if (list.Count == 0)
                return true;

            return list.Any(requirement =>
            {
                var equippedSlot = (byte)(requirement.TypeID3 == 6 ? 6 : 7);
                var equippedItem = Game.Player.Inventory.GetItemAt(equippedSlot);

                return equippedItem != null && requirement.EqualsRefItem(equippedItem.Record);
            });
        }

        return currentWeapon != null
            && currentWeapon.Record.TypeID2 == 1
            && currentWeapon.Record.TypeID3 == 6
            && (
                currentWeapon.Record.TypeID4 == (byte)skill.ReqCast_Weapon1
                || (
                    (byte)skill.ReqCast_Weapon2 != 0xFF
                    && currentWeapon.Record.TypeID4 == (byte)skill.ReqCast_Weapon2
                )
            );
    }

    public static bool CastSkill(SkillInfo skill, uint targetId = 0)
    {
        if (!Game.Player.Skills.HasSkill(skill.Id))
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedBionic>(targetId, out var entity))
            return false;

        if (entity.State.LifeState == LifeState.Dead)
            return false;

        if (!CheckSkillRequired(skill.Record))
            return false;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Cast); //Use Skill
        packet.WriteUInt(skill.Id);
        packet.WriteByte(ActionTarget.Entity);

        // unknown byte
        if (Game.ClientType < GameClientType.Thailand)
            packet.WriteByte(1);

        packet.WriteUInt(targetId);

        Log.Debug(
            $"Skill Attacking to: {targetId} State: {entity.State.LifeState} Health: {entity.Health} HasHealth: {entity.HasHealth} Dst: {Math.Round(entity.DistanceToPlayer, 1)}"
        );

        BeginCastRequest(skill, targetId, entity.DistanceToPlayer);
        PacketManager.SendPacket(packet, PacketDestination.Server);

        return true;
    }

    /// <summary>
    ///     Cast player skill
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="targetId">The target unique identifier.</param>
    /// <returns> <c>true</c> if this successfully used the selected skill; otherwise, <c>false</c>.</returns>
    public static bool CastSkillOld(SkillInfo skill, uint targetId = 0)
    {
        if (!Game.Player.Skills.HasSkill(skill.Id))
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedBionic>(targetId, out var entity))
            return false;

        if (entity.State.LifeState == LifeState.Dead)
            return false;

        var weapon = Game.Player.Inventory.GetItemAt(6);

        if (!CheckSkillRequired(skill.Record))
            return false;

        var distance = entity.DistanceToPlayer;
        var speed = Game.Player.ActualSpeed;
        var movingSleep = 0d;

        // tel3 warrior sprint teleport
        var tel3Index = skill.Record.Params.FindIndex(p => p == 1952803891);
        if (tel3Index != -1)
        {
            var tel3speed = skill.Record.Params[++tel3Index];
            var tel3meter = skill.Record.Params[++tel3Index] / 10;

            if (distance < tel3meter)
                movingSleep = distance / tel3speed;
            else
                movingSleep = (distance - tel3meter) / speed + tel3meter / tel3speed;
        }
        else
        {
            var range = skill.Record.Action_Range / 10;
            if (distance - 3 > range)
                movingSleep = (distance - range) / speed;
        }

        if (movingSleep < 0)
            movingSleep = 0;
        else
            movingSleep *= 10000.0;

        var duration = (int)movingSleep; /* +
                     skill.Record.Action_CastingTime; +
                     skill.Record.Action_ActionDuration +
                     skill.Record.Action_PreparingTime;*/

        var packet = new Packet(0x7074);
        packet.WriteByte(1); //Execute
        packet.WriteByte(4); //Use Skill
        packet.WriteUInt(skill.Id);
        packet.WriteByte(ActionTarget.Entity);
        packet.WriteUInt(targetId);

        var callback = new AwaitCallback(
            response =>
                response.ReadByte() == 0x02 && response.ReadByte() == 0x00
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed,
            0xB074
        );

        var altSkill = skill.Record;
        while (altSkill != null)
        {
            duration += altSkill.Action_CastingTime + altSkill.Action_ActionDuration + altSkill.Action_PreparingTime;

            if (altSkill.Basic_ChainCode != 0)
                altSkill = Game.ReferenceManager.GetRefSkill(altSkill.Basic_ChainCode);
            else
                break;
        }

        if (duration < 100)
            duration = 1000;

        Log.Debug(
            $"Skill Attacking to: {targetId} State: {entity.State.LifeState} Health: {entity.Health} HasHealth: {entity.HasHealth} Dst: {Math.Round(entity.DistanceToPlayer, 1)}"
        );

        PacketManager.SendPacket(packet, PacketDestination.Server, callback);
        Thread.Sleep(duration);

        if (skill.Record.Basic_Activity != 1)
        {
            callback.AwaitResponse(duration);
            return callback.IsCompleted;
        }

        return true;
    }

    /// <summary>
    ///     Casts the buff skill.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    public static void CastBuff(SkillInfo skill, uint target = 0, bool awaitBuffResponse = true)
    {
        if (skill == null || skill.Id == 0)
            return;

        /*
        if (!Game.Player.Skills.HasSkill(skill.Id))
            return;
        */
        if (!CheckSkillRequired(skill.Record))
            return;

        var isImbue = ImbueSkill?.Id == skill.Id;
        if (isImbue && !BeginImbueRequest(skill))
            return;

        Log.Notify($"Casting skill (self-buff) [{skill.Record.GetRealName()}]");

        var packet = new Packet(0x7074);
        packet.WriteByte(1); //Execute
        packet.WriteByte(4); //Use Skill
        packet.WriteUInt(skill.Id);

        if (skill.Record.TargetGroup_Self || skill.Record.TargetGroup_Party)
        {
            packet.WriteByte(ActionTarget.Entity);
            packet.WriteUInt(target == 0 ? Game.Player.UniqueId : target);
        }
        else
        {
            packet.WriteByte(ActionTarget.None);
        }

        var asyncCallback = new AwaitCallback(
            response =>
            {
                var targetId = response.ReadUInt();
                var castedSkillId = response.ReadUInt();

                if (targetId == (target == 0 ? Game.Player.UniqueId : target) && castedSkillId == skill.Id)
                    return AwaitCallbackResult.Success;

                return AwaitCallbackResult.ConditionFailed;
            },
            0xB0BD
        );

        var callback = new AwaitCallback(
            response =>
            {
                return response.ReadByte() == 0x02 && response.ReadByte() == 0x00
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed;
            },
            0xB074
        );

        if (!isImbue)
            BeginCastRequest(skill);
        PacketManager.SendPacket(packet, PacketDestination.Server, asyncCallback, callback);

        if (awaitBuffResponse)
            asyncCallback.AwaitResponse(
                skill.Record.Action_CastingTime
                    + skill.Record.Action_ActionDuration
                    + skill.Record.Action_PreparingTime
                    + 1500
            );

        if (skill.Record.Basic_Activity != 1 && awaitBuffResponse)
            callback.AwaitResponse();
    }

    /// <summary>
    ///     Casts a skill to the given target position
    /// </summary>
    /// <param name="skill"></param>
    /// <param name="target"></param>
    public static void CastSkillAt(SkillInfo skill, Position target)
    {
        if (target.Region == 0 || target.DistanceToPlayer() > 100)
            return;

        if (skill.Id == 0)
            return;

        if (!CheckSkillRequired(skill.Record))
            return;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Cast); //Use Skill
        packet.WriteUInt(skill.Id);
        packet.WriteByte(ActionTarget.Area);
        packet.WriteUShort(target.Region);

        if (target.Region.IsDungeon)
        {
            packet.WriteShort(target.XOffset);
            packet.WriteShort(target.YOffset);
            packet.WriteShort(target.ZOffset);
        }
        else
        {
            packet.WriteInt(target.XOffset);
            packet.WriteInt(target.ZOffset);
            packet.WriteInt(target.YOffset);
        }

        var callback = new AwaitCallback(
            response =>
            {
                return response.ReadByte() == 0x02 && response.ReadByte() == 0x00
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed;
            },
            0xB074
        );

        BeginCastRequest(skill);
        PacketManager.SendPacket(packet, PacketDestination.Server, callback);

        if (skill.Record.Basic_Activity != 1)
            callback.AwaitResponse(1000);
    }

    /// <summary>
    ///     Casts the skill. Does not check any weapon requirement.
    /// </summary>
    /// <param name="skill"></param>
    /// <param name="targetId"></param>
    /// <returns></returns>
    public static bool CastAutoAttack()
    {
        var entity = Game.SelectedEntity;
        if (entity == null)
            return false;

        if (entity.State.LifeState == LifeState.Dead)
            return false;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Attack); //Use Skill
        packet.WriteByte(ActionTarget.Entity);

        // unknown byte
        if (Game.ClientType < GameClientType.Thailand)
            packet.WriteByte(1);

        packet.WriteUInt(entity.UniqueId);

        Log.Debug(
            $"Normal Attacking to: {entity.UniqueId} State: {entity.State.LifeState} Health: {entity.Health} HasHealth: {entity.HasHealth} Dst: {Math.Round(entity.DistanceToPlayer, 1)}"
        );

        _currentCombatAction = CombatActionType.BasicAttack;
        PacketManager.SendPacket(packet, PacketDestination.Server);

        return true;
    }

    /// <summary>
    ///     Casts the skill at.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="position">The position.</param>
    public static void CastSkillAt(uint skillId, Position position)
    {
        if (!Game.Player.Skills.HasSkill(skillId))
            return;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Cast); //Use Skill
        packet.WriteUInt(skillId);
        packet.WriteByte(ActionTarget.Area);
        position.Region.Serialize(packet);
        packet.WriteFloat(position.XOffset);
        packet.WriteFloat(position.ZOffset);
        packet.WriteFloat(position.YOffset);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Cancels the buff.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    public static void CancelBuff(uint skillId)
    {
        if (!Game.Player.Skills.HasSkill(skillId))
            return;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Dispel); //Cancel Buff
        packet.WriteUInt(skillId);
        packet.WriteByte(ActionTarget.None);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Cancels the action.
    /// </summary>
    /// <returns></returns>
    public static bool CancelAction()
    {
        var startedTick = Kernel.TickCount;
        var previousAction = _currentCombatAction;
        _currentCombatAction = CombatActionType.Cancelling;
        _lastCancelTick = startedTick;
        _cancelSettleUntilTick = unchecked(startedTick + CANCEL_SETTLE_INTERVAL);
        var packet = new Packet(0x7074);
        packet.WriteByte(0x02); //Cancel

        var callback = new AwaitCallback(
            response =>
            {
                return response.ReadByte() == 0x02 && response.ReadByte() == 0x00
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed;
            },
            0xB074
        );

        PacketManager.SendPacket(packet, PacketDestination.Server, callback);
        callback.AwaitResponse();

        if (!callback.IsCompleted && _currentCombatAction == CombatActionType.Cancelling)
        {
            _currentCombatAction = previousAction;
            _cancelSettleUntilTick = 0;
        }
        else if (callback.IsCompleted)
        {
            _cancelSettleUntilTick = unchecked(Kernel.TickCount + CANCEL_SETTLE_INTERVAL);
        }

        Log.Append(
            LogLevel.Debug,
            $"CANCEL_RESULT completed={callback.IsCompleted} elapsed={Kernel.TickCount - startedTick}ms "
                + $"inAction={Game.Player.InAction} lastSkill={LastCastedSkillId} lastIsBasic={IsLastCastedBasic}",
            "CombatTrace"
        );

        return callback.IsCompleted;
    }
}
