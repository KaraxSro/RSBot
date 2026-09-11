using System.Collections.Generic;
using RSBot.Core.Client.ReferenceObjects;

namespace RSBot.MagicPop.References;

internal sealed class MagicPopCardPackage
{
    public MagicPopCardPackage(
        RefPackageItemScrap package,
        RefShopGood shopGood,
        RefShopTab shopTab,
        IReadOnlyList<RefShopGood> shopGoods,
        int encodedShopTabId,
        int packageId)
    {
        Package = package;
        ShopGood = shopGood;
        ShopTab = shopTab;
        ShopGoods = shopGoods;
        EncodedShopTabId = encodedShopTabId;
        PackageId = packageId;
    }

    public RefPackageItemScrap Package { get; }
    public RefShopGood ShopGood { get; }
    public RefShopTab ShopTab { get; }
    public IReadOnlyList<RefShopGood> ShopGoods { get; }
    public int EncodedShopTabId { get; }
    public int PackageId { get; }
}
