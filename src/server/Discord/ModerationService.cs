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

    public async Task OnMessageReceived(SocketUserMessage msg)
    {
        if (msg.Author.IsBot) return;
        if (msg.Author is not SocketGuildUser usr) return;

        if (DateTimeOffset.UtcNow.AddHours(-2) > usr.JoinedAt) return;

        var content = msg.Content.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length < 8) return;

        var lang = _detector.DetectLanguageOf(content);
        if (lang == Language.English || lang == Language.Unknown) return;

        try
        {
            const ulong nonEnglishChannelId = 1450610182995316969;
            await msg.ReplyAsync($"Read Rule #4, keep it english outside of <#{nonEnglishChannelId}>");
            await msg.DeleteAsync();
        }
        catch { }
    }
}