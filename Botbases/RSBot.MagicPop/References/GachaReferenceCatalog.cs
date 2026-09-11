using System;
using System.Collections.Generic;
using System.Linq;
using RSBot.Core;

namespace RSBot.MagicPop.References;

internal sealed class GachaReferenceCatalog
{
    private const string ItemSetPath = "server_dep\\silkroad\\textdata\\gachaitemset.txt";
    private const string NpcMapPath = "server_dep\\silkroad\\textdata\\gachanpcmap.txt";
    private const string PackageItemPath = "server_dep\\silkroad\\textdata\\RefPackageItem.txt";
    private const string CardPackageCodeName = "PACKAGE_ITEM_MALL_GACHA_CARD";
    private const string CardItemCodeName = "ITEM_MALL_GACHA_CARD";

    private readonly object _syncRoot = new();
    private IReadOnlyList<RefGachaItemSet> _itemSets = Array.Empty<RefGachaItemSet>();
    private IReadOnlyList<RefGachaNpcMap> _npcMaps = Array.Empty<RefGachaNpcMap>();
    private IReadOnlyList<GachaReward> _rewards = Array.Empty<GachaReward>();

    public bool IsLoaded { get; private set; }
    public bool IsValid { get; private set; }
    public string Error { get; private set; }
    public MagicPopCardPackage CardPackage { get; private set; }
    public string CardPackageError { get; private set; }
    public string LoadDetails { get; private set; }
    public IReadOnlyList<RefGachaItemSet> ItemSets => _itemSets;
    public IReadOnlyList<RefGachaNpcMap> NpcMaps => _npcMaps;
    public IReadOnlyList<GachaReward> Rewards => _rewards;

    public bool Load()
    {
        lock (_syncRoot)
        {
            Reset();

            if (Game.MediaPk2 == null || Game.ReferenceManager == null)
                return Fail("Game reference data is not available yet.");

            if (!TryReadLines(ItemSetPath, out var itemSetLines, out var readError))
                return Fail(readError);

            if (!TryReadLines(NpcMapPath, out var npcMapLines, out readError))
                return Fail(readError);

            var errors = new List<string>();
            var itemSets = ParseLines<RefGachaItemSet>(
                itemSetLines,
                RefGachaItemSet.TryParse,
                "gachaitemset.txt",
                errors
            );
            var npcMaps = ParseLines<RefGachaNpcMap>(
                npcMapLines,
                RefGachaNpcMap.TryParse,
                "gachanpcmap.txt",
                errors
            );

            ValidateItemSets(itemSets, errors);
            ValidateNpcMaps(npcMaps, itemSets, errors);
            CardPackage = ResolveCardPackage(out var cardPackageError);
            CardPackageError = cardPackageError;
            if (cardPackageError != null)
                Log.Warn($"[Magic POP] Card purchase is unavailable. {cardPackageError}");
            else
            {
                Log.Debug(
                    $"[Magic POP] Card package resolved: tab={CardPackage.ShopTab.CodeName}, wireShopID=0x{CardPackage.EncodedShopTabId:X8}, slot={CardPackage.ShopGood.SlotIndex}, packageID=0x{CardPackage.PackageId:X8}."
                );
            }

            var mappedSetIds = ResolveMappedSetIds(npcMaps).ToHashSet();
            var enabledEntries = itemSets.Where(entry => entry.Service == 1).ToArray();
            var mappedEntries = enabledEntries.Where(entry => mappedSetIds.Contains(entry.SetId)).ToArray();
            var rewards = mappedEntries
                .Where(entry => Game.ReferenceManager.ItemData.ContainsKey(entry.RefItemId))
                .Select(entry => new GachaReward(entry, Game.ReferenceManager.ItemData[entry.RefItemId]))
                .Where(reward => reward.Category != "Unsupported")
                .ToArray();

            LoadDetails =
                $"Parsed {itemSets.Count} item-set row(s) and {npcMaps.Count} NPC-map row(s); " +
                $"enabled={enabledEntries.Length}, Visible=0:{enabledEntries.Count(entry => entry.Visible == 0)}, " +
                $"Visible=1:{enabledEntries.Count(entry => entry.Visible == 1)}, " +
                $"mapped sets=[{string.Join(", ", mappedSetIds.OrderBy(value => value))}], " +
                $"mapped entries={mappedEntries.Length}, supported equipment rewards={rewards.Length}.";
            Log.Debug($"[Magic POP] {LoadDetails}");

            if (rewards.Length == 0)
                errors.Add("No enabled Magic POP reward records could be linked to ItemData.");

            _itemSets = itemSets;
            _npcMaps = npcMaps;
            _rewards = rewards;
            IsLoaded = true;

            if (errors.Count != 0)
                return Fail(FormatErrors(errors));

            IsValid = true;
            Error = null;
            Log.Notify($"[Magic POP] Loaded {rewards.Length} rewards from {itemSets.Count} item-set and {npcMaps.Count} NPC-map records.");
            return true;
        }
    }

