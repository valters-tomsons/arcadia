using System.Collections.Frozen;
using System.Data;
using Arcadia.EA;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NPTicket;

namespace Arcadia.Storage;

public sealed class Database
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly bool _initialized;
    private ulong _defaultUserId = 1000000000000;

    public Database(ILogger<Database> logger, IServiceProvider serviceProvider, IOptions<DebugSettings> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        if (options.Value.DisableDatabase) return;

        try
        {
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

            conn.Execute(
            """
            CREATE TABLE IF NOT EXISTS users (
                UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                PlatformId INTEGER NULL,

                UNIQUE(Username, Platform)
            );

            INSERT OR IGNORE INTO sqlite_sequence (name, seq) 
            SELECT 'users', 1000000000000 
            WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = 'users');
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

            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();
            conn.Execute("INSERT INTO server_startup DEFAULT VALUES");
    }

    public void RecordOnslaughtCompletion(OnslaughtLevelCompleteMessage[] messages)
    {
        if (messages.Length == 0 || !_initialized) return;

        try
        {
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
    }

    public void RecordLoginMetric(Ticket ticket, string platformName)
    {
        if (!_initialized) return;

        try
        {
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
    }

    public IReadOnlyDictionary<string, string> GetStatsByUser(PlasmaUser user, string partitionId, string[] keys)
    {
        if (!_initialized ||
            keys.Length == 0 ||
            string.IsNullOrWhiteSpace(user.Username) ||
            string.IsNullOrWhiteSpace(user.Platform)
        ) return FrozenDictionary<string, string>.Empty;

        var subdomain = partitionId.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(subdomain)) return FrozenDictionary<string, string>.Empty;

        try
        {
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
                user.Username,
                user.Platform,
                Subdomain = subdomain,
                Keys = keys
            })?.ToDictionary(
                row => (string)row.Key!,
                row => (string)row.Value!
            );

            return results ?? [];
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get stats: {Message}", e.Message);
            return FrozenDictionary<string, string>.Empty;
        }
    }

    /// <summary>
    /// Applies rank UpdateStats updates. Type (ut) is 0 set, 1 high, 2 low, 3 increment, 4 decrement. Games send values
    /// as offsets from their own defaults, so a missing value counts as 0.
    /// </summary>
    public void UpdateStatsByUser(PlasmaUser user, string partitionId, IEnumerable<(string Key, int Type, double Value)> stats)
    {
        if (!_initialized ||
            string.IsNullOrWhiteSpace(user.Username) ||
            string.IsNullOrWhiteSpace(user.Platform)
        ) return;

        var subdomain = partitionId.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(subdomain)) return;

        try
        {
            var updates = stats.Select(x => new
            {
                user.Username,
                user.Platform,
                Subdomain = subdomain,
                x.Key,
                x.Type,
                x.Value
            });

            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

            conn.Execute(
            """
            INSERT OR REPLACE INTO stats (Username, Platform, Subdomain, Key, Value)
            SELECT @Username, @Platform, @Subdomain, @Key,
                CASE @Type WHEN 1 THEN MAX(Current, @Value) WHEN 2 THEN MIN(Current, @Value) WHEN 3 THEN Current + @Value WHEN 4 THEN Current - @Value ELSE @Value END
            FROM (SELECT COALESCE((SELECT CAST(Value AS REAL) FROM stats WHERE Username = @Username AND Platform = @Platform AND Subdomain = @Subdomain AND Key = @Key), 0) AS Current)
            """,
            updates);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update stats: {Message}", e.Message);
        }
    }

    public double SumStats(string partitionId, string key)
    {
        if (!_initialized) return 0;

        var subdomain = partitionId.Split('/').LastOrDefault();

        try
        {
            using var conn = _serviceProvider.GetRequiredService<IDbConnection>();
            return conn.ExecuteScalar<double?>("SELECT SUM(CAST(Value AS REAL)) FROM stats WHERE Subdomain = @Subdomain AND Key = @Key", new { Subdomain = subdomain, Key = key }) ?? 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to sum stats: {Message}", e.Message);
            return 0;
        }
    }

    public PlasmaUser GetOrCreateUser(string username, string platformName, ulong? platformId)
    {
        if (!_initialized)
        {
            return new()
            {
                UserId = Interlocked.Increment(ref _defaultUserId),
                Platform = platformName,
                Username = username
            };
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformName);

        using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

        var user = conn.QueryFirstOrDefault<PlasmaUser?>(
        """
            SELECT * FROM users 
            WHERE Username = @Username AND Platform = @Platform;
            """,
        new { Username = username, Platform = platformName });

        if (user is not null) return user;

        var userId = conn.ExecuteScalar<ulong>(
        """
            INSERT INTO users (Username, Platform, PlatformId) 
            VALUES (@Username, @Platform, @PlatformId)
            RETURNING UserId;
            """,
        new { Username = username, Platform = platformName, PlatformId = platformId });

        return new()
        {
            UserId = userId,
            Username = username,
            Platform = platformName,
            PlatformId = platformId
        };
    }

    public PlasmaUser? FindUserByName(string username, string prefferedPlatform)
    {
        if (!_initialized) return null;

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefferedPlatform);

        using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

        return conn.QueryFirstOrDefault<PlasmaUser?>(
        """
            SELECT * FROM users 
            WHERE Username = @Username
            ORDER BY 
                CASE WHEN Platform = @Platform THEN 0 ELSE 1 END,
                Platform
            """,
        new { Username = username, Platform = prefferedPlatform });
    }

    public PlasmaUser? FindUserById(ulong userId)
    {
        if (!_initialized) return null;

        ArgumentOutOfRangeException.ThrowIfZero(userId);

        using var conn = _serviceProvider.GetRequiredService<IDbConnection>();

        return conn.QueryFirstOrDefault<PlasmaUser?>(
        """
            SELECT * FROM users 
            WHERE UserId = @UserId
            """,
        new { UserId = userId });
    }
}