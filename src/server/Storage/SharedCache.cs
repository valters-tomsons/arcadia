using System.Collections.Concurrent;
using Arcadia.EA;
using Microsoft.Extensions.Logging;

namespace Arcadia.Storage;

public class SharedCache(ILogger<SharedCache> logger, SharedCounters counters)
{
    private readonly ILogger<SharedCache> _logger = logger;
    private readonly SharedCounters _counters = counters;

    private readonly ConcurrentBag<GameServerListing> _gameServers = [];
    private readonly ConcurrentBag<PlasmaConnection> _connections = [];

    public PlasmaConnection CreatePlasmaConnection(IEAConnection fesl, string onlineId)
    {
        PlasmaConnection result = new()
        {
            FeslConnection = fesl,
            UID = _counters.GetNextUserId(),
            LKEY = SharedCounters.GenerateLKey(),
            NAME = onlineId
        };

        _connections.Add(result);
        return result;
    }

    public PlasmaConnection AddTheaterConnection(IEAConnection _conn, string lkey)
    {
        var plasma = _connections.SingleOrDefault(x => x.LKEY == lkey) ?? throw new Exception();
        plasma.TheaterConnection = _conn;
        return plasma;
    }

    public void RemoveConnection(PlasmaConnection plasma)
    {
        var hostedGames = _gameServers.Where(x => x.UID == plasma.UID);
        foreach (var game in hostedGames)
        {
            _gameServers.RemoveItemFromBag(game);
        }

        _connections.RemoveItemFromBag(plasma);
    }

    public PlasmaConnection[] GetConnectedClients()
    {
#pragma warning disable IDE0305 // Simplify collection initialization
        return _connections.ToArray();
#pragma warning restore IDE0305 // Simplify collection initialization
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

    public GameServerListing? GetGameByGid(long serverGid)
    {
        if (serverGid is 0) return null;
        return _gameServers.SingleOrDefault(x => x.GID == serverGid);
    }

    public long[] ListGameGids()
    {
        return _gameServers.Select(x => x.GID).ToArray();
    }

    public GameServerListing[] GetGameServers()
    {
#pragma warning disable IDE0305 // Simplify collection initialization
        return _gameServers.ToArray();
#pragma warning restore IDE0305 // Simplify collection initialization
    }

    public void AddGame(GameServerListing game)
    {
        _gameServers.Add(game);
    }

    public void RemoveGame(GameServerListing game)
    {
        _gameServers.RemoveItemFromBag(game);
    }
}

public class PlasmaConnection
{
    public IEAConnection? FeslConnection { get; set; }
    public IEAConnection? TheaterConnection { get; set; }

    public long UID { get; init; }
    public string NAME { get; init; } = string.Empty;
    public string LKEY { get; init; } = string.Empty;
    public int PID { get; set; }
}

public class GameServerListing
{
    public IEAConnection? TheaterConnection { get; init; }
    public ConcurrentDictionary<string, string> Data { get; init; } = new();

    public long UID { get; init; }
    public long GID { get; init; }
    public int LID { get; init; }

    public string UGID { get; init; } = string.Empty;
    public string SECRET { get; init; } = string.Empty;
    public string EKEY { get; init; } = string.Empty;
    public string NAME { get; init; } = string.Empty;

    public ConcurrentQueue<PlasmaConnection> JoiningPlayers { get; init; } = [];
    public ConcurrentBag<PlasmaConnection> ConnectedPlayers { get; init; } = [];

    public bool CanJoin { get; set; }
}