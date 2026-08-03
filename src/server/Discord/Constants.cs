using Discord;

namespace Arcadia.Discord;

public static class Constants
{
    public readonly static RequestOptions ReqOptions = new()
    {
        RetryMode = RetryMode.AlwaysRetry
    };

    public enum GameRole { BF1943, BFBC2, MERCS2, MOHA, LOTRQ }

    public sealed record NotificationRole(GameRole Id, string DisplayName, string? Emote);

    public static readonly NotificationRole[] Roles =
    [
        new(GameRole.BF1943,   "Battlefield 1943",   "🏖️"),
        new(GameRole.BFBC2,    "Bad Company 2",      "🪖"),
        new(GameRole.MERCS2,   "Mercenaries 2",      "💵"),
        new(GameRole.MOHA,     "Medal of Honor",     "🎖️"),
        new(GameRole.LOTRQ,    "Lord of the Rings",  "💍"),
    ];
}