using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Lingua;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Discord;

public sealed class ModerationService : IAsyncDisposable
{
    private static readonly string[] StupidPhrases =
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

    private static readonly string[] PiracyPhrases =
    [
        "pkgi",
        "where to find pkg",
        "where to get pkg",
        "where to download game",
        "how to download game",
    ];

    private static readonly LanguageDetector LangDetector = LanguageDetectorBuilder.FromLanguages(
        Language.English,
        Language.Spanish,
        Language.Russian
    ).WithMinimumRelativeDistance(0.2).Build();

    private enum RuleId { NoImageSpam, NoPiracy, ReadTheInfo, EnglishOnly }
    private enum Penalty { Delete, Ban }
    private sealed record Rule(RuleId Id, Func<SocketUserMessage, bool> IsViolation, Penalty Penalty, string ReplyText);
    private readonly Rule[] Rules;

    private readonly ConcurrentQueue<SocketUserMessage> _messageQueue = new();
    private readonly Task _scanTask;
    private readonly PeriodicTimer _scanTimer = new(TimeSpan.FromSeconds(5));

    private readonly ILogger<ModerationService> _logger;

    public ModerationService(IOptions<DiscordSettings> options, ILogger<ModerationService> logger)
    {
        var config = options.Value;

        Rules =
        [
            new(RuleId.NoImageSpam, IsImageSpam,                                          Penalty.Ban,     "Banned for spam. Have a nice day! 👋"),
            new(RuleId.NoPiracy,    static m => ContainsAny(m.Content, PiracyPhrases),    Penalty.Delete,  "Read Rule #2, no discussion of piracy!"),
            new(RuleId.ReadTheInfo, static m => ContainsAny(m.Content, StupidPhrases),    Penalty.Delete, $"Read <#{config.ServerInfoChannel}> in its entirety, it's already explained!"),
            new(RuleId.EnglishOnly, IsNonEnglish,                                         Penalty.Delete, $"Read Rule #4, keep it english outside of <#{config.NonEnglishChannel}>"),
        ];

        _logger = logger;
        _scanTask = Task.Run(ScanTask);
    }

    public async ValueTask DisposeAsync()
    {
        _scanTimer.Dispose();
        await _scanTask;
    }

    public void EnqueueMessage(SocketUserMessage msg)
    {
        if (msg.Author.IsBot || msg.Author is not SocketGuildUser) return;
        _messageQueue.Enqueue(msg);
    }

    private async Task ScanTask()
    {
        List<(SocketUserMessage, Rule)> violations = [];

        while (await _scanTimer.WaitForNextTickAsync())
        {
            violations.Clear();

            while (_messageQueue.TryDequeue(out var msg))
            {
                foreach (var rule in Rules)
                {
                    if (rule.IsViolation(msg))
                    {
                        violations.Add((msg, rule));
                        break;
                    }
                }
            }

            HashSet<ulong>? alreadyBanned = null;
            foreach (var (msg, rule) in violations)
            {
                if (alreadyBanned?.Contains(msg.Author.Id) == true) continue;

                _logger.LogInformation(
                    "[Moderation] Rule '{Rule}' violated by {Username} ({UserId}), penalty: {Penalty}, content: '{Content}'",
                    rule.Id, msg.Author.Username, msg.Author.Id, rule.Penalty, msg.Content
                );

                try
                {
                    await msg.ReplyAsync(rule.ReplyText);

                    if (rule.Penalty == Penalty.Ban && msg.Author is SocketGuildUser usr)
                    {
                        await usr.BanAsync(pruneDays: 2, "Spam");
                        (alreadyBanned ??= []).Add(usr.Id);
                    }
                    else
                    {
                        await msg.DeleteAsync();
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[Moderation] Exception while enforcing rule '{Rule}': {Message}", rule.Id, e.Message);
                }
            }

            var imageCutoff = DateTimeOffset.UtcNow - ImageSpamWindow;
            foreach (var (userId, window) in ImagePostHistory)
            {
                if (window.Start < imageCutoff) ImagePostHistory.Remove(userId, out _);
            }
        }
    }

    private static bool ContainsAny(string content, string[] phrases)
    {
        foreach (var p in phrases)
        {
            if (content.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsNonEnglish(SocketUserMessage msg)
    {
        const int MinContentLength = 5;

        var content = msg.Content.Trim();
        if (content.Length < MinContentLength) return false;

        var lang = LangDetector.DetectLanguageOf(content);
        return lang != Language.English && lang != Language.Unknown;
    }

    private const int ImageSpamThreshold = 4;
    private readonly TimeSpan ImageSpamWindow = TimeSpan.FromSeconds(20);
    private readonly record struct UserImagePosts(DateTimeOffset Start, int Count);
    private readonly Dictionary<ulong, UserImagePosts> ImagePostHistory = [];

    private bool IsImageSpam(SocketUserMessage msg)
    {
        var imageCount = msg.Attachments.Count;
        if (imageCount == 0) return false;

        var authorId = msg.Author.Id;
        var postedAt = msg.Timestamp;

        if (!ImagePostHistory.TryGetValue(authorId, out var window) || postedAt - window.Start > ImageSpamWindow)
        {
            window = new(postedAt, 0);
        }

        window = window with { Count = window.Count + imageCount };

        if (window.Count >= ImageSpamThreshold)
        {
            ImagePostHistory.Remove(authorId);
            return true;
        }

        ImagePostHistory[authorId] = window;
        return false;
    }
}