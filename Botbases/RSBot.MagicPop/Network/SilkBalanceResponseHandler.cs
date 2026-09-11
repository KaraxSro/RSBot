using RSBot.Core;
using RSBot.Core.Network;
using RSBot.MagicPop.Protocol;

namespace RSBot.MagicPop.Network;

internal sealed class SilkBalanceResponseHandler : IPacketHandler
{
    public ushort Opcode => SilkBalanceProtocol.Opcode;
    public PacketDestination Destination => PacketDestination.Client;

    public void Invoke(Packet packet)
    {
        if (SilkBalanceProtocol.TryParse(packet, out var balance, out var error))
        {
            Container.SilkBalance.Update(balance);
            return;
        }

        if (Bootstrap.IsActive)
            Log.Error($"[Magic POP] Failed to parse Silk balance: {error}");
    }
}
