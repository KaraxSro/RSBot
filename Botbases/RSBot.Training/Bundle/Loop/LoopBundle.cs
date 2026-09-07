using System.IO;
using System.Threading;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;

namespace RSBot.Training.Bundle.Loop;

internal class LoopBundle : IBundle
{
    private const int TELEPORT_SETTLE_TIME = 2_000;

    private int _startGate;
    private volatile bool _reverseReturnPending;
    private volatile int _teleportCompletedAt;

    /// <summary>
    ///     Gets the configuration.
    /// </summary>
    /// <value>
    ///     The configuration.
    /// </value>
    public LoopConfig Config { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this <see cref="LoopBundle" /> is running.
    /// </summary>
    /// <value>
    ///     <c>true</c> if running; otherwise, <c>false</c>.
    /// </value>
    public bool Running { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether [townscript running].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [townscript running]; otherwise, <c>false</c>.
    /// </value>
    public bool TownscriptRunning { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the bot should wait for a teleport to finish and the player position to settle.
    /// </summary>
    public bool WaitingForTeleportSettle
    {
        get
        {
            if (_reverseReturnPending && _teleportCompletedAt == 0)
                return true;

            if (
                _teleportCompletedAt != 0
                && Kernel.TickCount - _teleportCompletedAt < TELEPORT_SETTLE_TIME
            )
                return true;

            _reverseReturnPending = false;
            _teleportCompletedAt = 0;

            return false;
        }
    }

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (!Running)
            return;

        if (Config.UseVehicle && !Game.Player.HasActiveVehicle && !Game.Player.IsInDungeon)
        {
            Game.Player.SummonVehicle();

            //Wait for the vehicle to spawn
            Thread.Sleep(1000);
        }

        //We don't need to use buffs in town...
        if (Config.CastBuffs && !TownscriptRunning)
            Bundles.Buff.Invoke();
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        Config = new LoopConfig
        {
            WalkScript = PlayerConfig.Get<string>("RSBot.Walkback.File"),
            UseSpeedDrug = PlayerConfig.Get<bool>("RSBot.Training.checkUseSpeedDrug", true),
            UseVehicle = PlayerConfig.Get<bool>("RSBot.Training.checkUseMount", true),
            CastBuffs = PlayerConfig.Get<bool>("RSBot.Training.checkCastBuffs", true),
            UseReverse = PlayerConfig.Get<bool>("RSBot.Training.checkBoxUseReverse", false),
        };
    }

    public void Stop()
    {
        if (ScriptManager.Running)
            ScriptManager.Stop();

        if (ShoppingManager.Running)
            ShoppingManager.Stop();

        _reverseReturnPending = false;
        _teleportCompletedAt = 0;
        Running = false;
    }

    /// <summary>
    ///     Marks a teleport as completed and starts the position settling period.
    /// </summary>
    public void OnTeleportComplete()
    {
        _teleportCompletedAt = Kernel.TickCount;
    }

    /// <summary>
    ///     Starts this instance.
    /// </summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _startGate, 1, 0) != 0)
        {
            Log.Debug("[Training] Ignoring a duplicate loop start request.");
            return;
        }

        try
        {
            Running = true;

            Refresh();
            CheckForTownScript();
        }
        finally
        {
            Running = false;
            Interlocked.Exchange(ref _startGate, 0);
        }
    }

    /// <summary>
    ///     Checks for town script.
    /// </summary>
    public void CheckForTownScript()
    {
        if (ScriptManager.Running)
            return;

        var filename = Path.Combine(
            ScriptManager.InitialDirectory,
            "Towns",
            Game.Player.Movement.Source.Region + ".rbs"
        );

        Log.Debug(
            $"[Training] Checking town script for region [{Game.Player.Movement.Source.Region.Id}] at [{filename}]."
        );

        //The player is in town, therefore, we need to run the town script first.
        if (!File.Exists(filename))
        {
            CheckForWalkbackScript();
            return;
        }

        if (PlayerConfig.Get<bool>("RSBot.Protection.checkStopBotOnReturnToTown"))
        {
            Kernel.Bot.Stop();
            return;
        }

        Log.NotifyLang("LoadingTownScript", filename);

        TownscriptRunning = true;

        bool townScriptSucceeded;
        try
        {
            ScriptManager.Load(filename);
            townScriptSucceeded = ScriptManager.RunScriptWithResult(false);
        }
        finally
        {
            TownscriptRunning = false;
        }

        if (!townScriptSucceeded)
        {
            Log.Warn("[Training] Town script did not finish successfully. Return to the training area was cancelled.");
            return;
        }

        if (!Running)
            return;

        if (Running && Config.UseReverse)
        {
            var filter = new TypeIdFilter(3, 3, 3, 3);
            var item = Game.Player.Inventory.GetItem(filter);
            if (item != null)
            {
                /*
                    2 => go to last recall point
                    3 => go to last died position
                    7 => select position on map

                    error codes:
                        85: Cannot find the place where you selected as recall point.
                        86: Cannot find the place where you died.
                */
                _reverseReturnPending = true;
                _teleportCompletedAt = 0;

                if (item.UseTo(3))
                    return;

                _reverseReturnPending = false;
                _teleportCompletedAt = 0;
            }
        }

        Invoke();

        CheckForWalkbackScript(true);
    }

    /// <summary>
    ///     Checks for walkback script.
    /// </summary>
    public void CheckForWalkbackScript(bool startFromTown = false)
    {
        if (
            Config.WalkScript == null
            || ScriptManager.Running
            || !File.Exists(Config.WalkScript)
            || !Kernel.Bot.Running
        )
            return;

        Invoke();
        Log.NotifyLang("LoadingWalkScript", Config.WalkScript);

        ScriptManager.Load(Config.WalkScript);
        ScriptManager.RunScript(!startFromTown);
    }
}
