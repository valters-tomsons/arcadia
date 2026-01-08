using System.Collections.Immutable;
using Arcadia.EA;
using Microsoft.Extensions.Logging;

namespace Arcadia.Storage;

public class ConnectionManager(ILogger<ConnectionManager> logger, Database db)
{
    private static readonly ImmutableArray<string> DataKeyBlacklist = ["TID", "PID"];

    private readonly ILogger<ConnectionManager> _logger = logger;
    private readonly Database _db = db;

    private readonly List<GameServerListing> _gameServers = [];
    private readonly List<PlasmaSession> _connections = [];

    private readonly SemaphoreSlim _semaphore = new(1);

    public async Task<PlasmaSession> CreatePlasmaConnection(IEAConnection fesl, string onlineId, string clientString, string partitionId, string platform, ulong? platformId)
    {
        var user = _db.GetOrCreateUser(onlineId, platform, platformId);

        if (_connections.Any(x => x.User.UserId == user.UserId)) throw new("Plasma session already has an ongoing connection!");

        PlasmaSession result = new()
        {
            FeslConnection = fesl,
            User = user,
            LKEY = SharedCounters.GenerateLKey(),
            ClientString = clientString,
            PartitionId = partitionId,
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
        var plasma = _connections.SingleOrDefault(x => x.LKEY == lkey) ?? throw new("Failed to find a plasma session pair!");
        plasma.TheaterConnection = theater;
        return plasma;
    }

    public async Task RemovePlasmaSession(PlasmaSession session)
    {
        if (!_connections.Contains(session))
        {
            return;
        }

        await _semaphore.WaitAsync();

        try
        {
            _gameServers.RemoveAll(x => x.UID == session.User.UserId && x.PartitionId == session.PartitionId);

            var sessionInGame = FindGameWithPlayerByUid(session.PartitionId, session.User.UserId);
            sessionInGame?.ConnectedPlayers.Remove(session.User.UserId, out var _);

            _connections.Remove(session);
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
        return _connections.SingleOrDefault(x => x.PartitionId == partitionId && x.User.Username == playerName);
    }

    public PlasmaSession? FindSessionByLkey(string lkey)
    {
        return _connections.SingleOrDefault(x => x.LKEY == lkey);
    }

    public PlasmaSession? FindSessionByUID(ulong uid)
    {
        return _connections.SingleOrDefault(x => x.User.UserId == uid);
    }

    public void UpsertGameServerDataByGid(long serverGid, IDictionary<string, string> data)
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

        var server = _gameServers.SingleOrDefault(x => x.GID == serverGid) ?? throw new("Tried to update non-existant server");
        foreach (var line in data)
        {
            var removed = server.Data.Remove(line.Key, out var _);
            server.Data.TryAdd(line.Key, line.Value);
        }
    }

    public GameServerListing? FindGameWithPlayer(string partitionId, string playerName)
    {
        return _gameServers.FirstOrDefault(x => x.ConnectedPlayers.Values.Any(y => y.PartitionId == partitionId && y.User.Username.Equals(playerName)));
    }

    public GameServerListing? FindGameWithPlayerByUid(string partitionId, ulong uid)
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

    public int GetPartitionPlayerCount(string partitionId)
    {
        return _connections.Count(x => x.PartitionId == partitionId);
    }

    public ImmutableArray<GameServerListing> GetAllServersInternal()
    {
        return [.. _gameServers];
    }
}