using System.Collections.Concurrent;

namespace Arcadia.Storage;

public class SharedCache
{
    private readonly ConcurrentDictionary<string, string> _lkeyUsernames = new();

    public void AddUserWithKey(string lkey, string username)
    {
        _lkeyUsernames.TryAdd(lkey, username);
    }

    public string GetUsernameByKey(string lkey)
    {
        return _lkeyUsernames[lkey];
    }
}