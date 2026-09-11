using System.Collections.Generic;

namespace RSBot.MagicPop.Validation;

internal sealed class MagicPopValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Information { get; } = new();
    public bool IsValid => Errors.Count == 0;
}
