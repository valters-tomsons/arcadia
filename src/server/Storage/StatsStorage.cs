using System.Collections.Concurrent;

namespace Arcadia.Storage;

public record OnslaughtLevelCompleteMessage
{
    public required string PlayerName { get; init; }
    public required string MapKey { get; init; }
    public required string Difficulty { get; init; }
    public required TimeSpan GameTime { get; init; }
}

public class StatsStorage(Database db)
{
    private readonly ConcurrentQueue<OnslaughtLevelCompleteMessage> _onslaughtStats = new();
    private readonly Database _db = db;

    public void PostLevelComplete(OnslaughtLevelCompleteMessage msg)
    {
        _onslaughtStats.Enqueue(msg);
        _db.RecordOnslaughtCompletion(msg);
    }

    public OnslaughtLevelCompleteMessage? GetLevelComplete()
    {
        var dequeued = _onslaughtStats.TryDequeue(out var msg);
        if (!dequeued || msg is null) return null;
        return msg;
    }
}