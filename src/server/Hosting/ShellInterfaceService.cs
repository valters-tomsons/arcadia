using Arcadia.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arcadia.Hosting;

public class ShellInterfaceService(ILogger<ShellInterfaceService> logger, SharedCache storage, SharedCounters sharedCounters) : BackgroundService
{
    private readonly ILogger<ShellInterfaceService> _logger = logger;
    private readonly SharedCache _storage = storage;
    private readonly SharedCounters _sharedCounters = sharedCounters;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var inputStream = Console.OpenStandardInput();
        _logger.LogWarning("Shell interface open!");
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
            case "dev":
                switch (words[1].ToLower())
                {
                    case "1943":
                        var host = _storage.GetConnectedClients().First();

                        var gn = $"beach-{host.NAME}";
                        var ip = host.TheaterConnection?.NetworkAddress;
                        var game = new GameServerListing()
                        {
                            TheaterConnection = host.TheaterConnection,
                            UID = _sharedCounters.GetNextUserId(),
                            GID = _sharedCounters.GetNextGameId(),
                            LID = 257,
                            UGID = "NOGUID",
                            EKEY = "NOENCYRPTIONKEY", // Yes, that's the actual string
                            SECRET = "NOSECRET",
                            NAME = gn,
                            CanJoin = true,
                            Data = new()
                            {
                                ["RESERVE-HOST"] = "1",
                                ["PORT"] = "1003",
                                ["INT-PORT"] = "1003",
                                ["P"] = "1003",
                                ["HTTYPE"] = "A",
                                ["TYPE"] = "G",
                                ["QLEN"] = "0",
                                ["DISABLE-AUTO-DEQUEUE"] = "0",
                                ["HXFR"] = "0",
                                ["INT-IP"] = ip!,
                                ["MAX-PLAYERS"] = "16",
                                ["B-maxObservers"] = "0",
                                ["B-numObservers"] = "0",
                                ["B-version"] = "RETAIL421378",
                                ["JOIN"] = "O",
                                ["RT"] = "RT",
                                ["TICKET"] = $"{_sharedCounters.GetNextTicket()}",
                                ["B-U-balance"] = "NORMAL",
                                ["B-U-ciab"] = "MIXED",
                                ["B-U-elo"] = "1000",
                                ["B-U-gamemode"] = "CONQUEST",
                                ["B-U-level"] = "levels/wake_island_s",
                                ["B-U-location"] = "nrt",
                                ["B-U-mod"] = "1943",
                                ["B-U-playgroup"] = "NO",
                                ["B-U-public"] = "YES",
                                ["B-U-revision"] = "0",
                                ["B-U-type"] = "NORMAL",
                                ["JP"] = "2",
                                ["HN"] = host.NAME,
                                ["N"] = gn,
                                ["I"] = ip!,
                                ["J"] = "O",
                                // ["B-U-Time"] = "T%3a20.02 S%3a 9.81 L%3a 0.00",
                                ["V"] = "1.0",
                                // ["B-U-trial"] = "RETAIL",
                                // ["B-U-hash"] = "8FF089DA-0DE7-0470-EF0F-0D4C905B7DC5",
                                // ["B-U-Frames"] = "T%3a 300 B%3a 1",
                                ["QP"] = "0",
                                ["MP"] = "24",
                                ["PL"] = "PS3",
                                ["PW"] = "0",
                                ["B-U-coralsea"] = "NO",
                                ["AP"] = "0"
                            }
                        };
                        game.ConnectedPlayers.Add(host);
                        _storage.AddGame(game);
                        break;
                    default:
                        _logger.LogInformation("Unknown 'dev' command");
                        break;
                }
                break;
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