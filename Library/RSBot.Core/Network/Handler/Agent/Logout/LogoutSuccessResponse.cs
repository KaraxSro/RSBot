using RSBot.Core.Event;

namespace RSBot.Core.Network.Handler.Agent.Logout;

internal class LogoutSuccessResponse : IPacketHandler
{
    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0x300A;

    /// <summary>
    ///     Gets or sets the destination.
    /// </summary>
    /// <value>
    ///     The destination.
    /// </value>
    public PacketDestination Destination => PacketDestination.Client;

    /// <summary>
    ///     Handles the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Invoke(Packet packet)
    {
        Log.Notify("The player has left the game!");
        // During an RSBot-initiated exit the response has only just been queued for the
        // game client. Keep the proxy alive so it can receive the logout completion and
        // close normally; the main shutdown flow owns the timeout and forced fallback.
        if (!global::RSBot.Core.Components.ClientManager.IsIntentionalExit)
            Kernel.Proxy?.Shutdown();
        EventManager.FireEvent("OnLogout");
    }
}
