using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;

namespace RSBot.MagicPop.References;

internal sealed class GachaReward
{
    public GachaReward(RefGachaItemSet source, RefObjItem item)
    {
        Source = source;
        Item = item;
    }

    public RefGachaItemSet Source { get; }
    public RefObjItem Item { get; }

    public string Name => Item.GetRealName(true);
    public string CodeName => Item.CodeName;
    public int Degree => Item.Degree;
    public string Rarity => Item.GetRarityName();
    public string EquipmentKey => ItemCategoryRules.GetEquipmentKey(Item);

    public string RarityFilterName
    {
        get
        {
            if (Item.ItemClass is >= 31 and <= 34)
                return "Seal of Nova";
            if (CodeName.EndsWith("_A_RARE", System.StringComparison.OrdinalIgnoreCase))
                return "Seal of Star";
            if (CodeName.EndsWith("_B_RARE", System.StringComparison.OrdinalIgnoreCase))
                return "Seal of Moon";
            if (CodeName.EndsWith("_C_RARE", System.StringComparison.OrdinalIgnoreCase))
                return "Seal of Sun";
            return string.Empty;
        }
    }

    public string Race
    {
        get
        {
            var parts = EquipmentKey?.Split('.');
            if (parts == null || parts.Length < 2)
                return string.Empty;

            return parts[1] switch
            {
                "Chinese" => "CH",
                "European" => "EU",
                _ => string.Empty
            };
        }
    }

    public string Category
    {
        get
        {
            var key = EquipmentKey;
            if (string.IsNullOrEmpty(key))
                return "Unsupported";

            var parts = key.Split('.');
            if (parts[0] == "Clothes" && parts.Length >= 3)
            {
                return parts[2] switch
                {
                    "HeavyArmor" => "Heavy Armor",
                    "LightArmor" => "Light Armor",
                    _ => parts[2]
                };
            }

            return parts[0];
        }
    }

    public string Subtype
    {
        get
        {
            var key = EquipmentKey;
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            var parts = key.Split('.');
            return parts.Length == 0 ? string.Empty : parts[^1];
        }
    }
}
