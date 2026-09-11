using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;

namespace RSBot.General.Components;

internal static class AgentLoginWatchdog
{
    private const int TimeoutMilliseconds = 5_000;
    private const int MaximumRecoveryAttempts = 10;
    private static readonly object SyncRoot = new();

    private static CancellationTokenSource _cancellation;
    private static int _generation;
    private static int _recoveryAttempts;
    private static string _phase = "inactive";
    private static string _launchId = "none";

    internal static void PrepareForClientLaunch(string reason)
    {
        lock (SyncRoot)
        {
            CancelLocked();
            if (!string.Equals(reason, "automatic reconnect", StringComparison.OrdinalIgnoreCase))
                _recoveryAttempts = 0;
            _phase = "waiting for gateway login acceptance";
            _launchId = ClientManager.CurrentLaunchId ?? "pending";
        }
    }

    internal static void ObserveGatewayLoginAccepted()
    {
        Arm("waiting for agent login response");
    }

    internal static void ObserveAgentServerConnected()
    {
        Arm("waiting for agent login response");
    }

    internal static void ObserveAgentLoginResponse(bool accepted)
    {
        if (!accepted)
        {
            Cancel("agent login was rejected", false);
            return;
        }

        Arm("waiting for character list");
    }

    internal static void ObserveCharacterList()
    {
        Cancel("character list received", false);
    }

    internal static void ObserveEnterGame()
    {
        Cancel("character entered the game", true);
    }

    internal static void Cancel(string reason, bool resetRecoveryAttempts)
    {
        string phase;
        string launchId;
        lock (SyncRoot)
        {
            phase = _phase;
            launchId = _launchId;
            CancelLocked();
            _phase = "inactive";
            if (resetRecoveryAttempts)
                _recoveryAttempts = 0;
        }

        if (phase != "inactive")
            Log.Debug($"[AgentLoginWatchdog:{launchId}] Cancelled while {phase}; reason={reason}.");
    }

    private static void Arm(string phase)
    {
        if (Game.Clientless || !GlobalConfig.Get<bool>("RSBot.General.EnableAutomatedLogin"))
            return;

        CancellationToken token;
        int generation;
        int recoveryAttempts;
        string launchId;
        lock (SyncRoot)
        {
            CancelLocked();
            _phase = phase;
            _launchId = ClientManager.CurrentLaunchId ?? "none";
            _cancellation = new CancellationTokenSource();
            token = _cancellation.Token;
            generation = _generation;
            recoveryAttempts = _recoveryAttempts;
            launchId = _launchId;
        }

        Log.Debug(
            $"[AgentLoginWatchdog:{launchId}] Armed for {phase}; timeout={TimeoutMilliseconds / 1000}s; " +
            $"recoveryAttempt={recoveryAttempts}/{MaximumRecoveryAttempts}."
        );
        _ = RunAsync(generation, launchId, phase, token);
    }

    private static async Task RunAsync(int generation, string launchId, string phase, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeoutMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var shouldRecover = false;
        var recoveryAttempt = 0;
        var recoveryAttempts = 0;
        lock (SyncRoot)
        {
            if (generation != _generation || token.IsCancellationRequested || _phase != phase)
                return;

            CancelLocked();
            _phase = "inactive";

            if (_recoveryAttempts < MaximumRecoveryAttempts
                && !Game.Clientless
                && !ClientManager.IsIntentionalExit
                && ClientManager.IsRunning)
            {
                recoveryAttempt = ++_recoveryAttempts;
                shouldRecover = true;
            }
            recoveryAttempts = _recoveryAttempts;
        }

        if (!shouldRecover)
        {
            Log.Error(
                $"[AgentLoginWatchdog:{launchId}] Timed out after {TimeoutMilliseconds / 1000}s while {phase}; " +
                $"automatic recovery was not started (attempts={recoveryAttempts}/{MaximumRecoveryAttempts}, " +
                $"clientless={Game.Clientless}, intentionalExit={ClientManager.IsIntentionalExit}, " +
                $"agentConnected={Kernel.Proxy?.IsConnectedToAgentserver == true})."
            );
            return;
        }

        Log.Error(
            $"[AgentLoginWatchdog:{launchId}] Timed out after {TimeoutMilliseconds / 1000}s while {phase}; " +
            $"closing the stalled connection and starting controlled recovery " +
            $"{recoveryAttempt}/{MaximumRecoveryAttempts}."
        );
        Kernel.Proxy.Shutdown();
    }

    private static void CancelLocked()
    {
        _generation++;
        if (_cancellation == null)
            return;

        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = null;
    }
}
