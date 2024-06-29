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
    private IMessage? statusMessage;

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
        await _client.StopAsync();
        await _client.LogoutAsync();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (_client.ConnectionState != ConnectionState.Connected && statusMessage is null)
        {
            _logger.LogInformation("Initializing discord status...");
            await Task.Delay(2000, stoppingToken);
            statusMessage ??= await InitStatusMessage();
        }

        while (!stoppingToken.IsCancellationRequested && statusMessage is not null)
        {
            if (_client.ConnectionState != ConnectionState.Connected)
            {
                _logger.LogWarning("Discord connection lost!");
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            await UpdateRunningStatus(statusMessage);
            await Task.Delay(PeriodicUpdateInterval, stoppingToken);
        }
    }

    private async Task<IMessage?> InitStatusMessage()
    {
        if (_client.GetChannel(_config.Value.ChannelId) is not IMessageChannel channel)
        {
            return null;
        }

        var cachedId = await GetCachedMessageId();
        if(cachedId != 0)
        {
            return await channel.GetMessageAsync(cachedId);
        }

        var newMessage = await channel.SendMessageAsync("Backend starting...");
        await CacheMessageId(newMessage.Id);
        return newMessage;
    }

    private async Task UpdateRunningStatus(IMessage message)
    {
        var hosts = _sharedCache.GetGameServers();
        var connected = _sharedCache.GetConnectedClients().Length;
        var playing = hosts.Select(x => x.ConnectedPlayers.Count + x.JoiningPlayers.Count).Sum();

        if (_client.GetChannel(_config.Value.ChannelId) is IMessageChannel channel)
        {
            var serverEmbeds = new List<Embed>(hosts.Length);

            foreach (var server in hosts)
            {
                if (server.TheaterConnection?.NetworkStream?.CanWrite != true || !server.CanJoin) continue;

                try
                {
                    var serverName = $"**{server.NAME}**";

                    var level = server.Data["B-U-level"];
                    var levelName = LevelDisplayName(level);
                    var levelImageUrl = LevelImageUrl(level);

                    var difficulty = server.Data["B-U-difficulty"];
                    var online = $"{server.ConnectedPlayers.Count}/{server.Data["MAX-PLAYERS"]}";

                    var eb = new EmbedBuilder()
                        .WithTitle(serverName)
                        .WithDescription("Bad Company 2 Onslaught (PS3)")
                        .WithImageUrl(levelImageUrl)
                        .AddField("Level", levelName)
                        .AddField("Difficulty", difficulty)
                        .AddField("Online", online);

                    serverEmbeds.Add(eb.Build());
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to prepare server status!");
                }
            }

            var content = serverEmbeds.ToArray();
            await channel.ModifyMessageAsync(message.Id, x => x.Embeds = content);
        }
    }

    private async Task<ulong> GetCachedMessageId()
    {
        try
        {
            var text = await File.ReadAllTextAsync(messageIdFile);
            return ulong.Parse(text);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read messageId cache file.");
        }

        return 0;
    }

    private static Task CacheMessageId(ulong messageId)
    {
        return File.WriteAllTextAsync(messageIdFile, $"{messageId}");
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
