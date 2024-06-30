using Arcadia.Storage;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class DiscordHostedService(ILogger<DiscordHostedService> logger, SharedCache sharedCache, IOptions<DiscordSettings> config) : BackgroundService
{
    private const string messageIdFile = "./messageId";

    private static readonly TimeSpan PeriodicUpdateInterval = TimeSpan.FromSeconds(10);
    private static readonly DiscordSocketClient _client = new();

    private readonly ILogger<DiscordHostedService> _logger = logger;
    private readonly SharedCache _sharedCache = sharedCache;
    private readonly IOptions<DiscordSettings> _config = config;

    private readonly List<IMessage> _statusMessages = [];

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

        await InitMessages();

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

    private async Task InitMessages()
    {
        var channels = _config.Value.Channels;
        var cachedIds = await GetCachedMessageIds();
        var updateCache = false;

        foreach (var channelId in channels)
        {
            if (await _client.GetChannelAsync(channelId) is not IMessageChannel channel)
            {
                _logger.LogWarning("Failed to open channel: {channelId}", channelId);
                continue;
            }

            if (cachedIds.TryGetValue(channel.Id, out var messageId))
            {
                var msg = await channel.GetMessageAsync(messageId);
                _statusMessages.Add(msg);
            }
            else
            {
                updateCache = true;
                var msg = await channel.SendMessageAsync("Backend starting...");
                _logger.LogInformation("New status created: {messageId}", msg.Id);
                _statusMessages.Add(msg);
                cachedIds.Add(channelId, msg.Id);
            }
        }

        if (updateCache)
        {
            await CacheMessageIds(cachedIds);
        }
    }

    private async Task GracefulShutdown()
    {
        foreach(var message in _statusMessages)
        {
            if (_client.GetChannel(message.Channel.Id) is IMessageChannel channel)
            {
                await channel.ModifyMessageAsync(message.Id, x =>
                {
                    x.Content = "Server offline!";
                    x.Embeds = null;
                });
            }
        }

    }

    private async Task UpdateRunningStatus()
    {
        var hosts = _sharedCache.GetGameServers();
        var infoEmbeds = new List<Embed>(hosts.Length);

        foreach (var server in hosts)
        {
            if (server.TheaterConnection?.NetworkStream?.CanWrite != true || server.TheaterConnection?.NetworkStream?.CanRead != true) continue;
            try
            {
                var serverName = $"**{server.NAME}**";

                var level = server.Data["B-U-level"];
                var levelName = LevelDisplayName(level);
                var levelImageUrl = LevelImageUrl(level);

                var difficulty = server.Data.GetValueOrDefault("B-U-difficulty") ?? "`N/A`";
                var gamemode = server.Data["B-U-gamemode"];
                var online = $"{server.ConnectedPlayers.Count}/{server.Data["MAX-PLAYERS"]}";

                var eb = new EmbedBuilder()
                    .WithTitle(serverName)
                    .WithImageUrl(levelImageUrl)
                    .AddField("Level", levelName)
                    .AddField("Difficulty", difficulty)
                    .AddField("Gamemode", gamemode)
                    .AddField("Online", online);
                
                infoEmbeds.Add(eb.Build());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to prepare server status!");
            }
        }

        foreach (var message in _statusMessages)
        {
            if (_client.GetChannel(message.Channel.Id) is IMessageChannel channel)
            {
                var serverContent = new List<EmbedBuilder>(hosts.Length);
                await channel.ModifyMessageAsync(message.Id, x =>
                {
                    x.Content = infoEmbeds.Count > 0 ? "Ongoing Games:\n" : "Server online, no ongoing games. :(";
                    x.Embeds = infoEmbeds.ToArray();
                });
            }
        }
    }

    private async Task<Dictionary<ulong, ulong>> GetCachedMessageIds()
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

    private static Task CacheMessageIds(IDictionary<ulong, ulong> channelMessages)
    {
        return File.WriteAllLinesAsync(messageIdFile, channelMessages.Select(x => $"{x.Key}:{x.Value}"));
    }

    private static string LevelDisplayName(string levelName)
    {
        return levelName switch
        {
            "Levels/ONS_MP_002" => "Valparaiso",
            "Levels/ONS_MP_004" => "Isla Inocentes",
            "Levels/ONS_MP_005" => "Atacama Desert",
            "Levels/ONS_MP_008" => "Nelson Bay",
            _ => levelName,
        };
    }

    private static string LevelImageUrl(string levelName)
    {
        return levelName switch
        {
            "Levels/ONS_MP_002" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Valparaiso.jpg",
            "Levels/ONS_MP_004" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Isla_Inocentes.jpg",
            "Levels/ONS_MP_005" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Atacama_Desert.jpg",
            "Levels/ONS_MP_008" => "https://tomsonscloudstorage01.blob.core.windows.net/arcadia/BC2_Nelson_Bay.jpg",
            _ => string.Empty,
        };
    }
}
