using System.Data;
using Arcadia.Storage;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NPTicket;

namespace Arcadia;

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
            CREATE TABLE IF NOT EXISTS static_stats (
                ClientString TEXT NOT NULL,
                Key TEXT NOT NULL,
                Value TEXT NOT NULL,
                UNIQUE(ClientString, Key)
            )
            """);

            try
            {
                conn.Execute(@static.Tf2DbStats.InsertStats);
            }
            catch (Exception e) { _logger.LogWarning(e, "Failed to init stats"); }

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

    public void RecordLoginMetric(Ticket ticket)
    {
        if (!_initialized) return;

        static string GetPlatform(string signature) => signature switch
        {
            "RPCN" => "RPCN",
            "8-�" => "PSN",
            _ => string.Empty
        };

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
                Platform = GetPlatform(ticket.SignatureIdentifier),
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

    public IReadOnlyCollection<(string Key, string Value)> GetStaticStats(string clientString, string[] keys)
    {
        if (!_initialized) return [];

        try
        {
            _lock.EnterReadLock();
            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

            var results = conn.Query<(string Key, string Value)>(
            """
            SELECT Key, Value
            FROM static_stats
            WHERE ClientString = @ClientString AND Key in @Keys
            """,
            new
            {
                ClientString = clientString,
                Keys = keys
            });

            return [.. results];
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to query for stats!");
            return [];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}