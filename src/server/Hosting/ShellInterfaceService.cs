using Arcadia.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arcadia.Hosting;

public class ShellInterfaceService(ILogger<ShellInterfaceService> logger, SharedCache storage) : BackgroundService
{
    private readonly ILogger<ShellInterfaceService> _logger = logger;
    private readonly SharedCache _storage = storage;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Shell interface ready");
        while (!stoppingToken.IsCancellationRequested)
        {
            var command = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(command))
            {
                await ProcessCommand(command);
            }
        }
    }

    private Task ProcessCommand(string command)
    {
        switch (command.ToLower())
        {
            case "list gid":
                var gids = _storage.ListGameGids();
                _logger.LogInformation("> gids: {gids}", string.Join(';', gids));
                break;
            case "list games":
                var games = _storage.GetGameServers();
                foreach (var game in games)
                {
                    _logger.LogInformation("> GAME {gid}: name={name}, uid={uid}, ready={canJoin}, players={players}, joining={joining}", game.GID, game.NAME, game.UID, game.CanJoin, game.ConnectedPlayers.Count, game.JoiningPlayers.Count);
                }
                break;
            default:
                Console.WriteLine("Unknown command.");
                break;
        }

        return Task.CompletedTask;
    }
}