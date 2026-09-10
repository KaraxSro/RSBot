using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;
using System;
using System.Diagnostics;

namespace RSBot.Core.Network.Handler.Agent.Action;

internal class ActionSkillCastResponse : IPacketHandler
{
    private const int CooldownLogInterval = 5_000;
    private static int _lastCooldownLogTick = -CooldownLogInterval;

    /// <summary>
    ///     Gets or sets the destination.
    /// </summary>
    /// <value>
    ///     The destination.
    /// </value>
    public PacketDestination Destination => PacketDestination.Client;

    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0xB070;

    /// <summary>
    ///     Invokes the specified packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Invoke(Packet packet)
    {
        var rawPacket = Convert.ToHexString(packet.GetBytes());
        var result = packet.ReadByte();
        if (result != 0x01)
        {
            var errorCode = packet.ReadByte();
            var pendingSkillId = SkillManager.PendingSkillId;

            Log.Append(
                LogLevel.Debug,
                $"CAST_REJECTED client={Game.ClientType} result=0x{result:X2} error=0x{errorCode:X2} "
                    + $"pending={pendingSkillId} imbuePending={SkillManager.PendingImbueSkillId} raw={rawPacket}",
                "CombatTrace"
            );

            SkillManager.RejectCastRequest();

            switch (errorCode)
            {
                case 0x0C:
                    // Same other skills are already running
                    break;

                case 0x0E:
                    Game.Player.EquipAmmunition();
                    break;

                case 0x05:
                    if (Kernel.TickCount - _lastCooldownLogTick >= CooldownLogInterval)
                    {
                        _lastCooldownLogTick = Kernel.TickCount;
                        Log.Debug("Skill cooldown error. Still have time!");
                    }
                    break;

                case 0x06: // invalid target
                    break;

                case 0x10: // obstacle
                    EventManager.FireEvent("OnTargetBehindObstacle");
                    break;

                default:
                    Log.Error($"Invalid skill error code: 0x{errorCode:X2}");
                    break;
            }

            return;
        }

        var actionCode = packet.ReadByte();

        if (Game.ClientType > GameClientType.Thailand)
            packet.ReadByte(); // always 0x30

        var action = new Objects.Action
        {
            Code = actionCode,
            SkillId = packet.ReadUInt(),
            ExecutorId = packet.ReadUInt(),
            Id = packet.ReadUInt(),
        };

        if (Game.ClientType > GameClientType.Chinese && Game.ClientType != GameClientType.Japanese)
            action.UnknownId = packet.ReadUInt();

        action.TargetId = packet.ReadUInt();
        if (
            Game.ClientType == GameClientType.Turkey
            || Game.ClientType == GameClientType.Global
            || Game.ClientType == GameClientType.VTC_Game
            || Game.ClientType == GameClientType.RuSro
            || Game.ClientType == GameClientType.Korean
            || Game.ClientType == GameClientType.Japanese
            || Game.ClientType == GameClientType.Taiwan
        )
        {
            packet.ReadByte();
            action.Flag = (ActionStateFlag)packet.ReadByte();
        }
        else if (Game.ClientType == GameClientType.Rigid)
        {
            action.Flag = (ActionStateFlag)packet.ReadByte();
            var flag = packet.ReadByte();
            Debug.WriteLine("Flag:" + flag);
        }
        else
        {
            action.Flag = (ActionStateFlag)packet.ReadByte();
        }

        var knownPlayerSkill = Game.Player.Skills.GetSkillInfoById(action.SkillId);
        knownPlayerSkill ??= SkillManager.Buffs.Find(candidate => candidate.Id == action.SkillId);
        var skillIsBasic = SkillManager.IsBasicSkill(action.SkillId);
        if (action.PlayerIsExecutor || knownPlayerSkill != null || skillIsBasic)
        {
            var skillName = knownPlayerSkill?.Record?.GetRealName() ?? "unknown";
            Log.Append(
                LogLevel.Debug,
                $"CAST_ACCEPTED client={Game.ClientType} code=0x{action.Code:X2} "
                    + $"skill={skillName}({action.SkillId}) skillIsBasic={skillIsBasic} "
                    + $"executor={action.ExecutorId} expectedPlayer={Game.Player.UniqueId} playerExecutor={action.PlayerIsExecutor} "
                    + $"action={action.Id} target={action.TargetId} flag={action.Flag} pending={SkillManager.PendingSkillId} raw={rawPacket}",
                "CombatTrace"
            );
        }

        /*if (Game.ClientType >= GameClientType.Chinese)
            packet.ReadByte();

        action.Flag = (ActionStateFlag)packet.ReadByte();*/
        if (action.PlayerIsExecutor)
        {
            var skillInfo = Game.Player.Skills.GetSkillInfoById(action.SkillId);
            if (skillInfo == null)
                skillInfo = SkillManager.Buffs.Find(p => p.Id == action.SkillId);

            skillInfo?.Update();
            SkillManager.CompleteCastRequest(action.SkillId);

            EventManager.FireEvent("OnCastSkill", action.SkillId);
        }

        action.ReadPacket(packet);

        if (action.PlayerIsExecutor)
            return;

        if (!action.TryGetExecutor<SpawnedBionic>(out var executor))
            return;

        executor.TargetId = action.TargetId;
        //executor.StopMoving();

        if (!action.PlayerIsTarget)
            return;

        EventManager.FireEvent("OnEnemySkillOnPlayer");

        executor.StartAttackingTimer();
    }
}
