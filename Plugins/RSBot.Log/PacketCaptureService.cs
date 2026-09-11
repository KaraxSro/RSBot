using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Network;

namespace RSBot.Log;

internal sealed class PacketCaptureService
{
    private const int MaxPendingDisplayEntries = 20_000;
    private const int MaxPendingFileEntries = 50_000;
    private readonly object _syncRoot = new();
    private readonly ConcurrentQueue<PacketCaptureDisplayEntry> _pendingDisplayEntries = new();
    private BlockingCollection<PacketCaptureWorkItem> _captureQueue;
    private Task _writerTask;
    private int _pendingDisplayCount;
    private long _sequence;
    private bool _initialized;
    private volatile bool _enabled;

    public static PacketCaptureService Instance { get; } = new();

    public bool Enabled => _enabled;

    public string CurrentFilePath { get; private set; }

    private PacketCaptureService() { }

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        EventManager.SubscribeEvent("OnClientPacketReceive", new Action<Packet>(packet => Capture(packet, "C->S")));
        EventManager.SubscribeEvent("OnServerPacketReceive", new Action<Packet>(packet => Capture(packet, "S->C")));
    }

    public bool Start(out string error)
    {
        error = null;

        lock (_syncRoot)
        {
            if (_enabled)
                return true;

            try
            {
                var directory = Path.Combine(Kernel.BasePath, "User", "Logs", "PacketCapture");
                Directory.CreateDirectory(directory);

                CurrentFilePath = Path.Combine(
                    directory,
                    $"packet-capture-{DateTime.Now:yyyyMMdd-HHmmss-fff}.jsonl");

                var writer = new StreamWriter(CurrentFilePath, false, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                var captureQueue = new BlockingCollection<PacketCaptureWorkItem>(MaxPendingFileEntries);

                Interlocked.Exchange(ref _sequence, 0);
                _captureQueue = captureQueue;
                _enabled = true;
                _writerTask = Task.Factory.StartNew(
                    () => ProcessCaptureQueue(captureQueue, writer),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                return true;
            }
            catch (Exception exception)
            {
                _captureQueue?.Dispose();
                _captureQueue = null;
                _writerTask = null;
                CurrentFilePath = null;
                error = exception.Message;
                return false;
            }
        }
    }

    public void Stop()
    {
        BlockingCollection<PacketCaptureWorkItem> captureQueue;
        Task writerTask;

        lock (_syncRoot)
        {
            _enabled = false;
            captureQueue = _captureQueue;
            writerTask = _writerTask;
            _captureQueue = null;
            _writerTask = null;
        }

        captureQueue?.CompleteAdding();
        if (writerTask != null && writerTask.Id != Task.CurrentId)
        {
            try
            {
                writerTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Writer errors are surfaced in the packet capture view.
            }
        }
    }

    public bool TryDequeue(out PacketCaptureDisplayEntry entry)
    {
        if (!_pendingDisplayEntries.TryDequeue(out entry))
            return false;

        Interlocked.Decrement(ref _pendingDisplayCount);
        return true;
    }

    public void ClearPending()
    {
        while (_pendingDisplayEntries.TryDequeue(out _))
            Interlocked.Decrement(ref _pendingDisplayCount);
    }

    private void Capture(Packet packet, string direction)
    {
        if (!_enabled || packet == null)
            return;

        try
        {
            var sourceBytes = packet.GetBytes() ?? Array.Empty<byte>();
            var payload = sourceBytes.Length == 0 ? Array.Empty<byte>() : (byte[])sourceBytes.Clone();
            var entry = new PacketCaptureWorkItem
            {
                Sequence = Interlocked.Increment(ref _sequence),
                TimestampUtc = DateTimeOffset.UtcNow,
                Direction = direction,
                Opcode = packet.Opcode,
                Length = payload.Length,
                Encrypted = packet.Encrypted,
                Massive = packet.Massive,
                Payload = payload
            };

            var captureQueue = _captureQueue;
            if (captureQueue == null)
                return;

            if (!captureQueue.TryAdd(entry))
                CaptureFailed(captureQueue, "The packet writer queue is full.");
        }
        catch (Exception exception)
        {
            CaptureFailed(_captureQueue, exception.Message);
        }
    }

    private void ProcessCaptureQueue(
        BlockingCollection<PacketCaptureWorkItem> captureQueue,
        StreamWriter writer)
    {
        try
        {
            using (writer)
            {
                foreach (var workItem in captureQueue.GetConsumingEnumerable())
                {
                    var opcode = $"0x{workItem.Opcode:X4}";
                    var payloadHex = Convert.ToHexString(workItem.Payload);
                    var fileEntry = new PacketCaptureFileEntry
                    {
                        Sequence = workItem.Sequence,
                        TimestampUtc = workItem.TimestampUtc,
                        Direction = workItem.Direction,
                        Opcode = opcode,
                        Length = workItem.Length,
                        Encrypted = workItem.Encrypted,
                        Massive = workItem.Massive,
                        PayloadHex = payloadHex
                    };

                    writer.WriteLine(JsonSerializer.Serialize(fileEntry));
                    EnqueueDisplay(new PacketCaptureDisplayEntry(
                        workItem.Sequence,
                        workItem.TimestampUtc.ToLocalTime(),
                        workItem.Direction,
                        opcode,
                        workItem.Length,
                        workItem.Encrypted,
                        workItem.Massive,
                        payloadHex));
                }
            }
        }
        catch (Exception exception)
        {
            CaptureFailed(captureQueue, exception.Message);
        }
    }

    private void CaptureFailed(BlockingCollection<PacketCaptureWorkItem> captureQueue, string message)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_captureQueue, captureQueue))
                return;

            _enabled = false;
            _captureQueue = null;
            _writerTask = null;
        }

        if (captureQueue != null && !captureQueue.IsAddingCompleted)
            captureQueue.CompleteAdding();

        EnqueueStatus($"Packet capture stopped: {message}");
    }

    private void EnqueueDisplay(PacketCaptureDisplayEntry entry)
    {
        if (Interlocked.Increment(ref _pendingDisplayCount) <= MaxPendingDisplayEntries)
        {
            _pendingDisplayEntries.Enqueue(entry);
            return;
        }

        Interlocked.Decrement(ref _pendingDisplayCount);
    }

    private void EnqueueStatus(string message)
    {
        EnqueueDisplay(new PacketCaptureDisplayEntry(
            0,
            DateTimeOffset.Now,
            "INFO",
            string.Empty,
            0,
            false,
            false,
            message));
    }

    private readonly struct PacketCaptureWorkItem
    {
        public long Sequence { get; init; }
        public DateTimeOffset TimestampUtc { get; init; }
        public string Direction { get; init; }
        public ushort Opcode { get; init; }
        public int Length { get; init; }
        public bool Encrypted { get; init; }
        public bool Massive { get; init; }
        public byte[] Payload { get; init; }
    }

    private sealed class PacketCaptureFileEntry
    {
        public long Sequence { get; set; }
        public DateTimeOffset TimestampUtc { get; set; }
        public string Direction { get; set; }
        public string Opcode { get; set; }
        public int Length { get; set; }
        public bool Encrypted { get; set; }
        public bool Massive { get; set; }
        public string PayloadHex { get; set; }
    }
}

internal readonly struct PacketCaptureDisplayEntry
{
    public PacketCaptureDisplayEntry(
        long sequence,
        DateTimeOffset timestamp,
        string direction,
        string opcode,
        int length,
        bool encrypted,
        bool massive,
        string payloadHex)
    {
        Sequence = sequence;
        Timestamp = timestamp;
        Direction = direction;
        Opcode = opcode;
        Length = length;
        Encrypted = encrypted;
        Massive = massive;
        PayloadHex = payloadHex;
    }

    public long Sequence { get; }
    public DateTimeOffset Timestamp { get; }
    public string Direction { get; }
    public string Opcode { get; }
    public int Length { get; }
    public bool Encrypted { get; }
    public bool Massive { get; }
    public string PayloadHex { get; }
}
