namespace Arcadia.Storage;

public class SharedCounters
{
    private long _userId = 1000000000000;
    private long _lobbyId = 255;
    private long _ticket = 1500000000;
    private long _gameId = 800000;
    private long _pnowId = 350000;
    private long _pid = 60;
    private long _lid = 255;

    private static Random _random = new();

    public long GetNextUserId()
    {
        return Interlocked.Increment(ref _userId);
    }

    public long GetNextLobbyId()
    {
        return Interlocked.Increment(ref _lobbyId);
    }

    public long GetNextTicket()
    {
        return Interlocked.Increment(ref _ticket);
    }

    public long GetNextGameId()
    {
        return Interlocked.Increment(ref _gameId);
    }

    public long GetNextPnowId()
    {
        return Interlocked.Increment(ref _pnowId);
    }

    public long GetNextPid()
    {
        return Interlocked.Increment(ref _pid);
    }

    public long GetNextLid()
    {
        return Interlocked.Increment(ref _lid);
    }

    public string GetNextLkey()
    {
        var key = _random.Next(100000000, 999999999);
        const string keyTempl = "W5NyZzx{0}Cki6GQAAKDw.";
        return string.Format(keyTempl, key);
    }

    private Stream? _serverStream;

    public void SetServerNetworkStream(Stream stream)
    {
        _serverStream = stream;
    }

    public Stream? GetServerNetworkStream()
    {
        return _serverStream;
    }
}