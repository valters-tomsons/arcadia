using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Lingua;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Discord;

public sealed partial class ModerationService : IAsyncDisposable
{
    private static readonly string[] StupidPhrases =
    [
        "does anyone know where to get",
        "what do I need to do to play online",
        "how to play online",
        "how do i play online",
        "how can i play online",
        "can we play online",

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
    ).WithMinimumRelativeDistance(0.35).Build();

    private enum RuleId { NoImageSpam, NoPiracy, ReadTheInfo, MediaOnly, EnglishOnly }
    private enum Penalty { Delete, Ban }
    private sealed record Rule(RuleId Id, Func<SocketUserMessage, bool> IsViolation, Penalty Penalty, string ReplyText);
    private readonly Rule[] Rules;

    private readonly ConcurrentQueue<SocketUserMessage> _messageQueue = new();
    private readonly Task _scanTask;
    private readonly PeriodicTimer _scanTimer = new(TimeSpan.FromSeconds(5));

    private readonly ILogger<ModerationService> _logger;
    private readonly bool _dryRun;

    public ModerationService(IOptions<DiscordSettings> options, ILogger<ModerationService> logger)
    {
        var config = options.Value;
        _dryRun = config.ModerationDryRun;

        Rules =
        [
            new(RuleId.NoImageSpam, IsImageSpam,                                                 Penalty.Ban,     "Banned for spam. Have a nice day! 👋"),
            new(RuleId.NoPiracy,    static m => ContainsAny(m.Content, PiracyPhrases),           Penalty.Delete,  "Read Rule #2, no discussion of piracy!"),
            new(RuleId.ReadTheInfo, static m => IsAnsweredQuestion(m.Content),                   Penalty.Delete, $"Read <#{config.ServerInfoChannel}> in its entirety, it's already explained!"),
            new(RuleId.MediaOnly,   m => m.Channel.Id == config.MediaChannel && !HasMedia(m),    Penalty.Delete,  string.Empty),
            new(RuleId.EnglishOnly, m => m.Channel.Id != config.NonEnglishChannel && IsNonEnglish(m), Penalty.Delete, $"Read Rule #4, keep it english outside of <#{config.NonEnglishChannel}>"),
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
        if (msg.Author.IsBot || msg.Author is not SocketGuildUser usr) return;
        if (usr.GuildPermissions.ManageMessages) return;
        _messageQueue.Enqueue(msg);
    }

    private async Task ScanTask()
    {
        Dictionary<ulong, (SocketUserMessage msg, Rule rule)> violations = [];

        while (await _scanTimer.WaitForNextTickAsync())
        {
            violations.Clear();

            while (_messageQueue.TryDequeue(out var msg))
            {
                try
                {
                    foreach (var rule in Rules)
                    {
                        if (rule.IsViolation(msg))
                        {
                            violations[msg.Id] = (msg, rule);
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[Moderation] Exception while scanning message {MessageId}: {Message}", msg.Id, e.Message);
                }
            }

            HashSet<ulong>? alreadyBanned = null;
            foreach (var (msg, rule) in violations.Values)
            {
                if (alreadyBanned?.Contains(msg.Author.Id) == true) continue;

                _logger.LogInformation(
                    "[Moderation] {DryRun}Rule '{Rule}' violated by {Username} ({UserId}), penalty: {Penalty}, content: '{Content}'",
                    _dryRun ? "(dry run) " : string.Empty, rule.Id, msg.Author.Username, msg.Author.Id, rule.Penalty, msg.Content
                );

                if (_dryRun) continue;

                if (rule.ReplyText.Length > 0)
                {
                    try
                    {
                        await msg.ReplyAsync(rule.ReplyText, options: Constants.ReqOptions);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "[Moderation] Failed to reply for rule '{Rule}': {Message}", rule.Id, e.Message);
                    }
                }

                try
                {
                    if (rule.Penalty == Penalty.Ban && msg.Author is SocketGuildUser usr)
                    {
                        await usr.BanAsync(pruneDays: 2, "Spam", options: Constants.ReqOptions);
                        (alreadyBanned ??= []).Add(usr.Id);
                    }
                    else
                    {
                        await msg.DeleteAsync(options: Constants.ReqOptions);
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

    private static bool HasMedia(SocketUserMessage msg)
    {
        return msg.Attachments.Count > 0
            || msg.Embeds.Count > 0
            || msg.Content.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || msg.Content.Contains("https://", StringComparison.OrdinalIgnoreCase);
    }

    // Only short questions, longer messages are usually specific and worth answering
    internal static bool IsAnsweredQuestion(string content)
    {
        const int MaxWordCount = 15;

        content = content.Trim();
        if (WordCount(content) > MaxWordCount) return false;

        foreach (var p in StupidPhrases)
        {
            var idx = content.IndexOf(p, StringComparison.OrdinalIgnoreCase);
            if (idx == 0 || (idx > 0 && content.Contains('?'))) return true;
        }

        return false;
    }

    private static int WordCount(string content) => content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsNonEnglish(SocketUserMessage msg) => IsNonEnglish(msg.Content);

    internal static bool IsNonEnglish(string content)
    {
        const int MinContentLength = 5;
        const int MinWordCount = 2;

        content = UrlRegex().Replace(content, string.Empty).Trim();
        if (content.Length < MinContentLength) return false;
        if (WordCount(content) < MinWordCount) return false;

        var lang = LangDetector.DetectLanguageOf(content);
        return lang != Language.English && lang != Language.Unknown;
    }

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // Spam bots post attachments to several channels at once, real users post a few screenshots in one channel
    private const int ImageSpamMessageThreshold = 3;
    private const int ImageSpamChannelThreshold = 2;
    private readonly TimeSpan ImageSpamWindow = TimeSpan.FromSeconds(20);
    private sealed record UserImagePosts(DateTimeOffset Start, HashSet<ulong> Channels)
    {
        public int Count { get; set; }
    }
    private readonly Dictionary<ulong, UserImagePosts> ImagePostHistory = [];

    private bool IsImageSpam(SocketUserMessage msg)
    {
        // Edits re-deliver the same attachments, don't count them twice
        if (msg.EditedTimestamp is not null) return false;
        if (msg.Attachments.Count == 0) return false;

        return TrackImagePost(msg.Author.Id, msg.Channel.Id, msg.Timestamp);
    }

    internal bool TrackImagePost(ulong authorId, ulong channelId, DateTimeOffset postedAt)
    {
        if (!ImagePostHistory.TryGetValue(authorId, out var window) || postedAt - window.Start > ImageSpamWindow)
        {
            window = new(postedAt, []);
            ImagePostHistory[authorId] = window;
        }

        window.Count++;
        window.Channels.Add(channelId);

        if (window.Count >= ImageSpamMessageThreshold && window.Channels.Count >= ImageSpamChannelThreshold)
        {
            ImagePostHistory.Remove(authorId);
            return true;
        }

        return false;
    }
}