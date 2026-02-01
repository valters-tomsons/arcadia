using System.Collections.Concurrent;
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
        "does anyone know where to get",
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
    
    private readonly string[] piracyPhrases =
    [
        "pkgi",
        "where to find pkg",
        "where to get pkg",
        "where to download game",
        "how to download game",
    ];

    private ConcurrentDictionary<SocketGuildUser, DateTimeOffset> _lastEmbeds = [];
    private static readonly TimeSpan _embedTreshhold = TimeSpan.FromMinutes(1);

    public async Task OnMessageReceived(SocketUserMessage msg)
    {
        if (msg.Author.IsBot) return;
        if (msg.Author is not SocketGuildUser usr) return;

        if (msg.Embeds.Count > 2)
        {
            if (_lastEmbeds.TryGetValue(usr, out var lastPosted))
            {
                var embedTimeDiff = DateTimeOffset.UtcNow - lastPosted;
                if (embedTimeDiff < _embedTreshhold)
                {
                    _lastEmbeds.Remove(usr, out _);
                    await DeleteImageSpam(msg, usr);
                    return;
                }
            }

            _lastEmbeds[usr] = DateTimeOffset.UtcNow;
        }

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

        foreach(var phrase in piracyPhrases)
        {
            if (content.Contains(phrase, StringComparison.InvariantCultureIgnoreCase))
            {
                await DeletePiracy(msg);
                return;
            }
        }
    }

    private async Task DeleteNonEnglish(SocketUserMessage msg)
    {
        const ulong nonEnglishChannelId = 1450610182995316969;

        _logger.LogInformation("[Moderation] Deleting non-english message: '{Content}'", msg.Content);

        try
        {
            await msg.ReplyAsync($"Read Rule #4, keep it english outside of <#{nonEnglishChannelId}>");
            await msg.DeleteAsync();
        }
        catch { }
    }

    private async Task DeleteIlliterate(SocketUserMessage msg)
    {
        const ulong infoChannelId = 1256693500901331044;

        _logger.LogInformation("[Moderation] Deleting stupid question: '{Content}'", msg.Content);

        try
        {
            await msg.ReplyAsync($"Read <#{infoChannelId}> in its entirety, it's already explained!");
            await msg.DeleteAsync();
        }
        catch { }
    }

    private async Task DeletePiracy(SocketUserMessage msg)
    {
        _logger.LogInformation("[Moderation] Deleting piracy related message: '{Content}'", msg.Content);

        try
        {
            await msg.ReplyAsync("Read Rule #2, no discussion of piracy!");
            await msg.DeleteAsync();
        }
        catch { }
    }

    private async Task DeleteImageSpam(SocketUserMessage msg, SocketGuildUser usr)
    {
        _logger.LogInformation("[Moderation] Detected spam from {Username} ({UserId})", msg.Author.Username, msg.Author.Id);

        try
        {
            await msg.ReplyAsync("Banned for spam. Have a nice day! 👋");
        }
        catch { }

        try
        {
            await usr.BanAsync(pruneDays: 2, "Spam");
        }
        catch { }
    }
}