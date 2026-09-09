using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Network;
using RSBot.General.Components;
using View = RSBot.General.Views.View;

namespace RSBot.General.PacketHandler;

internal class GatewayLoginResponse : IPacketHandler
{
    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0xA102;

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
        if (packet.ReadByte() == 0x01)
        {
            AutoLogin.RecordState("Gateway login accepted", LogLevel.Notify);
            Log.NotifyLang("AuthGetewaySuccess");
            AutoLogin.Pending = false;
            View.PendingWindow?.Hide();
            View.PendingWindow?.StopClientlessQueueTask();

            if (Game.ClientType == GameClientType.Japanese)
            {
                packet.ReadUInt(); //Token
                packet.ReadString(); //IP
                packet.ReadUShort(); //Port
                GlobalConfig.Set("RSBot.JSRO.login", packet.ReadString()); //Login
                packet.ReadByte(); //Channel
            }

            var selectedAccount = Accounts.SavedAccounts?.Find(p =>
                p.Username == GlobalConfig.Get<string>("RSBot.General.AutoLoginAccountUsername")
            );

            if (Game.ClientType == GameClientType.Global && selectedAccount?.Channel == 0x02)
            {
                packet.ReadUInt(); //Token
                packet.ReadString(); //IP
                packet.ReadUShort(); //Port
                packet.ReadByte(); //Channel
                GlobalConfig.Set("RSBot.JCPlanet.login", packet.ReadString()); //Login
            }

            return;
        }

        var code = packet.ReadByte();
        AutoLogin.RecordState($"Gateway login rejected; code={code}", LogLevel.Warning);

        switch (code)
        {
            case 1:
                Log.NotifyLang("AuthGatewayWrongIdPw");
                break;

            case 2:
                Log.NotifyLang("AuthAccountBanned");
                break;

            case 3:
                Log.NotifyLang("AuthAccountAlreadyInGame");
                break;

            case 4:
                Log.WarnLang("ServerCheck");
                AutoLogin.Handle("server inspection response");
                break;

            case 28: // isro block
            case 29: // ksro block
            case 5:
                Log.WarnLang("ServerFull");
                _ = AutoLogin.RetryAfterAsync(1000, "server full response");

                break;

            case 15:
                Log.WarnLang("APIServerError");
                break;

            case 26: // queue

                var count = packet.ReadUShort();
                var timestamp = packet.ReadInt();
                AutoLogin.RecordState($"Entered gateway queue; position={count}; estimatedWaitMs={timestamp}", LogLevel.Notify);

                Task.Run(() =>
                {
                    var main = Application.OpenForms
                            .OfType<Form>()
                            .FirstOrDefault();

                    main?.BeginInvoke(() =>
                    {
                        View.PendingWindow.Start(count, timestamp);
                        if (!GlobalConfig.Get<bool>("RSBot.General.AutoHidePendingWindow"))
                            View.PendingWindow.ShowAtTop(View.Instance);
                    });
                });

                break;

            case 43:
                Log.WarnLang("TooManyAttempts");
                break;

            default:
                Log.WarnLang("AuthFailed", code);
                break;
        }
    }
}
