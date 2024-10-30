using System.Collections.Concurrent;
using System.Collections.Immutable;
using Arcadia.EA;
using Microsoft.Extensions.Logging;

namespace Arcadia.Storage;

public class SharedCache(ILogger<SharedCache> logger, SharedCounters counters)
{
    private static readonly ImmutableArray<string> DataKeyBlacklist = ["TID", "PID"];

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

    public PlasmaSession AddTheaterConnection(IEAConnection theater, string lkey)
    {
        var plasma = _connections.SingleOrDefault(x => x.LKEY == lkey) ?? throw new Exception();
        plasma.TheaterConnection = theater;
        return plasma;
    }

    public void RemovePlasmaSession(PlasmaSession plasma)
    {
        var hostedGames = _gameServers.Where(x => x.UID == plasma.UID);
        foreach (var game in hostedGames)
        {
            _gameServers.Remove(game);
        }

        _connections.Remove(plasma);
    }

    public void AddGameListing(GameServerListing game)
    {
        _gameServers.Add(game);
    }

    public void RemoveGameListing(GameServerListing game)
    {
        _gameServers.Remove(game);
    }

    public PlasmaSession? FindPartitionSessionByUser(string partitionId, string playerName)
    {
        return _connections.SingleOrDefault(x => x.PartitionId == partitionId && x.NAME == playerName);
    }

    public PlasmaSession? FindSessionByLkey(string lkey)
    {
        return _connections.SingleOrDefault(x => x.LKEY == lkey);
    }

    public void UpsertGameServerDataByGid(string partitionId, long serverGid, IDictionary<string, string> data)
    {
        if (serverGid < 1)
        {
            _logger.LogWarning("Tried to update server with GID=0");
            return;
        }

        foreach(var item in DataKeyBlacklist)
        {
            data.Remove(item);
        }

        var server = _gameServers.SingleOrDefault(x => x.GID == serverGid);
        if (server is not null)
        {
            foreach (var line in data)
            {
                var removed = server.Data.Remove(line.Key, out var _);
                server.Data.TryAdd(line.Key, line.Value);
            }

            return;
        }

        _gameServers.Add(new()
        {
            GID = serverGid,
            Data = new ConcurrentDictionary<string, string>(data),
            PartitionId = partitionId
        });
    }

    public GameServerListing? FindGameWithPlayer(string partitionId, string playerName)
    {
        return _gameServers.FirstOrDefault(x => x.ConnectedPlayers.Any(y => y.PartitionId == partitionId && y.NAME.Equals(playerName)));
    }

    public GameServerListing? GetGameByGid(string partitionId, long serverGid)
    {
        if (serverGid is 0) return null;
        return _gameServers.SingleOrDefault(x => x.PartitionId == partitionId && x.GID == serverGid);
    }

    public ImmutableArray<GameServerListing> GetPartitionServers(string partitionId)
    {
        return _gameServers.Where(x => x.PartitionId == partitionId).ToImmutableArray();
    }

    public ImmutableArray<GameServerListing> GetAllServers()
    {
        return [.. _gameServers];
    }
}