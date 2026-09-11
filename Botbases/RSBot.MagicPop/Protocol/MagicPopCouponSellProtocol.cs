using System;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Inventory;

namespace RSBot.MagicPop.Protocol;

internal static class MagicPopCouponSellProtocol
{
    private const string LosingCouponCodeName = "ITEM_MALL_GACHA_CARD_LOSE";

    public const ushort RequestOpcode = 0x7034;
    public const ushort ResponseOpcode = 0xB034;

    public static Packet CreateRequest(InventoryItem coupon, uint npcUniqueId)
    {
        if (coupon?.Record == null
            || !string.Equals(coupon.Record.CodeName, LosingCouponCodeName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only a verified losing Magic POP coupon can be sold.", nameof(coupon));
        if (npcUniqueId == 0)
            throw new ArgumentOutOfRangeException(nameof(npcUniqueId));

        var packet = new Packet(RequestOpcode);
        packet.WriteByte(InventoryOperation.SP_SELL_ITEM);
        packet.WriteByte(coupon.Slot);
        packet.WriteUShort(coupon.Amount);
        packet.WriteUInt(npcUniqueId);
        return packet;
    }

    public static bool TryParseResponse(
        Packet packet,
        byte expectedSlot,
        ushort expectedAmount,
        uint expectedNpcUniqueId,
        out string error)
    {
        error = null;
        if (packet == null || packet.Opcode != ResponseOpcode)
        {
            error = "Unexpected coupon-sale response.";
            return false;
        }

        var copy = new Packet(packet);
        if (copy.Length < 9 || copy.ReadByte() != 1)
        {
            error = "Losing-coupon sale was rejected or truncated.";
            return false;
        }

        if (copy.ReadByte() != (byte)InventoryOperation.SP_SELL_ITEM)
        {
            error = "Response is not an inventory sale result.";
            return false;
        }

        var slot = copy.ReadByte();
        var amount = copy.ReadUShort();
        var npcUniqueId = copy.ReadUInt();
        if (slot != expectedSlot || amount != expectedAmount || npcUniqueId != expectedNpcUniqueId)
        {
            error = "Sale response does not match the in-flight losing coupon request.";
            return false;
        }

        return true;
    }
}
