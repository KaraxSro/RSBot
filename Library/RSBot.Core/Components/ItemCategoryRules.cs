using System;
using System.Collections.Generic;
using System.Linq;
using RSBot.Core.Client.ReferenceObjects;

namespace RSBot.Core.Components;

/// <summary>
/// Category based pickup, store and automatic equipment selling rules.
/// These rules intentionally live outside the legacy per-item filters.
/// </summary>
public static class ItemCategoryRules
{
    private const string PickupCategoriesKey = "RSBot.Items.Categories.Pickup";
    private const string StoreCategoriesKey = "RSBot.Items.Categories.Store";
    private const string PickupRareRulesKey = "RSBot.Items.Rare.Pickup";
    private const string StoreRareRulesKey = "RSBot.Items.Rare.Store";
    private const string StoreRareKey = "RSBot.Items.Store.Rare";
    private const string StoreRareDegreesKey = "RSBot.Items.Store.Rare.Degrees";
    private const string StoreEquipmentDegreesKey = "RSBot.Items.Store.Equipment.Degrees";
    private const string SellUnselectedEquipmentKey = "RSBot.Items.Store.Equipment.SellUnselected";
    private const string StoreMaleClothesKey = "RSBot.Items.Store.Clothes.Male";
    private const string StoreFemaleClothesKey = "RSBot.Items.Store.Clothes.Female";

    private static readonly object SyncRoot = new();
    private static HashSet<string> _pickupCategories = new(StringComparer.Ordinal);
    private static HashSet<string> _storeCategories = new(StringComparer.Ordinal);
    private static HashSet<string> _pickupRareRules = CreateAllRareRules();
    private static HashSet<string> _storeRareRules = new(StringComparer.Ordinal);
    private static HashSet<int> _storeEquipmentDegrees = new(Enumerable.Range(1, 12));
    private static bool _sellUnselectedEquipment = true;
    private static bool _storeMaleClothes = true;
    private static bool _storeFemaleClothes = true;

    public static bool StoreMaleClothes
    {
        get
        {
            lock (SyncRoot)
                return _storeMaleClothes;
        }
    }

    public static bool SellUnselectedEquipment
    {
        get
        {
            lock (SyncRoot)
                return _sellUnselectedEquipment;
        }
    }

    public static string[] PickupRareRules
    {
        get
        {
            lock (SyncRoot)
                return _pickupRareRules.ToArray();
        }
    }

    public static string[] StoreRareRules
    {
        get
        {
            lock (SyncRoot)
                return _storeRareRules.ToArray();
        }
    }

    public static int[] StoreEquipmentDegrees
    {
        get
        {
            lock (SyncRoot)
                return _storeEquipmentDegrees.ToArray();
        }
    }

    public static bool StoreFemaleClothes
    {
        get
        {
            lock (SyncRoot)
                return _storeFemaleClothes;
        }
    }

    public static string[] PickupCategories
    {
        get
        {
            lock (SyncRoot)
                return _pickupCategories.ToArray();
        }
    }

    public static string[] StoreCategories
    {
        get
        {
            lock (SyncRoot)
                return _storeCategories.ToArray();
        }
    }

    public static void Load()
    {
        lock (SyncRoot)
        {
            _pickupCategories = new HashSet<string>(
                PlayerConfig.GetArray<string>(PickupCategoriesKey),
                StringComparer.Ordinal
            );
            _storeCategories = new HashSet<string>(
                PlayerConfig.GetArray<string>(StoreCategoriesKey),
                StringComparer.Ordinal
            );
            _pickupRareRules = LoadPickupRareRules();
            _storeRareRules = LoadStoreRareRules();
            _storeEquipmentDegrees = LoadDegrees(StoreEquipmentDegreesKey);
            _sellUnselectedEquipment = PlayerConfig.Get(SellUnselectedEquipmentKey, true);
            _storeMaleClothes = PlayerConfig.Get(StoreMaleClothesKey, true);
            _storeFemaleClothes = PlayerConfig.Get(StoreFemaleClothesKey, true);
        }
    }

