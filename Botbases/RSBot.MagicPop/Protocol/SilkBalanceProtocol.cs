using RSBot.Core.Network;

namespace RSBot.MagicPop.Protocol;

internal static class SilkBalanceProtocol
{
    public const ushort Opcode = 0x3153;
    public const int PayloadLength = 12;

    public static bool TryParse(Packet packet, out SilkBalance balance, out string error)
    {
        balance = null;
        error = null;

        if (packet == null || packet.Opcode != Opcode)
        {
            error = "Unexpected Silk balance packet.";
            return false;
        }

        var copy = new Packet(packet);
        if (copy.Length != PayloadLength)
        {
            error = $"Expected a {PayloadLength}-byte Silk balance payload, found {copy.Length} bytes.";
            return false;
        }

        balance = new SilkBalance(copy.ReadUInt(), copy.ReadUInt(), copy.ReadUInt());
        return true;
    }
}
