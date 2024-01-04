using Arcadia.EA;
using static Arcadia.EA.Constants.FeslTransmissionType;
using Arcadia.Handlers;
using AutoFixture;
using AutoFixture.AutoMoq;
using Moq;
using Xunit;
using System.Collections.Concurrent;

namespace tests;

public class TheaterHandlerTests
{
    private readonly IFixture fixture;
    private readonly Mock<IEAConnection> mockConnection;
    private readonly TheaterClientHandler handler;

    private readonly ConcurrentQueue<Packet> responses = new();

    public TheaterHandlerTests()
    {
        fixture = new Fixture().Customize(new AutoMoqCustomization());
        mockConnection = fixture.Freeze<Mock<IEAConnection>>();

        mockConnection.Setup(x => x.SendPacket(It.IsAny<Packet>()))
                      .Callback<Packet>(responses.Enqueue)
                      .Returns(Task.FromResult(true))
                      .Verifiable();

        handler = fixture.Create<TheaterClientHandler>();
    }

    [Theory]
    [InlineData("PS3", "0")]
    public async Task ClientConn_ServerResponds(string plat, string prot)
    {
        Dictionary<string, object> requestData = new() {
            ["TID"] = 0,
            ["PLAT"] = plat,
            ["PROT"] = prot
        };
        var request = new Packet("CONN", SinglePacketRequest, 0, requestData);

        await handler.HandlePacket(request);

        mockConnection.Verify();
        Assert.NotEmpty(responses);
        Assert.True(responses.TryDequeue(out var response));
        Assert.Equal("CONN", response.Type);
        Assert.NotEqual(0, (long)response.DataDict["TIME"]);
    }
}