    public static void Save(
        IEnumerable<string> pickupCategories,
        IEnumerable<string> storeCategories,
        IEnumerable<string> pickupRareRules,
        IEnumerable<string> storeRareRules,
        IEnumerable<int> storeEquipmentDegrees,
        bool sellUnselectedEquipment,
        bool storeMaleClothes,
        bool storeFemaleClothes
    )
    {
        lock (SyncRoot)
        {
            _pickupCategories = new HashSet<string>(pickupCategories ?? Array.Empty<string>(), StringComparer.Ordinal);
            _storeCategories = new HashSet<string>(storeCategories ?? Array.Empty<string>(), StringComparer.Ordinal);
            _pickupRareRules = NormalizeRareRules(pickupRareRules);
            _storeRareRules = NormalizeRareRules(storeRareRules);
            _storeEquipmentDegrees = NormalizeDegrees(storeEquipmentDegrees);
            _sellUnselectedEquipment = sellUnselectedEquipment;
            _storeMaleClothes = storeMaleClothes;
            _storeFemaleClothes = storeFemaleClothes;

            PlayerConfig.SetArray(PickupCategoriesKey, _pickupCategories.OrderBy(value => value));
            PlayerConfig.SetArray(StoreCategoriesKey, _storeCategories.OrderBy(value => value));
            PlayerConfig.SetArray(PickupRareRulesKey, _pickupRareRules.OrderBy(value => value));
            PlayerConfig.SetArray(StoreRareRulesKey, _storeRareRules.OrderBy(value => value));
            PlayerConfig.SetArray(StoreEquipmentDegreesKey, _storeEquipmentDegrees.OrderBy(value => value));
            PlayerConfig.Set(SellUnselectedEquipmentKey, _sellUnselectedEquipment);
            PlayerConfig.Set(StoreMaleClothesKey, _storeMaleClothes);
            PlayerConfig.Set(StoreFemaleClothesKey, _storeFemaleClothes);
        }
    }

    public static bool ShouldPickup(RefObjItem item)
    {
        var rareKey = GetRareRuleKey(item);
        if (rareKey != null)
        {
            lock (SyncRoot)
                return _pickupRareRules.Contains(rareKey);
        }

        var key = GetSupplyKey(item);
        if (key == null)
            return false;

        lock (SyncRoot)
            return _pickupCategories.Contains(key);
    }

    public static bool ShouldStore(RefObjItem item)
    {
        if (item == null)
            return false;

        lock (SyncRoot)
        {
            var rareKey = GetRareRuleKey(item);
            if (rareKey != null && _storeRareRules.Contains(rareKey))
                return true;

            var equipmentKey = GetEquipmentKey(item);
            if (
                equipmentKey != null
                && _storeEquipmentDegrees.Contains(item.Degree)
            )
            {
                var hasEquipmentTypeFilter = _storeCategories.Any(IsEquipmentCategoryKey);
                if (hasEquipmentTypeFilter && !_storeCategories.Contains(equipmentKey))
                    return false;

                if (!IsClothes(item))
                    return true;

                // Gender is an optional additional clothes filter. With neither
                // gender selected it imposes no restriction; with one selected,
                // only that gender matches.
                if (!_storeMaleClothes && !_storeFemaleClothes)
                    return true;

                return item.ReqGender switch
                {
                    (byte)ObjectGender.Male => _storeMaleClothes,
                    (byte)ObjectGender.Female => _storeFemaleClothes,
                    _ => _storeMaleClothes && _storeFemaleClothes,
                };
            }

            var supplyKey = GetSupplyKey(item);
            return supplyKey != null && _storeCategories.Contains(supplyKey);
        }
    }

    /// <summary>
    /// Clothes, accessories, weapons and shields participate in "store selected, sell the rest".
    /// </summary>
    public static bool IsManagedEquipment(RefObjItem item) => GetEquipmentKey(item) != null;

    public static string GetEquipmentKey(RefObjItem item)
    {
        if (item == null || !item.IsEquip)
            return null;

        if (IsClothes(item))
        {
            var country = item.TypeID3 <= 3 ? "Chinese" : "European";
            var clothesType = item.TypeID3 switch
            {
                1 => "Armor",
                2 => "Protector",
                3 => "Garment",
                9 => "HeavyArmor",
                10 => "LightArmor",
                11 => "Robe",
                _ => null,
            };
            var slot = item.TypeID4 switch
            {
                1 => "Head",
                2 => "Shoulder",
                3 => "Chest",
                4 => "Pants",
                5 => "Bracer",
                6 => "Boots",
                _ => null,
            };

            return clothesType == null || slot == null ? null : $"Clothes.{country}.{clothesType}.{slot}";
        }

        if (item.TypeID3 is 5 or 12)
        {
            var country = item.TypeID3 == 5 ? "Chinese" : "European";
            var accessory = item.TypeID4 switch
            {
                1 => "Earring",
                2 => "Necklace",
                3 => "Ring",
                _ => null,
            };

            return accessory == null ? null : $"Accessory.{country}.{accessory}";
        }

        if (item.TypeID3 == 4)
        {
            var country = item.TypeID4 switch
            {
                1 => "Chinese",
                2 => "European",
                _ => null,
            };
            return country == null ? null : $"Weapon.{country}.Shield";
        }

        if (item.TypeID3 != 6)
            return null;

        var weapon = item.TypeID4 switch
        {
            2 => ("Chinese", "Sword"),
            3 => ("Chinese", "Blade"),
            4 => ("Chinese", "Spear"),
            5 => ("Chinese", "Glaive"),
            6 => ("Chinese", "Bow"),
            7 => ("European", "Sword"),
            8 => ("European", "TwoHandedSword"),
            9 => ("European", "Axe"),
            10 => ("European", "WarlockRod"),
            11 => ("European", "Staff"),
            12 => ("European", "Crossbow"),
            13 => ("European", "Dagger"),
            14 => ("European", "Harp"),
            15 => ("European", "ClericRod"),
            _ => ((string Country, string Type)?)null,
        };

        return weapon == null ? null : $"Weapon.{weapon.Value.Country}.{weapon.Value.Type}";
    }

