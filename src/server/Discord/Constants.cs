using Discord;

namespace Arcadia.Discord;

public static class Constants
{
    public readonly static RequestOptions ReqOptions = new()
    {
        RetryMode = RetryMode.AlwaysRetry
    };

    // Names must match the last segment of a server's PartitionId
    public enum GameId { BEACH, BFBC2, AO3, MERCS2, LOTR, MOHAIR, CNCRA3 }

    public sealed record NotificationRole(GameId Id, string DisplayName, string? Emote);

    public static readonly NotificationRole[] Roles =
    [
        new(GameId.BEACH,    "Battlefield 1943",   "🏖️"),
        new(GameId.BFBC2,    "Bad Company 2",      "🪖"),
        new(GameId.MERCS2,   "Mercenaries 2",      "💵"),
        new(GameId.MOHAIR,   "Medal of Honor",     "🎖️"),
        new(GameId.LOTR,     "Lord of the Rings",  "💍"),
    ];
}