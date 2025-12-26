using System.Collections.Concurrent;

namespace Arcadia.EA;

public class GameServerListing
{
    public IEAConnection? TheaterConnection { get; init; }
    public ConcurrentDictionary<string, string> Data { get; init; } = new();

    public required string PartitionId { get; init; }
    public required string Platform { get; init; }

    public ulong UID { get; init; }
    public long GID { get; init; }
    public int LID { get; init; }

    public string UGID { get; init; } = string.Empty;
    public string SECRET { get; init; } = string.Empty;
    public string EKEY { get; init; } = string.Empty;
    public string NAME { get; init; } = string.Empty;

    public ConcurrentQueue<PlasmaSession> JoiningPlayers { get; init; } = [];
    public ConcurrentDictionary<ulong, PlasmaSession> ConnectedPlayers { get; init; } = [];

    public bool CanJoin { get; set; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
}