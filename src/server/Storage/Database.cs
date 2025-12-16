using System.Collections.Immutable;
using System.Data;
using Arcadia.EA;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NPTicket;

namespace Arcadia.Storage;

public sealed class Database : IDisposable
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly bool _initialized;

    public Database(ILogger<Database> logger, IServiceProvider serviceProvider, IOptions<DebugSettings> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        if (options.Value.DisableDatabase) return;

        try
        {
            _lock.EnterWriteLock();

            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

            conn.Execute("PRAGMA journal_mode=WAL;");

            conn.Execute("CREATE TABLE IF NOT EXISTS server_startup (started_at DATETIME DEFAULT CURRENT_TIMESTAMP)");

            conn.Execute(
            """
            CREATE TABLE IF NOT EXISTS onslaught_stats (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                MapKey TEXT NOT NULL,
                Difficulty TEXT NOT NULL,
                PlayerName TEXT NOT NULL,
                GameTime TEXT NOT NULL,
                FinishedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            )
            """);

            conn.Execute(
            """
            CREATE TABLE IF NOT EXISTS login_metrics (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                GameID TEXT NOT NULL,

                FirstLoginDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                LastLoginDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                LoginCount INTEGER DEFAULT 1,

                UNIQUE(Username, Platform, GameID)
            )
            """);

            conn.Execute(
            """
            CREATE TABLE IF NOT EXISTS stats (
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                Subdomain TEXT NOT NULL,
                Key TEXT NOT NULL,

                Value TEXT NOT NULL,

                PRIMARY KEY (Username, Platform, Subdomain, Key)
            ) WITHOUT ROWID
            """);

            _initialized = true;
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to initialize database, it will not be used!");
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void RecordStartup()
    {
        if (!_initialized) return;

        try
        {
            _lock.EnterWriteLock();
            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();
            conn.Execute("INSERT INTO server_startup DEFAULT VALUES");
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void RecordOnslaughtCompletion(OnslaughtLevelCompleteMessage[] messages)
    {
        if (messages.Length == 0 || !_initialized) return;

        try
        {
            _lock.EnterWriteLock();
            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

            foreach (var msg in messages)
            {
                conn.Execute(
                """
                INSERT INTO onslaught_stats (
                    MapKey, 
                    Difficulty,
                    PlayerName,
                    GameTime
                ) VALUES (
                    @MapKey, 
                    @Difficulty, 
                    @PlayerName, 
                    @GameTime
                );
                """,
                new
                {
                    msg.MapKey,
                    msg.Difficulty,
                    msg.PlayerName,
                    GameTime = msg.GameTime.ToString()
                });
            }
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to record onslaught stats!");
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void RecordLoginMetric(Ticket ticket, string platformName)
    {
        if (!_initialized) return;


        try
        {
            _lock.EnterWriteLock();
            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

            conn.Execute(
            """
            INSERT INTO login_metrics (Username, Platform, GameID) 
            VALUES (@Username, @Platform, @GameID)
            ON CONFLICT(Username, Platform, GameID) 
            DO UPDATE SET 
                LoginCount = LoginCount + 1,
                LastLoginDate = CURRENT_TIMESTAMP
            """,
            new
            {
                ticket.Username,
                Platform = platformName,
                GameID = ticket.TitleId
            });
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to record login metric!");
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public ImmutableDictionary<string, string> GetStatsBySession(PlasmaSession session, string[] keys)
    {
        if (!_initialized ||
            keys.Length == 0 ||
            string.IsNullOrWhiteSpace(session.NAME) ||
            string.IsNullOrWhiteSpace(session.OnlinePlatformId)
        ) return ImmutableDictionary<string, string>.Empty;

        var subdomain = session.PartitionId.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(subdomain)) return ImmutableDictionary<string, string>.Empty;

        try
        {
            _lock.EnterReadLock();
            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

            var results = conn.Query(
            """
            SELECT Key, Value
            FROM stats
            WHERE 
                Username = @Username AND
                Platform = @Platform AND
                Subdomain = @Subdomain AND
                Key in @Keys
            """,
            new
            {
                Username = session.NAME,
                Platform = session.OnlinePlatformId,
                Subdomain = subdomain,
                Keys = keys
            })?.ToDictionary(
                row => (string)row.Key!,
                row => (string)row.Value!
            );

            return results?.ToImmutableDictionary() ?? throw new("Database query returned null");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get stats: {Message}", e.Message);
            return ImmutableDictionary<string, string>.Empty;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void SetStatsBySession(PlasmaSession session, IDictionary<string, string> stats)
    {
        if (!_initialized ||
            stats.Count == 0 ||
            string.IsNullOrWhiteSpace(session.NAME) ||
            string.IsNullOrWhiteSpace(session.OnlinePlatformId)
        ) return;

        var subdomain = session.PartitionId.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(subdomain)) return;

        try
        {
            var updates = stats.Select(x => new
            {
                Username = session.NAME,
                Platform = session.OnlinePlatformId,
                Subdomain = subdomain,
                x.Key,
                x.Value
            });

            _lock.EnterWriteLock();
            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

            conn.Execute(
            """
            INSERT OR REPLACE INTO stats (Username, Platform, Subdomain, Key, Value) VALUES (@Username, @Platform, @Subdomain, @Key, @Value)
            """, 
            updates);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get stats: {Message}", e.Message);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}