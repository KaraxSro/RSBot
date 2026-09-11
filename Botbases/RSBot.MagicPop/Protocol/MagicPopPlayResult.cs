namespace RSBot.MagicPop.Protocol;

internal enum MagicPopOutcome
{
    Lose,
    Win
}

internal sealed class MagicPopPlayResult
{
    public MagicPopPlayResult(MagicPopOutcome outcome)
    {
        Outcome = outcome;
    }

    public MagicPopOutcome Outcome { get; }
}
