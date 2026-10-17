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

    public bool CanJoin { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    public ConcurrentDictionary<ulong, PlasmaSession> ConnectedPlayers { get; init; } = [];

    private ConcurrentDictionary<long, PlasmaSession> _joinQueue { get; init; } = [];
    private long _pid = 0;

    public void EnqueuePlayer(PlasmaSession playerSession)
    {
        var pid = ++_pid;
        playerSession.PID = pid;
        _joinQueue[pid] = playerSession;
    }

    public PlasmaSession? DequeuePlayer(int pid)
    {
        if (1 > pid) return null;
        if (!_joinQueue.TryRemove(pid, out var player)) return null;
        return player;
    }
}