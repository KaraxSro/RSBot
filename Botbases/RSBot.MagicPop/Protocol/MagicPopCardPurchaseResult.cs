namespace RSBot.MagicPop.Protocol;

internal sealed class MagicPopCardPurchaseResult
{
    public MagicPopCardPurchaseResult(byte[] destinationSlots, ushort quantity)
    {
        DestinationSlots = destinationSlots;
        Quantity = quantity;
    }

    public byte[] DestinationSlots { get; }
    public ushort Quantity { get; }
}
