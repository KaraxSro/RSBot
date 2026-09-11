using System;

namespace RSBot.MagicPop.Model;

internal sealed class MagicPopOperation
{
    public MagicPopOperation(uint npcUniqueId, MagicPopTarget target, byte cardSlot, DateTime startedUtc)
    {
        NpcUniqueId = npcUniqueId;
        Target = target;
        CardSlot = cardSlot;
        StartedUtc = startedUtc;
    }

    public uint NpcUniqueId { get; }
    public MagicPopTarget Target { get; }
    public byte CardSlot { get; }
    public DateTime StartedUtc { get; }
}
