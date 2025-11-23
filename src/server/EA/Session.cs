using System.Collections.Concurrent;

namespace Arcadia.EA;

public class PlasmaSession
{
    public IEAConnection? FeslConnection { get; set; }
    public IEAConnection? TheaterConnection { get; set; }

    public int PID { get; set; }

    public required long UID { get; init; }
    public required string NAME { get; init; }
    public required string LKEY { get; init; }

    public required string ClientString { get; init; }
    public required string PartitionId { get; init; }
}

public class GameServerListing
{
    public IEAConnection? TheaterConnection { get; init; }
    public ConcurrentDictionary<string, string> Data { get; init; } = new();

    public required string PartitionId { get; init; }
    public required string Platform { get; init; }

    public long UID { get; init; }
    public long GID { get; init; }
    public int LID { get; init; }

    public string UGID { get; init; } = string.Empty;
    public string SECRET { get; init; } = string.Empty;
    public string EKEY { get; init; } = string.Empty;
    public string NAME { get; init; } = string.Empty;

    public ConcurrentQueue<PlasmaSession> JoiningPlayers { get; init; } = [];
    public ConcurrentDictionary<long, PlasmaSession> ConnectedPlayers { get; init; } = [];

    public bool CanJoin { get; set; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
}