using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.MagicPop.Protocol;

namespace RSBot.MagicPop.Network;

internal sealed class MagicPopPlayResponseHandler : IPacketHandler
{
    public ushort Opcode => MagicPopPlayProtocol.ResponseOpcode;
    public PacketDestination Destination => PacketDestination.Client;

    public void Invoke(Packet packet)
    {
        if (!Bootstrap.IsActive)
            return;

        if (!MagicPopPlayProtocol.TryParseResponse(packet, out var result, out var error))
        {
            Log.Error($"[Magic POP] Invalid play response: {error}");
            EventManager.FireEvent("MagicPop.OnPlayProtocolError", error);
            return;
        }

        EventManager.FireEvent("MagicPop.OnPlayResult", result);
    }
}
