using RSBot.Core.Event;

namespace RSBot.Core.Network.Handler.Agent.Action;

internal class ActionCommandStateResponse : IPacketHandler
{
    /// <summary>
    ///     Invokes the specified packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Invoke(Packet packet)
    {
        var state = packet.ReadByte();
        var recurring = packet.ReadByte();
        var wasInAction = Game.Player.InAction;
        RSBot.Core.Components.SkillManager.UpdateActionState(state, recurring);
        if (recurring == 0)
        {
            Game.Player.InAction = false;
            EventManager.FireEvent("OnPlayerExitAction");
        }
        else
        {
            Game.Player.InAction = true;
            EventManager.FireEvent("OnPlayerInAction");
        }

        Log.Append(
            LogLevel.Debug,
            $"ACTION_STATE client={Game.ClientType} state=0x{state:X2} recurring=0x{recurring:X2} "
                + $"inAction={wasInAction}->{Game.Player.InAction} lastSkill={RSBot.Core.Components.SkillManager.LastCastedSkillId} "
                + $"lastIsBasic={RSBot.Core.Components.SkillManager.IsLastCastedBasic} "
                + $"currentAction={RSBot.Core.Components.SkillManager.CurrentCombatAction} "
                + $"pending={RSBot.Core.Components.SkillManager.PendingSkillId} "
                + $"pendingTarget={RSBot.Core.Components.SkillManager.PendingSkillTargetId} "
                + $"retry={RSBot.Core.Components.SkillManager.RetryCombatSkillId}@{RSBot.Core.Components.SkillManager.RetryCombatTargetId} "
                + $"queued={RSBot.Core.Components.SkillManager.QueuedCombatSkillId}@{RSBot.Core.Components.SkillManager.QueuedCombatTargetId} "
                + $"cancelSettling={RSBot.Core.Components.SkillManager.IsCancellationSettling} "
                + $"imbuePending={RSBot.Core.Components.SkillManager.PendingImbueSkillId}",
            "CombatTrace"
        );
        /*
        switch (state)
        {
            case 0x01:
                Game.Player.InAction = true;
                EventManager.FireEvent("OnPlayerInAction");
                break;

            case 0x02:
                Game.Player.InAction = recurring != 0;

                EventManager.FireEvent("OnPlayerExitAction");
                break;
        }*/
    }

    #region Properites

    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0xB074;

    /// <summary>
    ///     Gets or sets the destination.
    /// </summary>
    /// <value>
    ///     The destination.
    /// </value>
    public PacketDestination Destination => PacketDestination.Client;

    #endregion Properites
}