    public static string GetSupplyKey(RefObjItem item)
    {
        if (item == null)
            return null;

        if (item.IsAmmunition)
        {
            return item.TypeID4 switch
            {
                1 => "Supply.Arrow",
                2 => "Supply.Bolt",
                _ => null,
            };
        }

        var elixirType = GetElixirType(item);
        if (elixirType != null)
            return $"Supply.Elixir.{elixirType}";

        if (GetPotionItemOrder(item) >= 0)
            return string.IsNullOrWhiteSpace(item.CodeName) ? null : $"Supply.Item.{item.CodeName}";

        return null;
    }

    /// <summary>
    /// Returns the fixed display/rule order for supported recovery items.
    /// Type IDs select the item family; code names select the exact supported variant.
    /// </summary>
    public static int GetPotionItemOrder(RefObjItem item)
    {
        if (item?.CodeName == null)
            return -1;

        var codeName = item.CodeName;
        if (item.IsHpPotion)
            return GetRecoveryPotionOrder(codeName, "HP");
        if (item.IsMpPotion)
            return GetRecoveryPotionOrder(codeName, "MP");
        if (item.IsAllPotion)
            return GetRecoveryPotionOrder(codeName, "ALL");

        if (item.IsUniversalPill)
            return GetNumberedCodeNameOrder(codeName, "ITEM_ETC_CURE_ALL_", 1, 6, 0);
        if (item.IsPurificationPill)
            return GetNumberedCodeNameOrder(codeName, "ITEM_ETC_CURE_RANDOM_", 1, 4, 6);

        // In particular, TypeID4 == 9 abnormal-state potions are intentionally excluded.
        return -1;
    }

    public static string GetSupplyGroup(RefObjItem item)
    {
        if (item == null)
            return null;
        if (item.IsHpPotion)
            return "HP";
        if (item.IsMpPotion)
            return "MP";
        if (item.IsAllPotion)
            return "Vigor";
        if (item.IsUniversalPill || item.IsPurificationPill || item.IsAbnormalPotion)
            return "Pill";
        if (GetElixirType(item) != null)
            return "Elixirs";
        if (item.IsAmmunition)
            return "Arrow / Bolt";
        return null;
    }

    public static string GetRareRuleKey(RefObjItem item)
    {
        if (item?.CodeName == null)
            return null;

        var codeName = item.CodeName;
        if (
            codeName.StartsWith("ITEM_ROC_", StringComparison.OrdinalIgnoreCase)
            && codeName.EndsWith("_SET", StringComparison.OrdinalIgnoreCase)
        )
            return "RocSet";

        if (item.Degree == 11)
        {
            if (codeName.EndsWith("_SET_A_RARE", StringComparison.OrdinalIgnoreCase))
                return "NovaSetA";
            if (codeName.EndsWith("_SET_B_RARE", StringComparison.OrdinalIgnoreCase))
                return "NovaSetB";
            if (codeName.EndsWith("_A_RARE", StringComparison.OrdinalIgnoreCase))
                return "Nova";
            return null;
        }

        if (item.Degree is < 1 or > 10)
            return null;

        if (codeName.EndsWith("_A_RARE", StringComparison.OrdinalIgnoreCase))
            return $"Star.{item.Degree}";
        if (codeName.EndsWith("_B_RARE", StringComparison.OrdinalIgnoreCase))
            return $"Moon.{item.Degree}";
        if (codeName.EndsWith("_C_RARE", StringComparison.OrdinalIgnoreCase))
            return $"Sun.{item.Degree}";

        return null;
    }

    private static bool IsClothes(RefObjItem item) => item.TypeID3 is 1 or 2 or 3 or 9 or 10 or 11;

