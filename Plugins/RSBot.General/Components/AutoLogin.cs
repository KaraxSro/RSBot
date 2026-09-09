using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Network.Protocol;
using RSBot.General.Models;
using Server = RSBot.General.Models.Server;

namespace RSBot.General.Components;

internal static class AutoLogin
{
    /// <summary>
    ///     Is the auto login pending <c>true</c> otherwise; <c>false</c>
    /// </summary>
    public static bool Pending;

    public static CancellationTokenSource? Cts { get; private set; }

    /// <summary>
    ///     Is the auto login handling <c>true</c> otherwise; <c>false</c>
    /// </summary>
    private static int _busy;
    private static string _attemptId = "none";

    internal static void RecordState(string message, LogLevel level = LogLevel.Debug)
    {
        Log.Append(level, $"[AutoLogin:{_attemptId}] {message}", "AutoLogin", _attemptId);
    }

    /// <summary>
    ///     Does the automatic login.
    /// </summary>
    public static void Handle(string trigger = "gateway event") => _ = HandleAsync(trigger);

    public static async Task RetryAfterAsync(int milliseconds, string trigger)
    {
        Log.Debug($"[AutoLogin:{_attemptId}] Retry scheduled in {milliseconds} ms; trigger={trigger}");
        await Task.Delay(milliseconds);
        await HandleAsync(trigger);
    }

