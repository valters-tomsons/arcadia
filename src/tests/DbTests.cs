using Xunit;
using AutoFixture;
using AutoFixture.AutoMoq;
using System.Data;
using Microsoft.Data.Sqlite;
using Moq;
using Dapper;
using Castle.DynamicProxy;
using Arcadia.EA;
using Arcadia.Storage;

namespace tests;

public sealed class DbTests
{
    internal class NoDisposeInterceptor : IInterceptor
    {
        public void Intercept(IInvocation invocation)
        {
            if (invocation.Method.Name == "Dispose") return;
            invocation.Proceed();
        }
    }

    private readonly IFixture fixture;
    private readonly Mock<IServiceProvider> serviceMock;
    private readonly Database db;

    private readonly SqliteConnection sqlite = new("Data Source=DbTests;Mode=Memory;Cache=Shared");

    public DbTests()
    {
        fixture = new Fixture().Customize(new AutoMoqCustomization());

        sqlite.Open();

        var generator = new ProxyGenerator();
        var connProxy = generator.CreateInterfaceProxyWithTarget<IDbConnection>(sqlite, new NoDisposeInterceptor());

        serviceMock = fixture.Freeze<Mock<IServiceProvider>>();
        serviceMock.Setup(x => x.GetService(typeof(IDbConnection))).Returns(connProxy);

        db = fixture.Create<Database>();
    }

    [Fact]
    public void RecordStartup_Inserts()
    {
        var initialCount = sqlite.ExecuteScalar<int>("SELECT COUNT(*) FROM server_startup");
        Assert.Equal(0, initialCount);

        db.RecordStartup();
        var startedAt = sqlite.ExecuteScalar<string>("SELECT started_at FROM server_startup");
        Assert.True(DateTime.TryParse(startedAt, out var _));

        var afterCount = sqlite.ExecuteScalar<int>("SELECT COUNT(*) FROM server_startup");
        Assert.Equal(1, afterCount);
    }

    [Fact]
    public void UpdateStats_AppliesUpdateTypes()
    {
        var user = new PlasmaUser { UserId = 1, Username = "statsUser", Platform = "ps3" };
        string[] keys = ["set", "high", "low", "inc", "dec"];

        db.UpdateStatsByUser(user, "/ps3/BEACH", [("set", 0, 5), ("high", 1, -2), ("low", 2, 3), ("inc", 3, 2), ("dec", 4, 2)]);
        db.UpdateStatsByUser(user, "/ps3/BEACH", [("set", 0, 1), ("high", 1, 4), ("low", 2, -1), ("inc", 3, 2.5), ("dec", 4, 1)]);

        var stats = db.GetStatsByUser(user, "/ps3/BEACH", keys).ToDictionary(x => x.Key, x => double.Parse(x.Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new Dictionary<string, double> { ["set"] = 1, ["high"] = 4, ["low"] = -1, ["inc"] = 4.5, ["dec"] = -3 }, stats);
        Assert.Empty(db.GetStatsByUser(user, "/ps3/BFBC2", keys));
    }
}
