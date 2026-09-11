using RSBot.MagicPop.Protocol;

namespace RSBot.MagicPop.Model;

internal sealed class MagicPopRollCompletion
{
    public MagicPopRollCompletion(MagicPopOperation operation, MagicPopOutcome outcome)
    {
        Operation = operation;
        Outcome = outcome;
    }

    public MagicPopOperation Operation { get; }
    public MagicPopOutcome Outcome { get; }
}
