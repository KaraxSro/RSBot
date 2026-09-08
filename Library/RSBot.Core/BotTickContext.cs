namespace RSBot.Core;

/// <summary>
///     Provides bot tick subscribers with a way to postpone the current botbase tick.
/// </summary>
public sealed class BotTickContext
{
    /// <summary>
    ///     Gets or sets a value indicating whether the current botbase tick should be skipped.
    /// </summary>
    public bool Cancel { get; set; }
}
