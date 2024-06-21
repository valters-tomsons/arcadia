using System.Collections.Concurrent;
using Arcadia.EA;
using Microsoft.Extensions.Logging;

namespace Arcadia.Storage;

public class SharedCache(ILogger<SharedCache> logger)
{
    private readonly ILogger<SharedCache> _logger = logger;

    private readonly ConcurrentDictionary<string, string> _lkeyUsernames = new();
    private readonly ConcurrentBag<GameServerListing> _gameServers = [];
    private readonly ConcurrentBag<TheaterClient> _theaterConnections = [];

    public void AddUserWithLKey(string lkey, string username)
    {
        _lkeyUsernames.TryAdd(lkey, username);
    }

    public string GetUsernameByLKey(string lkey)
    {
        return _lkeyUsernames[lkey];
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
        return [.. _gameServers];
    }

    public void AddTheaterConnection(TheaterClient conn)
    {
        _theaterConnections.Add(conn);
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

public class TheaterClient
{
    public IEAConnection? TheaterConnection { get; init; }

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

    public ConcurrentQueue<TheaterClient> JoiningPlayers { get; init; } = [];
    public ConcurrentBag<TheaterClient> ConnectedPlayers { get; init; } = [];

    public bool CanJoin { get; set; }
}