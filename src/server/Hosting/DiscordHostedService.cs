using Arcadia.Storage;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class DiscordHostedService(DiscordSocketClient client, ILogger<DiscordHostedService> logger, SharedCache sharedCache, IOptions<DiscordSettings> config) : BackgroundService
{

    private const string messageIdFile = "./messageId";
    private static readonly TimeSpan PeriodicUpdateInterval = TimeSpan.FromSeconds(10);

    private readonly DiscordSocketClient _client = client;
    private readonly ILogger<DiscordHostedService> _logger = logger;
    private readonly SharedCache _sharedCache = sharedCache;
    private readonly IOptions<DiscordSettings> _config = config;

    private readonly List<IMessage> _statusMessages = [];
    private readonly Dictionary<ulong, List<(long GID, IMessage Message)>> _channelsGameMessage = [];

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

        _client.Log += x =>
        {
            _logger.LogInformation("Discord.NET: {msg}", x.ToString());
            return Task.CompletedTask;
        };

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (_client.ConnectionState != ConnectionState.Connected)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await InitializeChannels();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_client.ConnectionState != ConnectionState.Connected)
            {
                _logger.LogWarning("Discord connection lost!");
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            await UpdateRunningStatus();
            await Task.Delay(PeriodicUpdateInterval, stoppingToken);
        }
    }

    private async Task InitializeChannels()
    {
        var channels = _config.Value.Channels;
        var cachedIds = await GetCachedStatusMessageIds();
        var updateCache = false;

        foreach (var channelId in channels)
        {
            if (await _client.GetChannelAsync(channelId) is not IMessageChannel channel)
            {
                _logger.LogError("Failed to open channel: {channelId}", channelId);
                continue;
            }

            IMessage statusMsg;
            if (cachedIds.TryGetValue(channel.Id, out var messageId))
            {
                statusMsg = await channel.GetMessageAsync(messageId);
                _logger.LogInformation("Existing status found, msgId:{messageId}, chId:{channelId}", statusMsg.Id, channelId);
            }
            else
            {
                updateCache = true;
                statusMsg = await channel.SendMessageAsync("Backend starting...");
                cachedIds.Add(channelId, statusMsg.Id);
                _logger.LogInformation("New status created, msgId:{messageId}, chId:{channelId}", statusMsg.Id, channelId);
            }

            await foreach (var batch in channel.GetMessagesAsync())
            {
                foreach(var message in batch)
                {
                    if (message.Id != statusMsg.Id) await message.DeleteAsync();
                }
            }

            _statusMessages.Add(statusMsg);
        }

        if (updateCache)
        {
            await CacheStatusMessageIds(cachedIds);
        }
    }

    private async Task GracefulShutdown()
    {
        foreach (var message in _statusMessages)
        {
            if (_client.GetChannel(message.Channel.Id) is not IMessageChannel channel)
            {
                _logger.LogWarning("Failed to open channel");
                continue;
            }

            await channel.ModifyMessageAsync(message.Id, x =>
            {
                x.Content = "Server offline!";
                x.Embeds = null;
            });

            var gameMessages = _channelsGameMessage.GetValueOrDefault(channel.Id);
            if (gameMessages is null) continue;
            foreach (var (_, gameMessage) in gameMessages)
            {
                await gameMessage.DeleteAsync();
            }
        }
    }

    private async Task UpdateRunningStatus()
    {
        var hosts = _sharedCache.GetGameServers();
        var gidEmbeds = new List<(long GID, Embed Embed)>(hosts.Length);

        foreach (var server in hosts)
        {
            if (!server.CanJoin) continue;
            try
            {
                var serverName = $"**{server.NAME}**";

                var level = server.Data.GetValueOrDefault("B-U-level");
                var levelName = LevelDisplayName(level);
                var levelImageUrl = LevelImageUrl(level);

                var difficulty = server.Data.GetValueOrDefault("B-U-difficulty") ?? "`N/A`";
                var gamemode = server.Data.GetValueOrDefault("B-U-gamemode") ?? "`N/A`";
                var online = $"{server.ConnectedPlayers.Count}/{server.Data["MAX-PLAYERS"]}";

                var eb = new EmbedBuilder()
                    .WithTitle(serverName)
                    .WithImageUrl(levelImageUrl)
                    .AddField("Level", levelName)
                    .AddField("Difficulty", difficulty)
                    .AddField("Gamemode", gamemode)
                    .AddField("Online", online);
                
                gidEmbeds.Add((server.GID, eb.Build()));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to build server status embedding, game skipped!");
            }
        }

        foreach (var status in _statusMessages)
        {
            if (_client.GetChannel(status.Channel.Id) is not IMessageChannel channel)
            {
                _logger.LogError("Skipping message in channel: {channelId}", status.Channel.Id);
                continue;
            }

            try
            {

                if (gidEmbeds.Count > 0)
                {
                    var gamePlural = gidEmbeds.Count > 1 ? "games! 🤩" : "game! 🫡";
                    await channel.ModifyMessageAsync(status.Id, x =>
                    {
                        x.Content = "\n";
                        x.Embed = new EmbedBuilder()
                                .WithTitle("Arcadia")
                                .WithDescription($"**{gidEmbeds.Count}** ongoing {gamePlural}")
                                .WithCurrentTimestamp()
                                .Build();
                    });
                }
                else
                {
                    await channel.ModifyMessageAsync(status.Id, x =>
                    {
                        x.Content = "\n";
                        x.Embed = new EmbedBuilder()
                                .WithTitle("Arcadia")
                                .WithDescription("There are no ongoing games. 😞")
                                .WithCurrentTimestamp()
                                .Build();
                    });
                }
            }
            catch (HttpException e)
            {
                _logger.LogError(e, "Failed to update channel status message, reason: {message}", e.Message);
            }

            var channelMessages = _channelsGameMessage.GetValueOrDefault(channel.Id);
            if (channelMessages is null)
            {
                channelMessages = new(gidEmbeds.Count);
                _channelsGameMessage.Add(channel.Id, channelMessages);
            }
            else
            {
                var gidsToRemove = new List<long>(3);
                foreach (var postedGame in channelMessages)
                {
                    try
                    {
                        if (gidEmbeds.Any(x => x.GID == postedGame.GID)) continue;
                        await channel.DeleteMessageAsync(postedGame.Message.Id);
                        gidsToRemove.Add(postedGame.GID);
                        _logger.LogDebug("Server listing removed, GID:{GID}", postedGame.GID);
                    }
                    catch (HttpException e)
                    {
                        _logger.LogError(e, "Failed to delete game server message, reason: {message}", e.Message);
                    }
                }

                channelMessages.RemoveAll(x => gidsToRemove.Contains(x.GID));
            }

            try
            {
                foreach (var game in gidEmbeds)
                {
                    var channelGameMsg = channelMessages.FirstOrDefault(x => x.GID == game.GID);
                    if (channelGameMsg == default)
                    {
                        var message = await channel.SendMessageAsync("\n", embed: game.Embed);
                        channelMessages.Add((game.GID, message));
                        _logger.LogDebug("Server listing added, GID:{GID}", game.GID);
                    }
                    else
                    {
                        await channel.ModifyMessageAsync(channelGameMsg.Message.Id, x =>
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

    private async Task<Dictionary<ulong, ulong>> GetCachedStatusMessageIds()
    {
        var dict = new Dictionary<ulong, ulong>();

        try
        {
            var text = await File.ReadAllLinesAsync(messageIdFile);
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
        }

        return dict;
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
        return levelName switch
        {
            "Levels/ONS_MP_002" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Valparaiso.jpg",
            "Levels/ONS_MP_004" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Isla_Inocentes.jpg",
            "Levels/ONS_MP_005" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Atacama_Desert.jpg",
            "Levels/ONS_MP_008" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Nelson_Bay.jpg",
            "levels/wake_island_s" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/1943_Wake_Island.jpg",
            _ => string.Empty,
        };
    }
}