    private static IEnumerable<uint> ResolveMappedSetIds(IReadOnlyList<RefGachaNpcMap> npcMaps)
    {
        foreach (var map in npcMaps.Where(entry => entry.Service == 1))
        {
            if (!Game.ReferenceManager.CharacterData.TryGetValue(map.RefNpcId, out var npc)
                || !string.Equals(npc.CodeName, "NPC_CH_GACHA_MACHINE", StringComparison.OrdinalIgnoreCase))
                continue;

            for (var setId = map.FirstSetId; setId <= map.LastSetId; setId++)
            {
                yield return setId;
                if (setId == uint.MaxValue)
                    break;
            }
        }
    }

    private void ValidateItemSets(IReadOnlyList<RefGachaItemSet> itemSets, ICollection<string> errors)
    {
        foreach (var entry in itemSets.Where(entry => entry.Service == 1))
        {
            if (!Game.ReferenceManager.ItemData.ContainsKey(entry.RefItemId))
                errors.Add($"GachaID {entry.GachaId} refers to missing ItemData RefItemID {entry.RefItemId}.");
        }

        foreach (var duplicate in itemSets
                     .Where(entry => entry.Service == 1)
                     .GroupBy(entry => (entry.SetId, entry.GachaId))
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate enabled GachaID {duplicate.Key.GachaId} in Set_ID {duplicate.Key.SetId}.");
        }
    }

