using System;
using RSBot.Core.Network;
using RSBot.Core.Objects.Inventory;
using RSBot.MagicPop.References;

namespace RSBot.MagicPop.Protocol;

internal static class MagicPopCardPurchaseProtocol
{
    public const ushort RequestOpcode = 0x7034;
    public const ushort ResponseOpcode = 0xB034;

    public static Packet CreateRequest(MagicPopCardPackage cardPackage, uint quantity)
    {
        if (cardPackage == null)
            throw new ArgumentNullException(nameof(cardPackage));
        if (quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var packet = new Packet(RequestOpcode);
        packet.WriteByte(InventoryOperation.SP_BUY_CASH_ITEM);
        packet.WriteInt(cardPackage.EncodedShopTabId);
        packet.WriteByte(cardPackage.ShopGood.SlotIndex);
        packet.WriteString(cardPackage.Package.RefPackageItemCodeName);
        packet.WriteUInt(quantity);
        packet.WriteUInt(0);
        packet.WriteUShort(0);
        packet.WriteInt(cardPackage.PackageId);
        return packet;
    }

    public static bool TryParseResponse(
        Packet packet,
        MagicPopCardPackage expectedPackage,
        out MagicPopCardPurchaseResult result,
        out string error)
    {
        result = null;
        error = null;
        if (packet == null || packet.Opcode != ResponseOpcode || expectedPackage == null)
        {
            error = "Unexpected cash-item purchase response.";
            return false;
        }

        var copy = new Packet(packet);
        if (copy.Length < 2)
        {
            error = "Cash-item purchase response is truncated.";
            return false;
        }

        if (copy.ReadByte() != 1)
        {
            error = "Cash-item purchase was rejected by the server.";
            return false;
        }

        if (copy.ReadByte() != (byte)InventoryOperation.SP_BUY_CASH_ITEM || copy.Remaining < 6)
        {
            error = "Response is not a Magic POP cash-item purchase result.";
            return false;
        }

        var encodedTabId = copy.ReadInt();
        var slotIndex = copy.ReadByte();
        if (encodedTabId != expectedPackage.EncodedShopTabId || slotIndex != expectedPackage.ShopGood.SlotIndex)
        {
            error = "Cash-item response belongs to a different shop tab or slot.";
            return false;
        }

        var itemCount = copy.ReadByte();
        if (itemCount == 0 || copy.Remaining < itemCount + sizeof(ushort))
        {
            error = "Cash-item response has an invalid destination-slot list.";
            return false;
        }

        var destinationSlots = copy.ReadBytes(itemCount);
        var quantity = copy.ReadUShort();
        if (quantity == 0)
        {
            error = "Cash-item response returned zero quantity.";
            return false;
        }

        result = new MagicPopCardPurchaseResult(destinationSlots, quantity);
        return true;
    }
}
