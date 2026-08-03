using Arcadia.EA;
using Arcadia.Storage;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Arcadia.Discord.Constants;

namespace Arcadia.Discord;

public sealed class StatusService(ILogger<StatusService> logger, ConnectionManager sharedCache, IOptions<DiscordSettings> config, StatsStorage stats) : IDisposable
{
    private const string MessageIdFile = "./messageId";
    private const string AssetsUrlBase = "https://raw.githubusercontent.com/valters-tomsons/arcadia/refs/heads/main/src/server/static/assets/";
    private const int OnslaughtBatchSize = 8;

    private sealed record Listing(long GID, Embed Embed, GameId Game);
    private sealed record StatusChannel(IMessageChannel Channel, ulong StatusMessageId)
    {
        public Dictionary<GameId, string> RoleMentions { get; } = [];
        public Dictionary<long, ulong> GameMessages { get; } = [];
    }

    private StatusChannel? _status;

    private readonly ILogger<StatusService> _logger = logger;
    private readonly ConnectionManager _sharedCache = sharedCache;
    private readonly IOptions<DiscordSettings> _config = config;
    private readonly StatsStorage _stats = stats;
    private readonly GeoFlags _geo = new(logger);

    public async Task Initialize(DiscordSocketClient client)
    {
        _geo.Load();

        var cachedIds = await LoadMessageCacheFromFile();
        var cacheDirty = false;

        var channelId = _config.Value.OngoingGamesChannel;
        var channel = await client.GetChannelAsync(channelId, options: ReqOptions) as IMessageChannel ?? throw new("Failed to get channel");

        var cacheHit = cachedIds.TryGetValue(channelId, out var cachedMessageId);
        var statusMessage = cacheHit
            ? await channel.GetMessageAsync(cachedMessageId, options: ReqOptions)
            : await channel.SendMessageAsync("Initializing status...", options: ReqOptions);

        if (statusMessage is null)
        {
            if (cacheHit)
            {
                _logger.LogCritical("Cached message no longer exists, must manually remove cache line {ChannelId}:{MessageId}", channelId, cachedMessageId);
            }

            throw new($"Failed to acquire status message in channel:{channelId}");
        }

        if (!cacheHit)
        {
            cachedIds[channelId] = statusMessage.Id;
            cacheDirty = true;
            _logger.LogInformation("New status created, msgId:{MessageId}, chId:{ChannelId}", statusMessage.Id, channelId);
        }

        await foreach (var batch in channel.GetMessagesAsync())
        {
            foreach (var message in batch)
            {
                if (message.Id != statusMessage.Id) await message.DeleteAsync();
            }
        }

        _status = new StatusChannel(channel, statusMessage.Id);

        if (channel is SocketTextChannel guildChannel)
        {
            foreach (var role in Roles)
            {
                var guildRole = guildChannel.Guild.Roles.FirstOrDefault(r => r.Name == role.DisplayName);
                if (guildRole is not null) _status.RoleMentions[role.Id] = guildRole.Mention;
            }
        }

        if (cacheDirty)
        {
            await File.WriteAllLinesAsync(MessageIdFile, cachedIds.Select(x => $"{x.Key}:{x.Value}"));
        }
    }

    public async Task Execute(DiscordSocketClient client)
    {
        await PostOnslaughtStats(client);
        await UpdateStatus();
    }

    public async Task Shutdown()
    {
        if (_status is null) return;

        try
        {
            await _status.Channel.ModifyMessageAsync(_status.StatusMessageId, x =>
            {
                x.Content = "Server offline!";
                x.Embeds = null;
            });

            foreach (var messageId in _status.GameMessages.Values)
            {
                await _status.Channel.DeleteMessageAsync(messageId);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to notify channel about shutdown!");
        }

        _status = null;
    }

    private async Task<Dictionary<ulong, ulong>> LoadMessageCacheFromFile()
    {
        if (!File.Exists(MessageIdFile))
        {
            _logger.LogWarning("Status messageId Cache file doesn't exist");
            return [];
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(MessageIdFile);
            var cache = new Dictionary<ulong, ulong>(lines.Length);

            foreach (var line in lines)
            {
                var parts = line.Split(':');
                cache[ulong.Parse(parts[0])] = ulong.Parse(parts[1]);
            }

            return cache;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read messageId cache file.");
            return [];
        }
    }

    private async Task UpdateStatus()
    {
        if (_status is null) return;

        var listings = BuildListings();
        var summary = FormatSummary(listings.Length);
        var liveGids = listings.Select(x => x.GID).ToHashSet();

        await UpdateSummary(_status, summary);
        await RemoveStaleListings(_status, liveGids);
        await PublishListings(_status, listings);
    }

    private async Task UpdateSummary(StatusChannel status, string summary)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Arcadia")
            .WithDescription(summary)
            .Build();

        try
        {
            await status.Channel.ModifyMessageAsync(status.StatusMessageId, x =>
            {
                x.Content = "\n";
                x.Embed = embed;
            },
            options: ReqOptions);
        }
        catch (HttpException e)
        {
            _logger.LogError(e, "Failed to update channel status message, reason: {Message}", e.Message);
        }
    }

