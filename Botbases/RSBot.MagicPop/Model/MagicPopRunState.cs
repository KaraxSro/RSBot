namespace RSBot.MagicPop.Model;

internal enum MagicPopRunState
{
    Stopped,
    Validating,
    ReturningToHotan,
    BuyingCards,
    MovingToMachine,
    OpeningMachine,
    Rolling,
    WaitingForRollResult,
    MovingToPotionShop,
    SellingLosingCoupons,
    ReturningToMachine,
    Completed,
    Faulted
}
