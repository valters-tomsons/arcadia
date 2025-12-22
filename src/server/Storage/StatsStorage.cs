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
    private readonly Database _db = db;
    private readonly ConcurrentQueue<OnslaughtLevelCompleteMessage> _onslaughtStats = new();

    public int QueueCount => _onslaughtStats.Count;

    public void PostLevelComplete(OnslaughtLevelCompleteMessage[] messages)
    {
        _db.RecordOnslaughtCompletion(messages);

        foreach (var msg in messages)
            _onslaughtStats.Enqueue(msg);
    }

    public OnslaughtLevelCompleteMessage? DequeueCompletion()
    {
        var dequeued = _onslaughtStats.TryDequeue(out var msg);
        if (!dequeued || msg is null) return null;
        return msg;
    }
}