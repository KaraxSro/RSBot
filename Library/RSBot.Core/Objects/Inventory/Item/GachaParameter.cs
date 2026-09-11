using RSBot.Core.Network;

namespace RSBot.Core.Objects.Item;

public sealed class GachaParameter
{
    public uint Id { get; private set; }
    public uint Value { get; private set; }

    internal static GachaParameter FromPacket(Packet packet)
    {
        return new GachaParameter
        {
            Id = packet.ReadUInt(),
            Value = packet.ReadUInt()
        };
    }
}
