using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RSBot.Core.Event;

public static class EventManager
{
    private static readonly object ListenerLock = new();
    private static readonly List<(string name, Delegate handler)> Listeners = new();
    private static readonly ConcurrentQueue<(string name, Delegate handler, object[] parameters)> NetworkQueue = new();
    private static readonly ConcurrentDictionary<Delegate, PendingUiInvocation> PendingUiInvocations = new();
    private static readonly ConcurrentDictionary<Delegate, PendingUiBatch> PendingUiBatches = new();

    private static readonly HashSet<string> CoalescedUiEvents = new(StringComparer.Ordinal)
    {
        "OnChangeStatusText",
        "OnExpSpUpdate",
        "OnUpdateHPMP",
        "OnUpdateGold",
        "OnUpdateSP",
        "OnUpdateEntityHp",
        "OnGrowthExperienceUpdate",
        "OnGrowthHungerUpdate",
        "OnGrowthHealthUpdate",
        "OnFellowExperienceUpdate",
        "OnFellowSatietyUpdate",
        "OnFellowHealthUpdate",
        "OnUpdateTransportHealth",
        "OnUpdateJobTransportHealth"
    };

    private static readonly HashSet<string> BatchedUiEvents = new(StringComparer.Ordinal)
    {
        "OnAddLog"
    };

    private static SynchronizationContext _uiContext;
    private static int _uiThreadId;
    private static int _networkWorkerRunning;

    /// <summary>
    /// Registers the WinForms synchronization context used by UI event subscribers.
    /// </summary>
    public static void ConfigureUiThread(SynchronizationContext context)
    {
        _uiContext = context ?? throw new ArgumentNullException(nameof(context));
        _uiThreadId = Environment.CurrentManagedThreadId;
    }

    public static void SubscribeEvent(string name, Delegate handler)
    {
        if (handler == null)
            return;

        lock (ListenerLock)
            Listeners.Add((name, handler));
    }

    public static void SubscribeEvent(string name, Action handler)
    {
        SubscribeEvent(name, (Delegate)handler);
    }

    public static void FireEvent(string name, params object[] parameters)
    {
        Delegate[] targets;
        lock (ListenerLock)
        {
            targets = Listeners
                .Where(listener => listener.name == name && listener.handler.Method.GetParameters().Length == parameters.Length)
                .Select(listener => listener.handler)
                .ToArray();
        }

        foreach (var target in targets)
        {
            if (target.Target is Control control && _uiContext != null)
            {
                if (control.IsDisposed || control.Disposing)
                    continue;

                DispatchUi(name, target, parameters);
            }
            else if (Thread.CurrentThread.Name == "Network.PacketProcessor")
            {
                NetworkQueue.Enqueue((name, target, parameters));
                StartNetworkWorker();
            }
            else
            {
                InvokeTarget(name, target, parameters);
            }
        }
    }

    private static void DispatchUi(string name, Delegate target, object[] parameters)
    {
        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            InvokeTarget(name, target, parameters);
            return;
        }

        if (BatchedUiEvents.Contains(name))
        {
            DispatchUiBatch(name, target, parameters);
            return;
        }

        if (!CoalescedUiEvents.Contains(name))
        {
            _uiContext.Post(_ => InvokeTarget(name, target, parameters), null);
            return;
        }

        var pending = PendingUiInvocations.GetOrAdd(target, _ => new PendingUiInvocation());
        lock (pending.SyncRoot)
        {
            pending.Name = name;
            pending.Parameters = parameters;
            if (pending.Scheduled)
                return;

            pending.Scheduled = true;
        }

        _uiContext.Post(_ => FlushUiInvocation(target, pending), null);
    }

    private static void DispatchUiBatch(string name, Delegate target, object[] parameters)
    {
        var batch = PendingUiBatches.GetOrAdd(target, _ => new PendingUiBatch());
        batch.Invocations.Enqueue((name, parameters));

        if (Interlocked.CompareExchange(ref batch.Scheduled, 1, 0) != 0)
            return;

        _uiContext.Post(_ => FlushUiBatch(target, batch), null);
    }

    private static void FlushUiBatch(Delegate target, PendingUiBatch batch)
    {
        do
        {
            while (batch.Invocations.TryDequeue(out var invocation))
                InvokeTarget(invocation.name, target, invocation.parameters);

            Interlocked.Exchange(ref batch.Scheduled, 0);
        } while (!batch.Invocations.IsEmpty && Interlocked.CompareExchange(ref batch.Scheduled, 1, 0) == 0);
    }

    private static void FlushUiInvocation(Delegate target, PendingUiInvocation pending)
    {
        string name;
        object[] parameters;
        lock (pending.SyncRoot)
        {
            name = pending.Name;
            parameters = pending.Parameters;
            pending.Scheduled = false;
        }

        InvokeTarget(name, target, parameters);
    }

    private static void StartNetworkWorker()
    {
        if (Interlocked.CompareExchange(ref _networkWorkerRunning, 1, 0) != 0)
            return;

        _ = Task.Run(ProcessNetworkQueue);
    }

    private static void ProcessNetworkQueue()
    {
        do
        {
            while (NetworkQueue.TryDequeue(out var invocation))
                InvokeTarget(invocation.name, invocation.handler, invocation.parameters);

            Interlocked.Exchange(ref _networkWorkerRunning, 0);
        } while (!NetworkQueue.IsEmpty && Interlocked.CompareExchange(ref _networkWorkerRunning, 1, 0) == 0);
    }

    private static void InvokeTarget(string eventName, Delegate target, object[] parameters)
    {
        if (target.Target is Control control && (control.IsDisposed || control.Disposing))
            return;

        try
        {
            target.DynamicInvoke(parameters);
        }
        catch (Exception exception)
        {
            if (eventName == "OnAddLog")
                Debug.WriteLine(exception);
            else
                Log.Fatal(exception);
        }
    }

    private sealed class PendingUiInvocation
    {
        public readonly object SyncRoot = new();
        public string Name;
        public object[] Parameters;
        public bool Scheduled;
    }

    private sealed class PendingUiBatch
    {
        public readonly ConcurrentQueue<(string name, object[] parameters)> Invocations = new();
        public int Scheduled;
    }
}
