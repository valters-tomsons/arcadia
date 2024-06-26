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
        using var inputStream = Console.OpenStandardInput();
        _logger.LogInformation("Shell interface open");
        while (!stoppingToken.IsCancellationRequested)
        {
            var command = await ReadInputLine();
            if (!string.IsNullOrWhiteSpace(command))
            {
                try
                {
                    await ProcessCommand(command);
                }
                catch
                {
                    _logger.LogWarning("Error occured in command");
                }
            }
        }
    }

    private Task ProcessCommand(string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries & StringSplitOptions.TrimEntries);

        // so what?
        switch (words[0].ToLower())
        {
            case "list":
                switch (words[1].ToLower())
                {
                    case "gid":
                        var gids = _storage.ListGameGids();
                        _logger.LogInformation("> gids: {gids}", string.Join(';', gids));
                        break;
                    case "games":
                        var games = _storage.GetGameServers();
                        _logger.LogInformation("> Total hosted games: {games}", games.Length);
                        foreach (var game in games)
                        {
                            if (game is null) continue;
                            _logger.LogInformation("> GAME {gid}: name={name}, uid={uid}, ready={canJoin}, players={players}, joining={joining}", game.GID, game.NAME, game.UID, game.CanJoin, game.ConnectedPlayers.Count, game.JoiningPlayers.Count);
                        }
                        break;
                    case "clients":
                        var clients = _storage.GetConnectedClients();
                        _logger.LogInformation("> Total players online: {clients}", clients.Length);
                        foreach (var client in clients)
                        {
                            if (client is null) continue;
                            _logger.LogInformation("> {name}: {uid} | fesl={fesl}, theater={thea}", client.NAME, client.UID, client.FeslConnection?.NetworkStream is not null, client.TheaterConnection?.NetworkStream is not null);
                        }
                        break;
                    default:
                        _logger.LogInformation("Unknown 'list' command");
                        break;
                }
                break;
            case "set":
                switch (words[1].ToLower())
                {
                    case "joinable":
                        var gid = long.Parse(words[2]);
                        var game = _storage.GetGameByGid(gid);
                        if (game is not null)
                        {
                            game.CanJoin = true;
                            _logger.LogInformation("true");
                        }
                        else
                        {
                            _logger.LogError("No Game with such ID");
                        }

                        break;
                    default:
                        _logger.LogInformation("Unknown 'set' command");
                        break;
                }
                break;
            default:
                _logger.LogInformation("Unknown command root");
                break;
        }

        return Task.CompletedTask;
    }

    private static Task<string> ReadInputLine()
    {
        var tcs = new TaskCompletionSource<string>();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var line = Console.ReadLine() ?? string.Empty;
                tcs.SetResult(line);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}