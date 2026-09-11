using System;
using RSBot.Core.Network;

namespace RSBot.MagicPop.Protocol;

internal static class MagicPopPlayProtocol
{
    public const ushort RequestOpcode = 0x7118;
    public const ushort ResponseOpcode = 0xB118;
    public const int RequestLength = 9;
    public const int ResponseLength = 2;

    public static Packet CreateRequest(uint npcUniqueId, uint gachaId, byte cardSlot)
    {
        if (npcUniqueId == 0)
            throw new ArgumentOutOfRangeException(nameof(npcUniqueId));
        if (gachaId == 0)
            throw new ArgumentOutOfRangeException(nameof(gachaId));

        var packet = new Packet(RequestOpcode);
        packet.WriteUInt(npcUniqueId);
        packet.WriteUInt(gachaId);
        packet.WriteByte(cardSlot);
        return packet;
    }

    public static bool TryParseResponse(Packet packet, out MagicPopPlayResult result, out string error)
    {
        result = null;
        error = null;

        if (packet == null)
        {
            error = "Response packet is null.";
            return false;
        }

        if (packet.Opcode != ResponseOpcode)
        {
            error = $"Unexpected opcode {packet.HexCode}.";
            return false;
        }

        var copy = new Packet(packet);
        if (copy.Length != ResponseLength)
        {
            error = $"Expected a {ResponseLength}-byte response, found {copy.Length} bytes.";
            return false;
        }

        var protocolResult = copy.ReadByte();
        var outcome = copy.ReadByte();
        if (protocolResult != 1)
        {
            error = $"Magic POP server rejected the request with result 0x{protocolResult:X2}.";
            return false;
        }

        if (outcome > 1)
        {
            error = $"Unknown Magic POP outcome 0x{outcome:X2}.";
            return false;
        }

        result = new MagicPopPlayResult(outcome == 1 ? MagicPopOutcome.Win : MagicPopOutcome.Lose);
        return true;
    }
}
