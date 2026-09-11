namespace RSBot.MagicPop.Protocol;

internal sealed class SilkBalance
{
    public SilkBalance(uint silk, uint secondBalance, uint thirdBalance)
    {
        Silk = silk;
        SecondBalance = secondBalance;
        ThirdBalance = thirdBalance;
    }

    public uint Silk { get; }
    public uint SecondBalance { get; }
    public uint ThirdBalance { get; }
}
