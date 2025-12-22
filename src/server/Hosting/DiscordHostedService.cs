using Arcadia.Discord;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class DiscordHostedService(DiscordSocketClient client, ILogger<DiscordHostedService> logger, IOptions<DiscordSettings> config, StatusService statusService) : BackgroundService
{

    private readonly DiscordSocketClient _client = client;
    private readonly ILogger<DiscordHostedService> _logger = logger;
    private readonly IOptions<DiscordSettings> _config = config;
    private readonly StatusService _statusService = statusService;

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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(async () => _statusService.Execute(_client, stoppingToken), stoppingToken);
    }
}