    private static MagicPopCardPackage ResolveCardPackage(out string error)
    {
        error = null;
        if (!Game.ReferenceManager.PackageItemScrap.TryGetValue(CardPackageCodeName, out var package))
        {
            error = $"Cash-shop package '{CardPackageCodeName}' is missing from RefScrapOfPackageItem.";
            return null;
        }

        if (!string.Equals(package.RefItemCodeName, CardItemCodeName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Cash-shop package '{CardPackageCodeName}' resolves to unexpected item '{package.RefItemCodeName}'.";
            return null;
        }

        if (package.RefItem == null)
        {
            error = $"Cash-shop package '{CardPackageCodeName}' refers to missing item '{CardItemCodeName}'.";
            return null;
        }

        if (!TryResolvePackageId(CardPackageCodeName, out var packageId, out error))
            return null;

        var goods = Game.ReferenceManager.ShopGoods
            .Where(candidate => string.Equals(candidate.RefPackageItemCodeName, CardPackageCodeName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (goods.Length == 0)
        {
            error = $"Cash-shop package '{CardPackageCodeName}' has no RefShopGoods entry.";
            return null;
        }

        if (!TryResolveCashShopLocation(CardPackageCodeName, out var good, out var tab, out var encodedShopTabId, out error))
            return null;

        return new MagicPopCardPackage(package, good, tab, goods, encodedShopTabId, packageId);
    }

    private static bool TryResolvePackageId(string packageCodeName, out int packageId, out string error)
    {
        packageId = 0;
        error = null;
        if (!TryReadLines(PackageItemPath, out var lines, out error))
            return false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var columns = line.TrimEnd('\r').Split('\t');
            if (columns.Length < 4
                || columns[0] != "1"
                || !string.Equals(columns[3], packageCodeName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(columns[2], out packageId) || packageId <= 0)
            {
                error = $"Cash-shop package '{packageCodeName}' has invalid RefPackageItem ID '{columns[2]}'.";
                return false;
            }

            return true;
        }

        error = $"Cash-shop package '{packageCodeName}' is missing from RefPackageItem.txt.";
        return false;
    }

    private static bool TryResolveCashShopLocation(
        string packageCodeName,
        out RSBot.Core.Client.ReferenceObjects.RefShopGood resolvedGood,
        out RSBot.Core.Client.ReferenceObjects.RefShopTab resolvedTab,
        out int encodedShopTabId,
        out string error)
    {
        resolvedGood = null;
        resolvedTab = null;
        encodedShopTabId = 0;
        error = null;

        foreach (var group in Game.ReferenceManager.ShopGroups.Values)
        {
            if (group.Id <= 0 || group.Id > ushort.MaxValue)
                continue;

            var shops = group.GetShops();
            for (var shopIndex = 0; shopIndex < shops.Count && shopIndex <= byte.MaxValue; shopIndex++)
            {
                var shop = shops[shopIndex];
                if (shop == null)
                    continue;

                var tabs = shop.GetTabs();
                for (var tabIndex = 0; tabIndex < tabs.Count && tabIndex <= byte.MaxValue; tabIndex++)
                {
                    var tab = tabs[tabIndex];
                    if (tab == null)
                        continue;

                    var good = tab.GetGoods().FirstOrDefault(candidate =>
                        string.Equals(candidate.RefPackageItemCodeName, packageCodeName, StringComparison.OrdinalIgnoreCase));
                    if (good == null)
                        continue;

                    resolvedGood = good;
                    resolvedTab = tab;
                    encodedShopTabId = (group.Id & 0xFFFF) | (shopIndex << 16) | (tabIndex << 24);
                    return true;
                }
            }
        }

        error = $"Cash-shop package '{packageCodeName}' has no resolvable group/shop/tab mapping.";
        return false;
    }

    private void ValidateNpcMaps(
        IReadOnlyList<RefGachaNpcMap> npcMaps,
        IReadOnlyList<RefGachaItemSet> itemSets,
        ICollection<string> errors)
    {
        foreach (var entry in npcMaps.Where(entry => entry.Service == 1))
        {
            if (!Game.ReferenceManager.CharacterData.TryGetValue(entry.RefNpcId, out var npc))
            {
                errors.Add($"Gacha NPC map refers to missing CharacterData RefNPCID {entry.RefNpcId}.");
                continue;
            }

            if (!string.Equals(npc.CodeName, "NPC_CH_GACHA_MACHINE", StringComparison.OrdinalIgnoreCase))
                errors.Add($"RefNPCID {entry.RefNpcId} resolves to unexpected NPC '{npc.CodeName}'.");

            for (var setId = entry.FirstSetId; setId <= entry.LastSetId; setId++)
            {
                if (!itemSets.Any(itemSet => itemSet.Service == 1 && itemSet.SetId == setId))
                    errors.Add($"Gacha NPC RefNPCID {entry.RefNpcId} refers to missing Set_ID {setId}.");

                if (setId == uint.MaxValue)
                    break;
            }
        }

    }

    private static IReadOnlyList<T> ParseLines<T>(
        IEnumerable<string> lines,
        TryParseLine<T> parser,
        string source,
        ICollection<string> errors)
    {
        var result = new List<T>();
        var lineNumber = 0;

        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (parser(line, out var value, out var error))
                result.Add(value);
            else
                errors.Add($"{source}:{lineNumber}: {error}");
        }

        return result;
    }

    private static bool TryReadLines(string path, out string[] lines, out string error)
    {
        lines = null;
        error = null;

        if (!Game.MediaPk2.TryGetFile(path, out var file) || file == null)
        {
            error = $"Required Magic POP reference file is missing: {path}";
            return false;
        }

        try
        {
            lines = file.ReadAllText().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return true;
        }
        catch (Exception exception)
        {
            error = $"Failed to read {path}: {exception.Message}";
            return false;
        }
    }

    private bool Fail(string error)
    {
        IsLoaded = true;
        IsValid = false;
        Error = error;
        Log.Error($"[Magic POP] Reference data is invalid. {error}");
        return false;
    }

    private void Reset()
    {
        IsLoaded = false;
        IsValid = false;
        Error = null;
        _itemSets = Array.Empty<RefGachaItemSet>();
        _npcMaps = Array.Empty<RefGachaNpcMap>();
        _rewards = Array.Empty<GachaReward>();
        CardPackage = null;
        CardPackageError = null;
        LoadDetails = null;
    }

    private static string FormatErrors(IReadOnlyCollection<string> errors)
    {
        const int displayedErrorLimit = 10;
        var displayed = errors.Take(displayedErrorLimit).ToArray();
        var suffix = errors.Count > displayedErrorLimit
            ? $" Additional errors: {errors.Count - displayedErrorLimit}."
            : string.Empty;
        return string.Join(" ", displayed) + suffix;
    }

    private delegate bool TryParseLine<T>(string line, out T value, out string error);
}
