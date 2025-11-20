using Xunit;
using AutoFixture;
using AutoFixture.AutoMoq;
using Arcadia;
using System.Data;
using Microsoft.Data.Sqlite;
using Moq;
using Dapper;
using Castle.DynamicProxy;

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
    public void GetStaticStats_Returns()
    {
        sqlite.Execute("INSERT INTO static_stats (ClientString, Key, Value) VALUES ('Game01', 'MP', '1.0')");
        sqlite.Execute("INSERT INTO static_stats (ClientString, Key, Value) VALUES ('Game01', 'SP', '99.0')");
        sqlite.Execute("INSERT INTO static_stats (ClientString, Key, Value) VALUES ('Game01', 'NP', '-1.0')");

        var actual = db.GetStaticStats("Game01", ["MP", "SP"]);

        Assert.Collection(actual,
        x =>
        {
            Assert.Equal("MP", x.Key);
            Assert.Equal("1.0", x.Value);
        },
        x =>
        {
            Assert.Equal("SP", x.Key);
            Assert.Equal("99.0", x.Value);
        });
    }
}