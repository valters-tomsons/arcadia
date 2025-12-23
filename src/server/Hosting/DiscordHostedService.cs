using Arcadia.Discord;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class DiscordHostedService(DiscordSocketClient client, ILogger<DiscordHostedService> logger, IOptions<DiscordSettings> config, StatusService statusService, ModerationService moderationService) : BackgroundService
{
    private static readonly TimeSpan PeriodicUpdateInterval = TimeSpan.FromSeconds(10);

    private readonly DiscordSocketClient _client = client;
    private readonly ILogger<DiscordHostedService> _logger = logger;
    private readonly IOptions<DiscordSettings> _config = config;

    private readonly StatusService _statusService = statusService;
    private readonly ModerationService _moderationService = moderationService;

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

        _client.Ready += () =>
        {
            _logger.LogInformation("Discord bot connected & ready!");
            return Task.CompletedTask;
        };

        _client.MessageReceived += async (msg) =>
        {
            if (msg is SocketUserMessage usrMsg) await _moderationService.OnMessageReceived(usrMsg);
        };

        _client.MessageUpdated += async (cache, msg, channel) =>
        {
            if (msg is SocketUserMessage usrMsg) await _moderationService.OnMessageReceived(usrMsg);
        };

        await _client.LoginAsync(TokenType.Bot, _config.Value.BotToken);
        await _client.StartAsync();

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _statusService.Shutdown();
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

        await _statusService.Initialize(_client);

        await Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_client.ConnectionState != ConnectionState.Connected)
                    {
                        _logger.LogWarning("Discord connection lost, delaying execution!");
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    await _statusService.Execute(_client);

                    await Task.Delay(PeriodicUpdateInterval, stoppingToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to update status!");
                }
            }
        }, stoppingToken);
    }
}