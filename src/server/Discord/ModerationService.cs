using Discord;
using Discord.WebSocket;
using Lingua;
using Microsoft.Extensions.Logging;

namespace Arcadia.Discord;

public class ModerationService(ILogger<ModerationService> logger)
{
    private readonly ILogger<ModerationService> _logger = logger;
    private readonly LanguageDetector _detector = LanguageDetectorBuilder.FromLanguages(
        Language.English,
        Language.Spanish,
        Language.Russian
    ).Build();

    private readonly string[] stupidPhrases =
    [
        "what do I need to do to play online",
        "how to play online",
        "how do i play online",
        "how can i play online",
        "can we play online",

        "does it work",
        "does this work",
        "does online work",
        "does multiplayer work",
        "does coop work",
        "does co-op work",
        "does pvp work",

        "is it playable",
        "is online playable",
        "is multiplayer playable",
        "is pvp playable",
        "is coop playable",
        "playable online",
        "playable multiplayer",

        "can i play",
        "can we play",
        "are we able to play",

        "which modes work",
        "tutorial only?",
    ];

    public async Task OnMessageReceived(SocketUserMessage msg)
    {
        if (msg.Author.IsBot) return;
        if (msg.Author is not SocketGuildUser usr) return;

        if (DateTimeOffset.UtcNow.AddHours(-2) > usr.JoinedAt) return;

        var content = msg.Content.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length < 8) return;
        var lang = _detector.DetectLanguageOf(content);

        if (lang != Language.English && lang != Language.Unknown)
        {
            await DeleteNonEnglish(msg);
            return;
        }

        foreach (var phrase in stupidPhrases)
        {
            if (content.Contains(phrase, StringComparison.InvariantCultureIgnoreCase))
            {
                await DeleteIlliterate(msg);
                return;
            }
        }
    }

    private static async Task DeleteNonEnglish(SocketUserMessage msg)
    {
        const ulong nonEnglishChannelId = 1450610182995316969;

        try
        {
            await msg.ReplyAsync($"Read Rule #4, keep it english outside of <#{nonEnglishChannelId}>");
            await msg.DeleteAsync();
        }
        catch { }
    }

    private static async Task DeleteIlliterate(SocketUserMessage msg)
    {
        const ulong infoChannelId = 1256693500901331044;

        try
        {
            await msg.ReplyAsync($"Read <#{infoChannelId}> in its entirety, it's already explained!");
            await msg.DeleteAsync();
        }
        catch { }
    }
}