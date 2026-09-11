using System;
using System.Globalization;

namespace RSBot.MagicPop.References;

internal sealed class RefGachaItemSet
{
    public const int ColumnCount = 15;

    public byte Service { get; private set; }
    public uint SetId { get; private set; }
    public uint RefItemId { get; private set; }
    public uint Ratio { get; private set; }
    public uint Count { get; private set; }
    public uint GachaId { get; private set; }
    public byte Visible { get; private set; }
    public int[] Parameters { get; } = new int[4];
    public string[] ParameterDescriptions { get; } = new string[4];

    public static bool TryParse(string line, out RefGachaItemSet value, out string error)
    {
        value = null;
        error = null;

        var columns = line.Split('\t');
        if (columns.Length != ColumnCount)
        {
            error = $"Expected {ColumnCount} columns, found {columns.Length}.";
            return false;
        }

        if (!TryByte(columns[0], "Service", out var service, out error)
            || !TryUInt(columns[1], "Set_ID", out var setId, out error)
            || !TryUInt(columns[2], "RefItemID", out var refItemId, out error)
            || !TryUInt(columns[3], "Ratio", out var ratio, out error)
            || !TryUInt(columns[4], "Count", out var count, out error)
            || !TryUInt(columns[5], "GachaID", out var gachaId, out error)
            || !TryByte(columns[6], "Visible", out var visible, out error))
            return false;

        if (service > 1 || visible > 1)
        {
            error = "Service and Visible must be either 0 or 1.";
            return false;
        }

        if (service == 1 && (setId == 0 || refItemId == 0 || gachaId == 0 || count == 0))
        {
            error = "Enabled records require non-zero Set_ID, RefItemID, Count and GachaID values.";
            return false;
        }

        var result = new RefGachaItemSet
        {
            Service = service,
            SetId = setId,
            RefItemId = refItemId,
            Ratio = ratio,
            Count = count,
            GachaId = gachaId,
            Visible = visible
        };

        for (var index = 0; index < result.Parameters.Length; index++)
        {
            var columnIndex = 7 + index * 2;
            if (!TryInt(columns[columnIndex], $"Param{index + 1}", out var parameter, out error))
                return false;

            result.Parameters[index] = parameter;
            result.ParameterDescriptions[index] = columns[columnIndex + 1];
        }

        value = result;
        return true;
    }

    private static bool TryByte(string text, string name, out byte value, out string error)
    {
        if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = null;
            return true;
        }

        error = $"Invalid {name} value '{text}'.";
        return false;
    }

    private static bool TryUInt(string text, string name, out uint value, out string error)
    {
        if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = null;
            return true;
        }

        error = $"Invalid {name} value '{text}'.";
        return false;
    }

    private static bool TryInt(string text, string name, out int value, out string error)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = null;
            return true;
        }

        error = $"Invalid {name} value '{text}'.";
        return false;
    }
}