    private static bool IsEquipmentCategoryKey(string key)
    {
        return key != null
            && (key.StartsWith("Clothes.", StringComparison.Ordinal)
                || key.StartsWith("Accessory.", StringComparison.Ordinal)
                || key.StartsWith("Weapon.", StringComparison.Ordinal));
    }

    private static HashSet<int> LoadDegrees(string key)
    {
        return PlayerConfig.Exists(key)
            ? NormalizeDegrees(PlayerConfig.GetArray<int>(key))
            : new HashSet<int>(Enumerable.Range(1, 12));
    }

    private static HashSet<int> NormalizeDegrees(IEnumerable<int> degrees)
    {
        return new HashSet<int>((degrees ?? Array.Empty<int>()).Where(degree => degree is >= 1 and <= 12));
    }

    private static HashSet<string> LoadPickupRareRules()
    {
        if (PlayerConfig.Exists(PickupRareRulesKey))
            return NormalizeRareRules(PlayerConfig.GetArray<string>(PickupRareRulesKey));

        return PlayerConfig.Get("RSBot.Items.Pickup.Rare", true)
            ? CreateAllRareRules()
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private static HashSet<string> LoadStoreRareRules()
    {
        if (PlayerConfig.Exists(StoreRareRulesKey))
            return NormalizeRareRules(PlayerConfig.GetArray<string>(StoreRareRulesKey));
        if (!PlayerConfig.Get(StoreRareKey, false))
            return new HashSet<string>(StringComparer.Ordinal);

        var degrees = LoadDegrees(StoreRareDegreesKey);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var degree in degrees.Where(degree => degree <= 10))
        {
            result.Add($"Star.{degree}");
            result.Add($"Moon.{degree}");
            result.Add($"Sun.{degree}");
        }

        if (degrees.Contains(10))
            result.Add("RocSet");
        if (degrees.Contains(11))
        {
            result.Add("Nova");
            result.Add("NovaSetA");
            result.Add("NovaSetB");
        }

        return result;
    }

    private static HashSet<string> NormalizeRareRules(IEnumerable<string> rules)
    {
        var validRules = CreateAllRareRules();
        return new HashSet<string>(
            (rules ?? Array.Empty<string>()).Where(validRules.Contains),
            StringComparer.Ordinal
        );
    }

    private static HashSet<string> CreateAllRareRules()
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { "RocSet", "Nova", "NovaSetA", "NovaSetB" };
        foreach (var degree in Enumerable.Range(1, 10))
        {
            result.Add($"Star.{degree}");
            result.Add($"Moon.{degree}");
            result.Add($"Sun.{degree}");
        }

        return result;
    }

    private static string GetElixirType(RefObjItem item)
    {
        if (item?.CodeName == null)
            return null;

        var codeName = item.CodeName;
        var isElixir = codeName.Contains("ELIXIR", StringComparison.OrdinalIgnoreCase)
            || codeName.StartsWith(
                "ITEM_ETC_ARCHEMY_REINFORCE_RECIPE_",
                StringComparison.OrdinalIgnoreCase
            );
        if (!isElixir)
            return null;

        if (codeName.Contains("ACCESSARY", StringComparison.OrdinalIgnoreCase)
            || codeName.Contains("ACCESSORY", StringComparison.OrdinalIgnoreCase))
            return "Accessory";
        if (codeName.Contains("SHIELD", StringComparison.OrdinalIgnoreCase))
            return "Shield";
        if (codeName.Contains("WEAPON", StringComparison.OrdinalIgnoreCase))
            return "Weapon";
        if (codeName.Contains("ARMOR", StringComparison.OrdinalIgnoreCase)
            || codeName.Contains("PROTECT", StringComparison.OrdinalIgnoreCase))
            return "Protector";

        return null;
    }

    private static int GetRecoveryPotionOrder(string codeName, string family)
    {
        if (codeName.Equals($"ITEM_ETC_{family}_SPOTION_01", StringComparison.OrdinalIgnoreCase))
            return 0;

        return GetNumberedCodeNameOrder(codeName, $"ITEM_ETC_{family}_POTION_", 1, 5, 1);
    }

    private static int GetNumberedCodeNameOrder(
        string codeName,
        string prefix,
        int firstNumber,
        int lastNumber,
        int firstOrder
    )
    {
        if (!codeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return -1;

        var suffix = codeName.Substring(prefix.Length);
        if (!int.TryParse(suffix, out var number) || number < firstNumber || number > lastNumber)
            return -1;

        return firstOrder + number - firstNumber;
    }
}
