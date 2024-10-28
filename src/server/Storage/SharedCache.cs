using System.Collections.Concurrent;
using Arcadia.EA;
using Microsoft.Extensions.Logging;

namespace Arcadia.Storage;

public class SharedCache(ILogger<SharedCache> logger, SharedCounters counters)
{
    private readonly ILogger<SharedCache> _logger = logger;
    private readonly SharedCounters _counters = counters;

    private readonly List<GameServerListing> _gameServers = [];
    private readonly List<PlasmaSession> _connections = [];

    public PlasmaSession CreatePlasmaConnection(IEAConnection fesl, string onlineId, string clientString, string partitionId)
    {
        PlasmaSession result = new()
        {
            FeslConnection = fesl,
            UID = _counters.GetNextUserId(),
            LKEY = SharedCounters.GenerateLKey(),
            NAME = onlineId,
            ClientString = clientString,
            PartitionId = partitionId
        };

        _connections.Add(result);
        return result;
    }

    public PlasmaSession AddTheaterConnection(IEAConnection _conn, string lkey)
    {
        var plasma = _connections.SingleOrDefault(x => x.LKEY == lkey) ?? throw new Exception();
        plasma.TheaterConnection = _conn;
        return plasma;
    }

    public PlasmaSession? FindPlayerByName(string playerName)
    {
        return _connections.SingleOrDefault(x => x.NAME == playerName);
    }

    public void RemoveConnection(PlasmaSession plasma)
    {
        var hostedGames = _gameServers.Where(x => x.UID == plasma.UID);
        foreach (var game in hostedGames)
        {
            _gameServers.Remove(game);
        }

        _connections.Remove(plasma);
    }

    public PlasmaSession[] GetConnectedClients()
    {
        return [.. _connections];
    }

    private readonly string[] blacklist = [ "TID", "PID" ];
    public void UpsertGameServerDataByGid(long serverGid, Dictionary<string, string> data)
    {
        if (serverGid < 1)
        {
            _logger.LogWarning("Tried to update server with GID=0");
            return;
        }

        foreach(var item in blacklist)
        {
            data.Remove(item);
        }

        var server = _gameServers.SingleOrDefault(x => x.GID == serverGid);
        if (server is null)
        {
            _gameServers.Add(new()
            {
                GID = serverGid,
                Data = new ConcurrentDictionary<string, string>(data)
            });

            return;
        }

        foreach (var line in data)
        {
            var removed = server.Data.Remove(line.Key, out var _);
            server.Data.TryAdd(line.Key, line.Value);
        }
    }

    public GameServerListing? FindGameWithPlayer(string playerName)
    {
        return _gameServers.FirstOrDefault(x => x.ConnectedPlayers.Any(y => y.NAME.Equals(playerName)));
    }

    public GameServerListing? GetGameByGid(long serverGid)
    {
        if (serverGid is 0) return null;
        return _gameServers.SingleOrDefault(x => x.GID == serverGid);
    }

    public GameServerListing[] GetGameServers()
    {
        return [.. _gameServers];
    }

    public void AddGame(GameServerListing game)
    {
        _gameServers.Add(game);
    }

    public void RemoveGame(GameServerListing game)
    {
        _gameServers.Remove(game);
    }
}