    public static async Task HandleAsync(string trigger = "gateway event")
    {
        if (Pending)
        {
            Log.Debug($"[AutoLogin:{_attemptId}] Ignored trigger '{trigger}': login is pending in queue");
            return;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            Log.Debug($"[AutoLogin:{_attemptId}] Ignored trigger '{trigger}': another attempt is busy");
            return;
        }

        _attemptId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var started = Stopwatch.GetTimestamp();
        Log.Notify($"[AutoLogin:{_attemptId}] Attempt started; trigger={trigger}; clientLaunch={ClientManager.CurrentLaunchId ?? "none"}");
        Log.StatusLang("WaitingUser");

        try
        {
            if (!GlobalConfig.Get<bool>("RSBot.General.EnableAutomatedLogin"))
            {
                Log.Debug($"[AutoLogin:{_attemptId}] Automatic login is disabled");
                return;
            }

            var selectedAccount = Accounts.SavedAccounts?.Find(p =>
                p.Username == GlobalConfig.Get<string>("RSBot.General.AutoLoginAccountUsername")
            );
            if (selectedAccount == null)
            {
                Log.Warn($"[AutoLogin:{_attemptId}] No configured account was found; requesting a fresh server list in 5 seconds");
                await Task.Delay(5000);
                ClientlessManager.RequestServerList();
                return;
            }
            Log.Debug($"[AutoLogin:{_attemptId}] Account selected: {Mask(selectedAccount.Username)}; server={selectedAccount.Servername}");

            var server = Serverlist.GetServerByName(selectedAccount.Servername);
            if (server == null && Serverlist.Servers?.Any() == true)
            {
                Log.NotifyLang("ServerNotFound", selectedAccount.Servername);

                server = Serverlist.Servers.First();

                Log.NotifyLang("SelectedFirstServer", server.Name);
            }
            if (server == null)
            {
                Log.Warn($"[AutoLogin:{_attemptId}] No server is available; requesting server list in 5 seconds");
                await Task.Delay(5000);
                ClientlessManager.RequestServerList();
                return;
            }

            if (!server.Status)
            {
                Log.Notify($"[AutoLogin:{_attemptId}] Server '{server.Name}' is under inspection; retrying server list in 5 seconds");
                await Task.Delay(5000);
                ClientlessManager.RequestServerList();
                return;
            }

            if (GlobalConfig.Get("RSBot.General.EnableLoginDelay", false))
            {
                var delay = GlobalConfig.Get("RSBot.General.LoginDelay", 10) * 1000;
                Cts = new CancellationTokenSource();
                Log.Debug($"[AutoLogin:{_attemptId}] Waiting configured login delay: {delay} ms");

                try
                {
                    await Task.Delay(delay, Cts.Token);
                }
                catch (TaskCanceledException)
                {
                    Log.Debug($"[AutoLogin:{_attemptId}] Cancelled because a manual login was detected");
                    return;
                }
                finally
                {
                    Cts.Dispose();
                    Cts = null;
                }
            }

            SendLoginRequest(selectedAccount, server);
        }
        catch (Exception exception)
        {
            Log.Error($"[AutoLogin:{_attemptId}] Unexpected failure: {exception}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            Log.Notify($"[AutoLogin:{_attemptId}] Flow ended; duration={Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s; pending={Pending}");
        }
    }

    /// <summary>
    ///     Sends the secondary password if have.
    /// </summary>
    internal static void SendSecondaryPassword()
    {
        if (Accounts.Joined == null)
        {
            Log.Debug($"[AutoLogin:{_attemptId}] Secondary password skipped: no joined account");
            return;
        }

        if (!GlobalConfig.Get<bool>("RSBot.General.EnableAutomatedLogin"))
            return;

        var secondaryPassword = Accounts.Joined.SecondaryPassword;

        if (string.IsNullOrWhiteSpace(secondaryPassword))
        {
            Log.Debug($"[AutoLogin:{_attemptId}] Secondary password is not configured");
            return;
        }

        Blowfish blowfish = new();
        byte[] key = { 0x0F, 0x07, 0x3D, 0x20, 0x56, 0x62, 0xC9, 0xEB };

        if (Game.ClientType == GameClientType.Rigid)
            key = key.Reverse().ToArray();

        blowfish.Initialize(key);

        var encodedBuffer = blowfish.Encode(Encoding.ASCII.GetBytes(secondaryPassword));

        var packet = new Packet(0x6117, true);
        packet.WriteByte(4);
        packet.WriteUShort(secondaryPassword.Length);
        packet.WriteBytes(encodedBuffer);
        PacketManager.SendPacket(packet, PacketDestination.Server);
        Log.Notify($"[AutoLogin:{_attemptId}] Secondary password packet sent (value redacted)");
    }

    /// <summary>
    ///     Sends the login request.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="server">The server.</param>
    private static void SendLoginRequest(Account account, Server server)
    {
        Log.Notify($"[AutoLogin:{_attemptId}] Sending login request; account={Mask(account.Username)}; server={server.Name}; channel={account.Channel}");

        ushort opcode = 0x6102;
        if (Game.ClientType >= GameClientType.Chinese)
            opcode = 0x610A;

        var loginPacket = new Packet(opcode, true);
        loginPacket.WriteByte(Game.ReferenceManager.DivisionInfo.Locale);
        if (Game.ClientType == GameClientType.RuSro)
        {
            loginPacket.WriteString(GlobalConfig.Get<string>("RSBot.RuSro.login"));
            loginPacket.WriteString(GlobalConfig.Get<string>("RSBot.RuSro.password"));
        }
        else if (Game.ClientType == GameClientType.Japanese)
        {
            loginPacket.WriteString(string.Empty);
            loginPacket.WriteString(GlobalConfig.Get<string>("RSBot.JSRO.token"));
        }
        else
        {
            loginPacket.WriteString(account.Username);
            loginPacket.WriteString(account.Password);
        }

        Game.MacAddress = GenerateMacAddress();

        if (
            Game.ClientType == GameClientType.Turkey
            || Game.ClientType == GameClientType.VTC_Game
            || Game.ClientType == GameClientType.RuSro
            || Game.ClientType == GameClientType.Korean
            || Game.ClientType == GameClientType.Japanese
            || Game.ClientType == GameClientType.Taiwan
        )
            loginPacket.WriteBytes(Game.MacAddress);

        loginPacket.WriteUShort(server.Id);

        if (opcode == 0x610A)
            loginPacket.WriteByte(account.Channel);

        PacketManager.SendPacket(loginPacket, PacketDestination.Server);

        Accounts.Joined = account;
        Serverlist.Joining = server;

    }

    /// <summary>
    ///     Generates valid MAC address.
    /// </summary>
    /// <returns></returns>
    private static byte[] GenerateMacAddress()
    {
        Random rand = new Random();
        byte firstByte = (byte)(rand.Next(0, 256) & 0xFE);

        byte[] macBytes = new byte[6];
        macBytes[0] = firstByte;
        for (int i = 1; i < 6; i++)
        {
            macBytes[i] = (byte)rand.Next(0, 256);
        }

        return macBytes;
    }

    /// <summary>
    ///     Sends the static captcha.
    /// </summary>
    public static void SendStaticCaptcha()
    {
        if (
            !GlobalConfig.Get<bool>("RSBot.General.EnableStaticCaptcha")
            || !GlobalConfig.Get<bool>("RSBot.General.EnableAutomatedLogin")
        )
            return;

        var captcha = GlobalConfig.Get<string>("RSBot.General.StaticCaptcha");
        captcha ??= string.Empty;

        Log.Notify($"[AutoLogin:{_attemptId}] Sending configured static captcha (value redacted; length={captcha.Length})");

        var packet = new Packet(0x6323);
        packet.WriteString(captcha);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Enters the game.
    /// </summary>
    /// <param name="character">The character.</param>
    public static void EnterGame(string character)
    {
        if (!GlobalConfig.Get<bool>("RSBot.General.EnableAutomatedLogin"))
            return;

        var packet = new Packet(0x7001);
        packet.WriteString(character);
        PacketManager.SendPacket(packet, PacketDestination.Server);

        PlayerConfig.Load(character);

        EventManager.FireEvent("OnEnterGame");
        Log.Notify($"[AutoLogin:{_attemptId}] Character entry requested: {Mask(character)}");
    }

    private static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "<empty>";
        return value.Length <= 2 ? new string('*', value.Length) : $"{value[0]}***{value[^1]}";
    }
}
