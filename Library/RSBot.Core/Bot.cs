using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Plugins;

namespace RSBot.Core;

public class Bot
{
    private readonly object _startLock = new();
    private Task _botTask;

    /// <summary>
    ///     Gets or sets a value indicating whether this <see cref="Bot" /> is running.
    /// </summary>
    /// <value>
    ///     <c>true</c> if running; otherwise, <c>false</c>.
    /// </value>
    public volatile bool Running;

    /// <summary>
    ///     Gets or sets to the <see cref="CancellationToken" />
    /// </summary>
    public CancellationTokenSource TokenSource;

    /// <summary>
    ///     Gets the base.
    /// </summary>
    /// <value>
    ///     The base.
    /// </value>
    public IBotbase Botbase { get; private set; }

    /// <summary>
    ///     Sets the botbase.
    /// </summary>
    /// <param name="botBase">The bot base.</param>
    public void SetBotbase(IBotbase botBase)
    {
        Botbase = botBase;
        Botbase.Initialize();

        EventManager.FireEvent("OnSetBotbase", botBase);
    }

    /// <summary>
    ///     Starts this instance.
    /// </summary>
    public void Start()
    {
        CancellationTokenSource tokenSource;
        lock (_startLock)
        {
            if (Running || Botbase == null)
                return;

            tokenSource = new CancellationTokenSource();
            TokenSource = tokenSource;
            Running = true;
        }

        _botTask = Task.Run(
            async () =>
            {
                try
                {
                    EventManager.FireEvent("OnStartBot");
                    Botbase.Start();

                    while (!tokenSource.IsCancellationRequested)
                    {
                        if (Game.Ready)
                        {
                            var tickContext = new BotTickContext();
                            EventManager.FireEvent("OnBeforeBotTick", tickContext);

                            if (!tickContext.Cancel)
                                Botbase.Tick();
                        }

                        // Always yield, including while the client is loading or disconnected.
                        // The old continue above this delay caused a full-core busy loop.
                        await Task.Delay(100, tokenSource.Token).ConfigureAwait(false);
                    }
                }
                catch (System.OperationCanceledException) when (tokenSource.IsCancellationRequested)
                {
                    // Expected when the bot is stopped.
                }
                catch (System.Exception ex)
                {
                    Log.Fatal(ex);
                    Running = false;
                }
            },
            tokenSource.Token
        );
    }

    /// <summary>
    ///     Stops this instance.
    /// </summary>
    public void Stop()
    {
        ScriptManager.Stop();
        ShoppingManager.Stop();
        PickupManager.Stop();

        if (Botbase == null)
            return;

        if (!Running)
            return;

        if (!TokenSource.IsCancellationRequested)
            TokenSource.Cancel();

        EventManager.FireEvent("OnStopBot");
        Log.Notify($"Stopping bot {Botbase.Title}");

        Game.SelectedEntity = null;
        Botbase.Stop();
        Running = false;

        Log.Notify($"Stoped bot {Botbase.Title}");
        Log.Status("Bot stopped");
    }
}
