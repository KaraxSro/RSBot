using System.Collections.Generic;
using System.IO;
using System.Linq;
using RSBot.Core.Components;
using RSBot.Core.Event;

namespace RSBot.Core;

public static class PlayerConfig
{
    /// <summary>
    ///     The config
    /// </summary>
    private static Config _config;

    /// <summary>
    ///     The config directory
    /// </summary>
    private static string _configDirectory => Path.Combine(Kernel.BasePath, "User", ProfileManager.SelectedProfile);

    /// <summary>
    ///     Load config from file
    /// </summary>
    /// <param name="file">The config file path</param>
    public static void Load(string charName)
    {
        var configPath = Path.Combine(_configDirectory, charName + ".rs");
        var isNewConfig = !File.Exists(configPath) || new FileInfo(configPath).Length == 0;

        _config = new Config(configPath);

        if (isNewConfig)
        {
            ApplyNewCharacterDefaults();
            _config.Save();
        }

        Log.Notify("[Player] settings have been loaded!");
    }

    /// <summary>
    ///     Applies the initial settings only when a character has no saved configuration yet.
    /// </summary>
    private static void ApplyNewCharacterDefaults()
    {
        const string training = "RSBot.Training.";
        Set(training + "checkUseMount", false);
        Set(training + "checkCastBuffs", true);
        Set(training + "checkUseSpeedDrug", false);
        Set(training + "checkBoxUseReverse", true);
        Set(training + "ReverseDestination", "Death");

        Set(training + "checkBerzerkMonsterAmount", true);
        Set(training + "numBerzerkMonsterAmount", 5);
        Set(training + "checkBerserkOnMonsterRarity", true);
        SetArray("RSBot.Avoidance.Berserk", new[] { "Giant" });

        Set(training + "checkBoxDimensionPillar", true);
        Set(training + "checkAttackWeakerFirst", true);
        Set(training + "checkDefendPetFirst", true);
        Set(training + "checkBoxDontFollowMobs", true);

        const string protection = "RSBot.Protection.";
        Set(protection + "checkUseHPPotionsPlayer", true);
        Set(protection + "numPlayerHPPotionMin", 60);
        Set(protection + "checkUseMPPotionsPlayer", true);
        Set(protection + "numPlayerMPPotionMin", 60);
        Set(protection + "checkUseVigorHP", true);
        Set(protection + "numPlayerHPVigorPotionMin", 50);
        Set(protection + "checkUseUniversalPills", true);

        Set(protection + "checkUsePetHP", true);
        Set(protection + "numPetMinHP", 80);
        Set(protection + "checkUseHGP", true);
        Set(protection + "numPetMinHGP", 90);
        Set(protection + "checkReviveAttackPet", true);
        Set(protection + "checkAutoSummonAttackPet", true);

        Set(protection + "checkDead", true);
        Set(protection + "numDeadTimeout", 5);
        Set(protection + "checkInventory", true);
        Set(protection + "checkFullPetInventory", true);
        Set(protection + "checkNoHPPotions", true);
        Set(protection + "numHPPotionsLeft", 15);
        Set(protection + "checkNoMPPotions", true);
        Set(protection + "numMPPotionsLeft", 15);
        Set(protection + "checkDurability", true);

        var rareRules = new List<string> { "RocSet", "Nova", "NovaSetA", "NovaSetB" };
        foreach (var degree in Enumerable.Range(1, 10))
        {
            rareRules.Add("Star." + degree);
            rareRules.Add("Moon." + degree);
            rareRules.Add("Sun." + degree);
        }

        SetArray("RSBot.Items.Rare.Pickup", rareRules);
        SetArray("RSBot.Items.Rare.Store", rareRules);
        Set("RSBot.Items.Pickup.AnyEquips", true);
        SetArray("RSBot.Items.Store.Equipment.Degrees", Enumerable.Range(1, 12));
        Set("RSBot.Items.Store.Clothes.Male", true);
        Set("RSBot.Items.Store.Clothes.Female", true);

        var elixirs = new List<string>
        {
            "Supply.Elixir.Weapon",
            "Supply.Elixir.Shield",
            "Supply.Elixir.Protector",
            "Supply.Elixir.Accessory",
        };

        var pickupSupplies = new List<string>(elixirs)
        {
            "Supply.Item.ITEM_ETC_HP_SPOTION_01",
            "Supply.Item.ITEM_ETC_MP_SPOTION_01",
            "Supply.Item.ITEM_ETC_ALL_SPOTION_01",
        };
        pickupSupplies.AddRange(
            Enumerable.Range(1, 5).Select(number => $"Supply.Item.ITEM_ETC_HP_POTION_{number:00}")
        );
        pickupSupplies.AddRange(
            Enumerable.Range(1, 5).Select(number => $"Supply.Item.ITEM_ETC_MP_POTION_{number:00}")
        );
        pickupSupplies.AddRange(
            Enumerable.Range(1, 5).Select(number => $"Supply.Item.ITEM_ETC_ALL_POTION_{number:00}")
        );
        pickupSupplies.AddRange(
            Enumerable.Range(1, 6).Select(number => $"Supply.Item.ITEM_ETC_CURE_ALL_{number:00}")
        );
        pickupSupplies.AddRange(
            Enumerable.Range(1, 4).Select(number => $"Supply.Item.ITEM_ETC_CURE_RANDOM_{number:00}")
        );

        SetArray("RSBot.Items.Categories.Pickup", pickupSupplies);
        SetArray("RSBot.Items.Categories.Store", elixirs);
    }

