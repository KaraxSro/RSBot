using System;
using System.Collections.Generic;
using System.Linq;
using RSBot.Core;

namespace RSBot.Statistics.Stats;

internal static class CalculatorRegistry
{
    private static readonly Dictionary<IStatisticCalculator, object> Values = new();
    private static System.Windows.Forms.Timer _collectionTimer;
    private static bool _initialResetPending;

    public static bool CollectionEnabled { get; private set; }

    /// <summary>
    ///     Gets the statistics.
    /// </summary>
    /// <value>
    ///     The statistics.
    /// </value>
    public static List<IStatisticCalculator> Calculators { get; private set; }

    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    public static void Initialize()
    {
        Calculators = new List<IStatisticCalculator>(20);
        Values.Clear();
        LoadCalculators();

        CollectionEnabled = true;
        _initialResetPending = true;
        _collectionTimer?.Dispose();
        _collectionTimer = new System.Windows.Forms.Timer { Interval = 1_000 };
        _collectionTimer.Tick += (_, _) => Collect();
        _collectionTimer.Start();
    }

    /// <summary>
    /// Returns the most recently collected value without advancing the calculator.
    /// </summary>
    public static object GetValue(IStatisticCalculator calculator)
    {
        return Values.TryGetValue(calculator, out var value) ? value : 0;
    }

    /// <summary>
    /// Enables or pauses collection. Enabling starts a fresh measurement period so
    /// changes made while collection was paused are not counted retroactively.
    /// </summary>
    public static void SetCollectionEnabled(bool enabled)
    {
        if (CollectionEnabled == enabled)
            return;

        CollectionEnabled = enabled;
        if (enabled)
        {
            _initialResetPending = true;
            Collect();
        }
    }

    /// <summary>
    /// Resets every calculator and immediately refreshes the collected values.
    /// </summary>
    public static void Reset()
    {
        foreach (var calculator in Calculators)
            calculator.Reset();

        _initialResetPending = false;
        Collect();
    }

    /// <summary>
    /// Resets one calculator and immediately refreshes its collected value.
    /// </summary>
    public static void Reset(IStatisticCalculator calculator)
    {
        calculator.Reset();
        Values[calculator] = calculator.GetValue();
    }

    private static void Collect()
    {
        if (!CollectionEnabled)
            return;

        if (_initialResetPending)
        {
            if (!Game.Ready)
                return;

            foreach (var calculator in Calculators)
                calculator.Reset();

            _initialResetPending = false;
        }

        foreach (var calculator in Calculators)
            Values[calculator] = calculator.GetValue();
    }

    /// <summary>
    ///     Loads the calculators.
    /// </summary>
    private static void LoadCalculators()
    {
        var type = typeof(IStatisticCalculator);
        var types = AppDomain
            .CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => type.IsAssignableFrom(p) && !p.IsInterface)
            .ToArray();

        foreach (var handler in types)
        {
            var instance = (IStatisticCalculator)Activator.CreateInstance(handler);
            instance.Initialize();
            Calculators.Add(instance);
        }

        Log.Debug($"Found {Calculators.Count} statistic calulators.");
    }
}
