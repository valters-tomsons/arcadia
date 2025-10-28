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
        db.RecordStartup();
        var now = DateTime.UtcNow;

        var startedAt = sqlite.ExecuteScalar<string>("SELECT started_at FROM server_startup");
        var startCount = sqlite.ExecuteScalar<int>("SELECT COUNT(*) FROM server_startup");

        Assert.NotNull(startedAt);
        Assert.True(now - DateTime.Parse(startedAt) < TimeSpan.FromMinutes(1));
        Assert.Equal(1, startCount);
    }
}