using System.Globalization;
using System.Text;
using Arcadia.Storage;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class DiscordHostedService(ILogger<DiscordHostedService> logger, SharedCache sharedCache, IOptions<DiscordSettings> config) : BackgroundService
{
    private static readonly DiscordSocketClient _client = new();
    private static readonly TimeSpan PeriodicUpdateInterval = TimeSpan.FromSeconds(10);

    private readonly ILogger<DiscordHostedService> _logger = logger;
    private readonly SharedCache _sharedCache = sharedCache;

    private readonly IOptions<DiscordSettings> _config = config;
    private IUserMessage? statusMessage;

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

    private async Task<IUserMessage?> InitStatusMessage()
    {
        if (_client.GetChannel(_config.Value.ChannelId) is not IMessageChannel channel)
        {
            return null;
        }

        return await channel.SendMessageAsync("Backend starting...");
    }

    private async Task UpdateRunningStatus(IUserMessage message)
    {
        var hosts = _sharedCache.GetGameServers();
        var connected = _sharedCache.GetConnectedClients().Length;
        var playing = hosts.Select(x => x.ConnectedPlayers.Count + x.JoiningPlayers.Count).Sum();

        if (_client.GetChannel(_config.Value.ChannelId) is IMessageChannel channel)
        {
            var statusBuilder = new StringBuilder($"Players Online: `{connected}`\nPlayers In-Game: `{playing}`");

            foreach (var server in hosts)
            {
                if (server.TheaterConnection?.NetworkStream?.CanWrite != true || !server.CanJoin) continue;

                try
                {
                    var serverName = $"**{server.NAME}**";
                    var levelName = LevelDisplayName(server.Data["B-U-level"]);
                    var difficulty = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(server.Data["B-U-difficulty"]);
                    var online = $"{server.ConnectedPlayers.Count}/{server.Data["MAX-PLAYERS"]}";

                    statusBuilder
                        .AppendLine()
                        .AppendLine()
                        .AppendLine(serverName)
                        .Append("Level: ").AppendLine(levelName)
                        .Append("Difficulty: ").AppendLine(difficulty)
                        .Append("Online: ").AppendLine(online);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to write server status!");
                }
            }

            await channel.ModifyMessageAsync(message.Id, x => x.Content = statusBuilder.ToString());
        }
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
}
