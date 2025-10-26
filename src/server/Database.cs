using Arcadia.Storage;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NPTicket;

namespace Arcadia;

public sealed class Database : IDisposable
{
    private readonly ILogger _logger;
    private readonly SqliteConnection _connection;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly bool _initialized;

    public Database(ILogger<Database> logger, SqliteConnection connection)
    {
        _connection = connection;
        _logger = logger;

        try
        {
            _connection.Execute("PRAGMA journal_mode=WAL;");

            _connection.Execute("CREATE TABLE IF NOT EXISTS server_startup (started_at DATETIME DEFAULT CURRENT_TIMESTAMP)");

            _connection.Execute(
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

            _connection.Execute(
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

            _initialized = true;
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to initialize database, it will not be used!");
        }
    }

    public void RecordStartup()
    {
        if (!_initialized) return;

        try
        {
            _lock.Wait();
            _connection.Execute("INSERT INTO server_startup DEFAULT VALUES");
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RecordOnslaughtCompletion(OnslaughtLevelCompleteMessage msg)
    {
        if (!_initialized) return;

        try
        {
            _lock.Wait();

            _connection.Execute(
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
            )
            """,
            new
            {
                msg.MapKey,
                msg.Difficulty,
                msg.PlayerName,
                GameTime = msg.GameTime.ToString()
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordLoginMetric(Ticket ticket)
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
            await _lock.WaitAsync();

            _connection.Execute(
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
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}