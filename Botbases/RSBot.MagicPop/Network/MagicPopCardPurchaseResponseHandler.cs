using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.MagicPop.Protocol;

namespace RSBot.MagicPop.Network;

internal sealed class MagicPopCardPurchaseResponseHandler : IPacketHandler
{
    public ushort Opcode => MagicPopCardPurchaseProtocol.ResponseOpcode;
    public PacketDestination Destination => PacketDestination.Client;

    public void Invoke(Packet packet)
    {
        if (!Bootstrap.IsActive || Container.References.CardPackage == null)
            return;

        var bytes = packet.GetBytes();
        if (bytes.Length < 2 || bytes[1] != 0x18)
            return;

        if (!MagicPopCardPurchaseProtocol.TryParseResponse(
                packet,
                Container.References.CardPackage,
                out var result,
                out var error))
        {
            EventManager.FireEvent("MagicPop.OnCardPurchaseError", error);
            return;
        }

        EventManager.FireEvent("MagicPop.OnCardPurchased", result);
    }
}
