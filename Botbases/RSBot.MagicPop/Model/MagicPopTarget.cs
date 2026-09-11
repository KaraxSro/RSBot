using RSBot.MagicPop.References;

namespace RSBot.MagicPop.Model;

internal sealed class MagicPopTarget
{
    public MagicPopTarget(GachaReward reward)
    {
        GachaId = reward.Source.GachaId;
        RefItemId = reward.Source.RefItemId;
        CodeName = reward.CodeName;
        DisplayName = reward.Name;
    }

    public uint GachaId { get; }
    public uint RefItemId { get; }
    public string CodeName { get; }
    public string DisplayName { get; }
}
