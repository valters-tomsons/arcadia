using System.Collections.Concurrent;

namespace Arcadia.EA;

public class PlasmaSession
{
    public IEAConnection? FeslConnection { get; set; }
    public IEAConnection? TheaterConnection { get; set; }

    public string ClientString { get; init; } = string.Empty;
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

    public ConcurrentQueue<PlasmaSession> JoiningPlayers { get; init; } = [];
    public List<PlasmaSession> ConnectedPlayers { get; init; } = [];

    public bool CanJoin { get; set; }
}