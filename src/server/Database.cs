using Arcadia.Storage;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NPTicket;

namespace Arcadia;

public sealed class Database
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly bool _initialized;

    public Database(ILogger<Database> logger, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        try
        {
            using var conn = _serviceProvider.GetRequiredService<SqliteConnection>();

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

        using var conn = _serviceProvider.GetRequiredService<SqliteConnection>();
        conn.Execute("INSERT INTO server_startup DEFAULT VALUES");
    }

    public void RecordOnslaughtCompletion(OnslaughtLevelCompleteMessage msg)
    {
        if (!_initialized) return;

        using var conn = _serviceProvider.GetRequiredService<SqliteConnection>();
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

    public async Task RecordLoginMetric(Ticket ticket)
    {
        if (!_initialized) return;

        static string GetPlatform(string signature) => signature switch
        {
            "RPCN" => "RPCN",
            "8-�" => "PSN",
            _ => string.Empty
        };


        using var conn = _serviceProvider.GetRequiredService<SqliteConnection>();

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
}