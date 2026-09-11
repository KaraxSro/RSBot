using System.Globalization;

namespace RSBot.MagicPop.References;

internal sealed class RefGachaNpcMap
{
    public const int ColumnCount = 4;

    public byte Service { get; private set; }
    public uint RefNpcId { get; private set; }
    public uint FirstSetId { get; private set; }
    public uint LastSetId { get; private set; }

    public static bool TryParse(string line, out RefGachaNpcMap value, out string error)
    {
        value = null;
        error = null;

        var columns = line.Split('\t');
        if (columns.Length != ColumnCount)
        {
            error = $"Expected {ColumnCount} columns, found {columns.Length}.";
            return false;
        }

        if (!byte.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var service)
            || service > 1)
        {
            error = $"Invalid Service value '{columns[0]}'.";
            return false;
        }

        if (!uint.TryParse(columns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var refNpcId)
            || !uint.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var firstSetId)
            || !uint.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastSetId))
        {
            error = "RefNPCID and set range values must be unsigned integers.";
            return false;
        }

        if (service == 1 && (refNpcId == 0 || firstSetId == 0 || lastSetId < firstSetId))
        {
            error = "Enabled records require a valid RefNPCID and set range.";
            return false;
        }

        value = new RefGachaNpcMap
        {
            Service = service,
            RefNpcId = refNpcId,
            FirstSetId = firstSetId,
            LastSetId = lastSetId
        };
        return true;
    }
}
