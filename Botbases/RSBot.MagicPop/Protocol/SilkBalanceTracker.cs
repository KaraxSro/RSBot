using System;

namespace RSBot.MagicPop.Protocol;

internal sealed class SilkBalanceTracker
{
    private readonly object _syncRoot = new();

    private SilkBalance _current;

    public SilkBalance Current
    {
        get
        {
            lock (_syncRoot)
                return _current;
        }
        private set => _current = value;
    }
    public DateTime? UpdatedUtc { get; private set; }

    public void Update(SilkBalance balance)
    {
        lock (_syncRoot)
        {
            Current = balance;
            UpdatedUtc = DateTime.UtcNow;
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            Current = null;
            UpdatedUtc = null;
        }
    }
}
