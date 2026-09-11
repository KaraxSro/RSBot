using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RSBot.Core;
using RSBot.Core.Network;

namespace RSBot.Log;

internal sealed class PacketNameRegistry
{
    private readonly Dictionary<string, string> _defaultNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _customNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenPackets = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath = Path.Combine(Kernel.BasePath, "User", "PacketNames.json");

    public static PacketNameRegistry Instance { get; } = new();

    private PacketNameRegistry()
    {
        LoadKnownPacketNames();
        LoadCustomNames();
    }

    public string GetName(string direction, string opcode)
    {
        var key = CreateKey(direction, opcode);
        if (_customNames.TryGetValue(key, out var customName))
            return customName;

        return _defaultNames.TryGetValue(key, out var defaultName) ? defaultName : string.Empty;
    }

    public void SetCustomName(string direction, string opcode, string name)
    {
        var key = CreateKey(direction, opcode);
        if (string.IsNullOrWhiteSpace(name))
            _customNames.Remove(key);
        else
            _customNames[key] = name.Trim();

        SaveCustomNames();
    }

    public void ResetCustomName(string direction, string opcode)
    {
        if (_customNames.Remove(CreateKey(direction, opcode)))
            SaveCustomNames();
    }

    public bool IsHidden(string direction, string opcode)
    {
        return _hiddenPackets.Contains(CreateKey(direction, opcode));
    }

    public void SetHidden(string direction, string opcode, bool hidden)
    {
        var key = CreateKey(direction, opcode);
        if (hidden)
            _hiddenPackets.Add(key);
        else
            _hiddenPackets.Remove(key);

        SaveCustomNames();
    }

    public List<HiddenPacketEntry> GetHiddenPackets()
    {
        return _hiddenPackets
            .Select(key =>
            {
                var separator = key.IndexOf('|');
                var direction = key[..separator];
                var opcode = key[(separator + 1)..];
                return new HiddenPacketEntry(direction, opcode, GetName(direction, opcode));
            })
            .OrderBy(entry => entry.Direction, StringComparer.Ordinal)
            .ThenBy(entry => entry.Opcode, StringComparer.Ordinal)
            .ToList();
    }

    private void LoadKnownPacketNames()
    {
        foreach (var handler in PacketManager.GetHandlers() ?? [])
            AddDiscoveredName(handler.Destination, handler.Opcode, handler.GetType().Name);

        foreach (var hook in PacketManager.GetHooks() ?? [])
            AddDiscoveredName(hook.Destination, hook.Opcode, hook.GetType().Name);

        AddKnownName("C->S", 0x2002, "KeepAliveRequest");
        AddKnownName("C->S", 0x7021, "PlayerMovementRequest");
        AddKnownName("C->S", 0x7034, "InventoryOperationRequest");
        AddKnownName("C->S", 0x7045, "ActionSelectRequest");
        AddKnownName("C->S", 0x7046, "NpcTalkOptionRequest");
        AddKnownName("C->S", 0x704B, "NpcCloseRequest");
        AddKnownName("C->S", 0x7118, "MagicPopPlayRequest");
        AddKnownName("C->S", 0x7119, "MagicPopExchangeRequest");

        AddKnownName("S->C", 0x3040, "InventoryUpdateItemResponse");
        AddKnownName("S->C", 0x3153, "SilkUpdateResponse");
        AddKnownName("S->C", 0x3154, "SilkNotifyResponse");
        AddKnownName("S->C", 0xB034, "InventoryOperationResponse");
        AddKnownName("S->C", 0xB045, "ActionSelectResponse");
        AddKnownName("S->C", 0xB046, "NpcTalkOptionResponse");
        AddKnownName("S->C", 0xB04B, "NpcCloseResponse");
        AddKnownName("S->C", 0xB118, "MagicPopPlayResponse");
        AddKnownName("S->C", 0xB119, "MagicPopExchangeResponse");
    }

    private void AddDiscoveredName(PacketDestination destination, ushort opcode, string name)
    {
        var direction = destination == PacketDestination.Server ? "C->S" : "S->C";
        var key = CreateKey(direction, $"0x{opcode:X4}");
        if (!_defaultNames.TryGetValue(key, out var existingName))
        {
            _defaultNames[key] = name;
            return;
        }

        var names = existingName.Split(" / ", StringSplitOptions.RemoveEmptyEntries);
        if (!names.Contains(name, StringComparer.Ordinal))
            _defaultNames[key] = $"{existingName} / {name}";
    }

    private void AddKnownName(string direction, ushort opcode, string name)
    {
        _defaultNames[CreateKey(direction, $"0x{opcode:X4}")] = name;
    }

    private void LoadCustomNames()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<PacketNameEntry>>(File.ReadAllText(_filePath)) ?? [];
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Direction)
                    || string.IsNullOrWhiteSpace(entry.Opcode))
                    continue;

                var key = CreateKey(entry.Direction, entry.Opcode);
                if (!string.IsNullOrWhiteSpace(entry.Name))
                    _customNames[key] = entry.Name.Trim();
                if (entry.Hidden)
                    _hiddenPackets.Add(key);
            }
        }
        catch (Exception exception)
        {
            RSBot.Core.Log.Warn($"Could not load packet names: {exception.Message}");
        }
    }

    private void SaveCustomNames()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            Directory.CreateDirectory(directory!);

            var keys = _customNames.Keys
                .Concat(_hiddenPackets)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var entries = keys
                .Select(key =>
                {
                    var separator = key.IndexOf('|');
                    return new PacketNameEntry
                    {
                        Direction = key[..separator],
                        Opcode = key[(separator + 1)..],
                        Name = _customNames.TryGetValue(key, out var name) ? name : null,
                        Hidden = _hiddenPackets.Contains(key)
                    };
                })
                .OrderBy(entry => entry.Direction, StringComparer.Ordinal)
                .ThenBy(entry => entry.Opcode, StringComparer.Ordinal)
                .ToList();

            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            RSBot.Core.Log.Warn($"Could not save packet names: {exception.Message}");
        }
    }

    private static string CreateKey(string direction, string opcode)
    {
        return $"{direction.Trim().ToUpperInvariant()}|{opcode.Trim().ToUpperInvariant()}";
    }

    private sealed class PacketNameEntry
    {
        public string Direction { get; set; }
        public string Opcode { get; set; }
        public string Name { get; set; }
        public bool Hidden { get; set; }
    }
}

internal sealed class HiddenPacketEntry
{
    public HiddenPacketEntry(string direction, string opcode, string name)
    {
        Direction = direction;
        Opcode = opcode;
        Name = name;
    }

    public string Direction { get; }
    public string Opcode { get; }
    public string Name { get; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name)
            ? $"{Direction}  {Opcode}"
            : $"{Direction}  {Opcode}  {Name}";
    }
}
