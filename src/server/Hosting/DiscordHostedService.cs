using System.Text;
using Arcadia.EA;
using Arcadia.Storage;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class DiscordHostedService(DiscordSocketClient client, ILogger<DiscordHostedService> logger, ConnectionManager sharedCache, IOptions<DiscordSettings> config, StatsStorage stats) : BackgroundService
{
    private const string messageIdFile = "./messageId";
    private static readonly TimeSpan PeriodicUpdateInterval = TimeSpan.FromSeconds(10);

    private readonly DiscordSocketClient _client = client;
    private readonly ILogger<DiscordHostedService> _logger = logger;
    private readonly ConnectionManager _sharedCache = sharedCache;
    private readonly IOptions<DiscordSettings> _config = config;
    private readonly StatsStorage _stats = stats;

    private readonly List<(IMessageChannel Channel, ulong StatusId)> _channelStatus = [];
    private readonly Dictionary<ulong, List<(long GID, ulong MessageId)>> _channelsGameMessage = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Value.EnableBot)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.Value.BotToken))
        {
            _logger.LogWarning("Discord bot not configured!");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _client.Log += x =>
            {
                _logger.LogDebug("Discord.NET: {msg}", x.ToString());
                return Task.CompletedTask;
            };
        }

        _logger.LogInformation("Starting Discord status bot...");

        await _client.LoginAsync(TokenType.Bot, _config.Value.BotToken);
        await _client.StartAsync();

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await GracefulShutdown();
        await _client.StopAsync();
        await _client.LogoutAsync();
        await base.StopAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(async () => {
            while (_client.ConnectionState != ConnectionState.Connected)
            {
                await Task.Delay(1000, stoppingToken);
            }

            await InitializeChannels();

            while (!stoppingToken.IsCancellationRequested)
            {
                try 
                {
                    if (_client.ConnectionState != ConnectionState.Connected)
                    {
                        _logger.LogWarning("Discord connection lost!");
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    await ProcessNewStats();

                    await UpdateGameStatus();
                    await Task.Delay(PeriodicUpdateInterval, stoppingToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to update status!");
                }
            }
        }, stoppingToken);
    }

    private async Task GracefulShutdown()
    {
        foreach (var (channel, statusId) in _channelStatus)
        {
            try
            {
                await channel.ModifyMessageAsync(statusId, x =>
                {
                    x.Content = "Server offline!";
                    x.Embeds = null;
                });

                var gameMessages = _channelsGameMessage.GetValueOrDefault(channel.Id);
                if (gameMessages is null) continue;

                foreach (var (_, gameMessageId) in gameMessages)
                {
                    await channel.DeleteMessageAsync(gameMessageId);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to notify channel about shutdown!");
            }
        }

        _channelStatus.Clear();
        _channelsGameMessage.Clear();
    }

    private async Task InitializeChannels()
    {
        var channels = _config.Value.Channels;
        var cachedIds = await GetCachedStatusMessageIds();
        var flushCache = false;

        foreach (var channelId in channels)
        {
            if (await _client.GetChannelAsync(channelId) is not IMessageChannel channel)
            {
                _logger.LogError("Failed to open channel: {channelId}", channelId);
                continue;
            }

            var cacheHit = cachedIds.TryGetValue(channel.Id, out var messageId);
            var statusMsg = cacheHit ? await channel.GetMessageAsync(messageId) : await channel.SendMessageAsync("Initializing status...");
            if (statusMsg is null)
            {
                if (cacheHit)
                {
                    _logger.LogWarning("Cached message no longer exists, must manually remove cache line {channelId}:{messageId}", channelId, messageId);
                }

                _logger.LogError("Failed to acquire status message in channel:{channelId}. Messages will not be posted here.", channelId);
                continue;
            }
            else if (!cacheHit)
            {
                flushCache = true;
                cachedIds.Add(channelId, statusMsg.Id);
                _logger.LogInformation("New status created, msgId:{messageId}, chId:{channelId}", statusMsg.Id, channelId);
            }

            await foreach (var batch in channel.GetMessagesAsync())
            {
                foreach (var message in batch)
                {
                    if (message.Id != statusMsg.Id) await message.DeleteAsync();
                }
            }

            _channelStatus.Add((channel, statusMsg.Id));
        }

        if (flushCache)
        {
            await CacheStatusMessageIds(cachedIds);
        }
    }

    private async Task ProcessNewStats()
    {
        var eb = new EmbedBuilder().WithTitle("Onslaught finished!");

        const int batchSize = 8;
        for (var i = 0; i < batchSize; i++)
        {
            var msg = _stats.GetLevelComplete();
            if (msg is null)
            {
                if (i == 0) return;
                _logger.LogInformation("Finished processing {i} stats messages.", i + 1);
                break;
            }

            var level = $"Levels/ONS_MP_{msg.MapKey}";
            var levelName = LevelDisplayName(level);
            var gt = msg.GameTime;
            var message = $"Finished {levelName} on {msg.Difficulty} in {gt.Hours} hours, {gt.Minutes} minutes and {gt.Seconds} seconds".Replace(" 0 hours, ", " ");

            eb.AddField(msg.PlayerName, message);
        }

        if (await _client.GetChannelAsync(_config.Value.OnslaughtStatsChannel) is not IMessageChannel channel)
        {
            _logger.LogError("Failed to open status channel: {channelId}", _config.Value.OnslaughtStatsChannel);
            return;
        }

        await channel.SendMessageAsync("\n", embed: eb.Build());
        _logger.LogInformation("New stats batch posted");
    }

    private async Task UpdateGameStatus()
    {
        var content = BuildGameStatusContent();

        foreach (var (channel, statusId) in _channelStatus)
        {
            try
            {
                await channel.ModifyMessageAsync(statusId, x =>
                {
                    x.Content = "\n";
                    x.Embed = new EmbedBuilder()
                            .WithTitle("Arcadia")
                            .WithDescription(content.StatusMessage)
                            .Build();
                });
            }
            catch (HttpException e)
            {
                _logger.LogError(e, "Failed to update channel status message, reason: {message}", e.Message);
            }

            var gameMessagesInChannel = _channelsGameMessage.GetValueOrDefault(channel.Id);
            if (gameMessagesInChannel is null)
            {
                gameMessagesInChannel = new(content.Games.Length);
                _channelsGameMessage.Add(channel.Id, gameMessagesInChannel);
            }
            else
            {
                var gidsToRemove = new List<long>(3);
                foreach (var postedGame in gameMessagesInChannel)
                {
                    try
                    {
                        if (content.Games.Any(x => x.GID == postedGame.GID)) continue;
                        _logger.LogDebug("Removing game listing, GID:{GID}", postedGame.GID);

                        gidsToRemove.Add(postedGame.GID);
                        await channel.DeleteMessageAsync(postedGame.MessageId);
                    }
                    catch (HttpException e)
                    {
                        _logger.LogError(e, "Failed to delete game server message, reason: {message}", e.Message);
                    }
                }

                gameMessagesInChannel.RemoveAll(x => gidsToRemove.Contains(x.GID));
            }

            try
            {
                for (var i = 0; i < content.Games.Length; i++)
                {
                    var game = content.Games[i];
                    var postedMsg = gameMessagesInChannel.FirstOrDefault(x => game.GID == x.GID);
                    if (postedMsg == default)
                    {
                        var gameMessage = await channel.SendMessageAsync("\n", embed: game.Embed);
                        gameMessagesInChannel.Add((game.GID, gameMessage.Id));
                        _logger.LogDebug("Server listing added, GID:{GID}", game.GID);
                    }
                    else
                    {
                        await channel.ModifyMessageAsync(postedMsg.MessageId, x =>
                        {
                            x.Content = "\n";
                            x.Embed = game.Embed;
                        });

                        _logger.LogDebug("Server listing updated, GID:{GID}", game.GID);
                    }
                }
            }
            catch (HttpException e)
            {
                _logger.LogError(e, "Failed to update game server messages, reason: {message}", e.Message);
            }
        }
    }

    private (string StatusMessage, (long GID, Embed Embed)[] Games) BuildGameStatusContent()
    {
        var hosts = _sharedCache.GetAllServers();

        var gidEmbeds = new (long GID, Embed Embed)[hosts.Length];
        for (var i = 0; i < hosts.Length; i++)
        {
            var server = hosts[i];
            if (!server.CanJoin)
            {
                gidEmbeds[i].GID = 0;
                continue;
            }

            try
            {
                if (server.PartitionId.EndsWith("/AO3"))
                {
                    gidEmbeds[i] = BuildAO3Status(server);
                }
                else
                {
                    gidEmbeds[i] = BuildOnslaughtStatus(server);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to build server status embedding, game skipped!");
            }
        }

        var embeds = gidEmbeds.Where(x => x.GID != 0).ToArray();

        const string statusFormat = "**{0}** ongoing game{1}";
        var gameCount = embeds.Length;
        var statusEnd = gameCount == 0 ? "s. 😞" : gameCount > 1 ? "s! 🔥" : "! ⭐";
        var statusMsg = string.Format(statusFormat, gameCount, statusEnd);

        return (statusMsg, embeds);
    }

    private static (long GID, Embed Embed) BuildOnslaughtStatus(GameServerListing server)
    {
        var serverName = $"**{server.NAME.Replace("P2P-", string.Empty)}**";

        var level = server.Data.GetValueOrDefault("B-U-level");
        var levelName = LevelDisplayName(level);
        var levelImageUrl = LevelImageUrl(level);

        var difficulty = server.Data.GetValueOrDefault("B-U-difficulty") ?? "`N/A`";
        var gamemode = server.Data.GetValueOrDefault("B-U-gamemode") ?? "`N/A`";

        var eb = new EmbedBuilder()
            .WithTitle($"{serverName} ({gamemode})")
            .WithImageUrl(levelImageUrl)
            .AddField("Level", levelName)
            .AddField("Difficulty", difficulty)
            .AddField("Players", GetPlayerCountString(server))
            .WithTimestamp(server.StartedAt);

        return (server.GID, eb.Build());
    }

    private static (long GID, Embed Embed) BuildAO3Status(GameServerListing server)
    {
        var serverName = $"**{server.NAME}**";
        var gamemode = server.Data.GetValueOrDefault("B-U-Mode") ?? string.Empty;
        var level = server.Data.GetValueOrDefault("B-U-Map") ?? "`N/A`";
        var playlist = server.Data.GetValueOrDefault("B-U-MapPlaylist") ?? "`N/A`";

        var eb = new EmbedBuilder()
            .WithTitle($"{serverName} (AO3{gamemode})")
            .AddField("Level", level)
            .AddField("Playlist", playlist)
            .AddField("Players", GetPlayerCountString(server))
            .WithTimestamp(server.StartedAt);

        return (server.GID, eb.Build());
    }

    private static readonly StringBuilder _playersStringBuilder = new();
    private static string GetPlayerCountString(GameServerListing server)
    {
        var maxPlayers = server.Data["MAX-PLAYERS"];

        if (server.ConnectedPlayers.IsEmpty)
        {
            return $"0/{maxPlayers}";
        }

        _playersStringBuilder.Clear();
        _playersStringBuilder
            .Append(server.ConnectedPlayers.Count)
            .Append('/')
            .Append(maxPlayers)
            .Append(" | ")
            .AppendJoin(", ", server.ConnectedPlayers.Select(x => x.Value.NAME));

        return _playersStringBuilder.ToString();
    }

    private async Task<Dictionary<ulong, ulong>> GetCachedStatusMessageIds()
    {
        try
        {
            var text = await File.ReadAllLinesAsync(messageIdFile);
            var dict = new Dictionary<ulong, ulong>(text.Length);
            foreach (var line in text)
            {
                var parts = line.Split(':');
                dict.Add(ulong.Parse(parts[0]), ulong.Parse(parts[1]));
            }

            return dict;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read messageId cache file.");
            return [];
        }
    }

    private static Task CacheStatusMessageIds(IDictionary<ulong, ulong> channelMessages)
    {
        return File.WriteAllLinesAsync(messageIdFile, channelMessages.Select(x => $"{x.Key}:{x.Value}"));
    }

    private static string LevelDisplayName(string? levelName)
    {
        return levelName switch
        {
            "Levels/ONS_MP_002" => "Valparaiso",
            "Levels/ONS_MP_004" => "Isla Inocentes",
            "Levels/ONS_MP_005" => "Atacama Desert",
            "Levels/ONS_MP_008" => "Nelson Bay",
            "levels/wake_island_s" => "Wake Island",
            null => "`N/A`",
            _ => levelName,
        };
    }

    private static string LevelImageUrl(string? levelName)
    {
        const string hostBase = "https://raw.githubusercontent.com/valters-tomsons/arcadia/refs/heads/main/src/server/static/assets/";

        var fileName = levelName switch
        {
            "Levels/ONS_MP_002" => "BC2_Valparaiso.jpg",
            "Levels/ONS_MP_004" => "BC2_Isla_Inocentes.jpg",
            "Levels/ONS_MP_005" => "BC2_Atacama_Desert.jpg",
            "Levels/ONS_MP_008" => "BC2_Nelson_Bay.jpg",
            "levels/wake_island_s" => "BC1943_Wake_Island.jpg",
            _ => string.Empty,
        };

        return string.Concat(hostBase, fileName);
    }
}