    /// <summary>
    ///     Existses the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns></returns>
    public static bool Exists(string key)
    {
        if (_config == null)
            return false;

        return _config.Exists(key);
    }

    /// <summary>
    ///     Gets the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public static T Get<T>(string key, T defaultValue = default)
    {
        if (_config == null)
            return defaultValue;

        return _config.Get(key, defaultValue);
    }

    /// <summary>
    ///     Gets the enum value with specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public static TEnum GetEnum<TEnum>(string key, TEnum defaultValue = default)
        where TEnum : struct
    {
        if (_config == null)
            return defaultValue;

        return _config.GetEnum(key, defaultValue);
    }

    /// <summary>
    ///     Sets the specified key inside the config.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public static void Set<T>(string key, T value)
    {
        if (_config != null)
            _config.Set(key, value);
    }

    /// <summary>
    ///     Gets the array.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="delimiter">The delimiter.</param>
    /// <returns></returns>
    public static T[] GetArray<T>(string key, char delimiter = ',')
    {
        if (_config == null)
            return new T[] { };

        return _config.GetArray<T>(key, delimiter);
    }

    /// <summary>
    ///     Gets the enum value with specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public static TEnum[] GetEnums<TEnum>(string key, char delimiter = ',')
        where TEnum : struct
    {
        if (_config == null)
            return new TEnum[] { };

        return _config.GetEnums<TEnum>(key, delimiter);
    }

    /// <summary>
    ///     Sets the array.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <param name="delimiter">The delimiter.</param>
    public static void SetArray<T>(string key, IEnumerable<T> values, string delimiter = ",")
    {
        if (_config != null)
            _config.SetArray(key, values, delimiter);
    }

    /// <summary>
    ///     Saves the specified file.
    /// </summary>
    /// <param name="file">The file.</param>
    public static void Save()
    {
        Save(true);
    }

    /// <summary>
    /// Saves the player configuration, optionally without the normal log and save event.
    /// </summary>
    public static void Save(bool notify)
    {
        if (_config == null)
            return;

        _config.Save();

        if (!notify)
            return;

        Log.Notify("[Player] have been saved!");
        EventManager.FireEvent("OnSavePlayerConfig");
    }
}