    private async Task RemoveStaleListings(StatusChannel status, HashSet<long> liveGids)
    {
        List<long>? stale = null;

        foreach (var (gid, messageId) in status.GameMessages)
        {
            if (liveGids.Contains(gid)) continue;
            (stale ??= []).Add(gid);

            try
            {
                _logger.LogDebug("Removing game listing, GID:{GID}", gid);
                await status.Channel.DeleteMessageAsync(messageId, options: ReqOptions);
            }
            catch (HttpException e)
            {
                _logger.LogError(e, "Failed to delete game server message, reason: {Message}", e.Message);
            }
        }

        if (stale is null) return;
        foreach (var gid in stale) status.GameMessages.Remove(gid);
    }

    private async Task PublishListings(StatusChannel status, Listing[] listings)
    {
        foreach (var listing in listings)
        {
            try
            {
                if (status.GameMessages.TryGetValue(listing.GID, out var messageId))
                {
                    await status.Channel.ModifyMessageAsync(messageId, x =>
                    {
                        x.Content = "\n";
                        x.Embed = listing.Embed;
                    },
                    options: ReqOptions);

                    _logger.LogDebug("Server listing updated, GID:{GID}", listing.GID);
                }
                else
                {
                    var mention = status.RoleMentions.GetValueOrDefault(listing.Game);
                    var posted = await status.Channel.SendMessageAsync(mention ?? "\n", embed: listing.Embed, options: ReqOptions);

                    status.GameMessages[listing.GID] = posted.Id;
                    _logger.LogDebug("Server listing added, GID:{GID}", listing.GID);
                }
            }
            catch (HttpException e)
            {
                _logger.LogError(e, "Failed to update game server messages, reason: {Message}", e.Message);
            }
        }
    }

    private Listing[] BuildListings()
    {
        var servers = _sharedCache.GetAllServersInternal();
        var listings = new List<Listing>(servers.Length);

        foreach (var server in servers)
        {
            if (!server.CanJoin) continue;

            var partition = server.PartitionId.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (!Enum.TryParse<GameId>(partition, ignoreCase: true, out var gameId) || !GameCatalog.Games.TryGetValue(gameId, out var game))
            {
                _logger.LogError("No status builder for '{PartitionId}'", server.PartitionId);
                continue;
            }

            if (game.Listed is { } listed && !listed(server)) continue;

            try
            {
                listings.Add(new(server.GID, BuildEmbed(server, game), gameId));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to build server status embedding: {Message}", e.Message);
            }
        }

        return [.. listings];
    }

    private Embed BuildEmbed(GameServerListing server, GameInfo game)
    {
        var embed = new EmbedBuilder()
            .WithTitle(game.Title + _geo.Suffix(server.TheaterConnection?.RemoteAddress))
            .AddField("Players", FormatPlayers(server))
            .WithTimestamp(server.StartedAt);

        foreach (var field in game.Fields)
        {
            var value = field.Value(server);
            if (!string.IsNullOrWhiteSpace(value)) embed.AddField(field.Name, value);
        }

        if (game.Level is { } level
            && server.Data.GetValueOrDefault(level.DataKey) is { } levelKey
            && level.Levels.TryGetValue(levelKey, out var levelInfo))
        {
            embed.AddField("Level", levelInfo.Display);
            embed.WithImageUrl(string.Concat(AssetsUrlBase, levelInfo.Image));
        }

        if (game.Footer?.Invoke(server) is { } footer) embed.WithFooter(footer);

        return embed.Build();
    }

    private static string FormatPlayers(GameServerListing server)
    {
        var maxPlayers = server.Data.GetValueOrDefault("MAX-PLAYERS", "?");
        var players = server.ConnectedPlayers;

        return players.IsEmpty
            ? $"0/{maxPlayers}"
            : $"{players.Count}/{maxPlayers} | {string.Join(", ", players.Select(x => x.Value.User.Username))}";
    }

    private static string FormatSummary(int gameCount) => gameCount switch
    {
        0 => "**0** ongoing games. 😞",
        1 => "**1** ongoing game! ⭐",
        _ => $"**{gameCount}** ongoing games! 🔥"
    };

    private async Task PostOnslaughtStats(DiscordSocketClient client)
    {
        if (_stats.QueueCount == 0) return;

        var embed = new EmbedBuilder().WithTitle("Onslaught finished!");
        var posted = 0;

        for (var i = 0; i < OnslaughtBatchSize; i++)
        {
            var msg = _stats.DequeueCompletion();
            if (msg is null) break;

            var level = GameCatalog.OnslaughtLevels.GetValueOrDefault($"Levels/ONS_MP_{msg.MapKey}");
            if (level is null)
            {
                _logger.LogError("Unknown onslaught map key '{MapKey}', not submitting stat {BatchIdx}!", msg.MapKey, i);
                continue;
            }

            var gt = msg.GameTime;
            var text = $"Finished {level.Display} on {msg.Difficulty} in {gt.Hours} hours, {gt.Minutes} minutes and {gt.Seconds} seconds".Replace(" 0 hours, ", " ");

            embed.AddField(msg.PlayerName, text);
            posted++;
        }

        if (posted == 0) return;

        if (await client.GetChannelAsync(_config.Value.OnslaughtStatsChannel) is not IMessageChannel channel)
        {
            _logger.LogError("Failed to open status channel: {ChannelId}", _config.Value.OnslaughtStatsChannel);
            return;
        }

        await channel.SendMessageAsync("\n", embed: embed.Build(), options: ReqOptions);
        _logger.LogInformation("New stats batch posted, {Count} messages", posted);
    }

    public void Dispose() => _geo.Dispose();
}