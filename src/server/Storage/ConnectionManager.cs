using System.Collections.Immutable;
using Arcadia.EA;
using Microsoft.Extensions.Logging;

namespace Arcadia.Storage;

public class ConnectionManager(ILogger<ConnectionManager> logger, SharedCounters counters)
{
    private static readonly ImmutableArray<string> DataKeyBlacklist = ["TID", "PID"];

    private readonly ILogger<ConnectionManager> _logger = logger;
    private readonly SharedCounters _counters = counters;

    private readonly List<GameServerListing> _gameServers = [];
    private readonly List<PlasmaSession> _connections = [];

    private readonly SemaphoreSlim _semaphore = new(1);

    public async Task<PlasmaSession> CreatePlasmaConnection(IEAConnection fesl, string onlineId, string clientString, string partitionId)
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

        await _semaphore.WaitAsync();

        try
        {
            _connections.Add(result);
        }
        finally
        {
            _semaphore.Release();
        }

        return result;
    }

    public PlasmaSession PairTheaterConnection(IEAConnection theater, string lkey)
    {
        var plasma = _connections.SingleOrDefault(x => x.LKEY == lkey) ?? throw new Exception("Failed to find a plasma session pair!");
        plasma.TheaterConnection = theater;
        return plasma;
    }

    public async Task RemovePlasmaSession(PlasmaSession plasma)
    {
        if (!_connections.Contains(plasma))
        {
            return;
        }

        await _semaphore.WaitAsync();

        try
        {
            _gameServers.RemoveAll(x => x.UID == plasma.UID && x.PartitionId == plasma.PartitionId);

            var sessionInGame = FindGameWithPlayerByUid(plasma.PartitionId, plasma.UID);
            sessionInGame?.ConnectedPlayers.Remove(plasma.UID, out var _);

            _connections.Remove(plasma);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddGameListing(GameServerListing game, Dictionary<string, string> data)
    {
        foreach (var line in data)
        {
            if (DataKeyBlacklist.Contains(line.Key)) continue;
            game.Data.TryAdd(line.Key, line.Value);
        }

        await _semaphore.WaitAsync();

        try
        {
            _gameServers.Add(game);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async void AddFakeListing(GameServerListing game)
    {
        game.Data["FAKE"] = "1";
        _gameServers.Add(game);
    }

    public async Task RemoveGameListing(GameServerListing game)
    {
        await _semaphore.WaitAsync();

        try
        {
            _gameServers.Remove(game);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public PlasmaSession? FindPartitionSessionByUser(string partitionId, string playerName)
    {
        return _connections.SingleOrDefault(x => x.PartitionId == partitionId && x.NAME == playerName);
    }

    public PlasmaSession? FindSessionByLkey(string lkey)
    {
        return _connections.SingleOrDefault(x => x.LKEY == lkey);
    }

    public PlasmaSession? FindSessionByUID(long uid)
    {
        return _connections.SingleOrDefault(x => x.UID == uid);
    }

    public async Task UpsertGameServerDataByGid(string partitionId, long serverGid, IDictionary<string, string> data)
    {
        if (serverGid < 1)
        {
            _logger.LogWarning("Tried to update server with GID=0");
            return;
        }

        foreach (var item in DataKeyBlacklist)
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

        await _semaphore.WaitAsync();

        try
        {
            _gameServers.Add(new()
            {
                GID = serverGid,
                Data = new(data),
                PartitionId = partitionId
            });

        }
        finally
        {
            _semaphore.Release();
        }
    }

    public GameServerListing? FindGameWithPlayer(string partitionId, string playerName)
    {
        return _gameServers.FirstOrDefault(x => x.ConnectedPlayers.Values.Any(y => y.PartitionId == partitionId && y.NAME.Equals(playerName)));
    }

    public GameServerListing? FindGameWithPlayerByUid(string partitionId, long uid)
    {
        return _gameServers.FirstOrDefault(x => x.ConnectedPlayers.Any(x => x.Key == uid && x.Value.PartitionId == partitionId));
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