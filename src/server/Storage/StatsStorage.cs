using System.Collections.Concurrent;

namespace Arcadia.Storage;

public record OnslaughtLevelCompleteMessage
{
    public required string PlayerName { get; init; }
    public required string MapKey { get; init; }
    public required string Difficulty { get; init; }
    public required TimeSpan GameTime { get; init; }
}

public class StatsStorage
{
    private readonly ConcurrentQueue<OnslaughtLevelCompleteMessage> _onslaughtStats = new();

    public void PostLevelComplete(OnslaughtLevelCompleteMessage msg)
    {
        _onslaughtStats.Enqueue(msg);
    }

    public OnslaughtLevelCompleteMessage? GetLevelComplete()
    {
        var dequeued = _onslaughtStats.TryDequeue(out var msg);
        if (!dequeued || msg is null) return null;
        return msg;
    }
}