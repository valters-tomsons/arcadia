using Arcadia.EA;
using static Arcadia.EA.Constants.FeslTransmissionType;
using AutoFixture;
using AutoFixture.AutoMoq;
using Moq;
using Xunit;
using System.Collections.Concurrent;
using Arcadia.EA.Handlers;

namespace tests;

public class FeslHandlerTests
{
    private readonly IFixture fixture;
    private readonly Mock<IEAConnection> mockConnection;
    private readonly FeslHandler handler;

    private readonly ConcurrentQueue<Packet> responses = new();

    public FeslHandlerTests()
    {
        fixture = new Fixture().Customize(new AutoMoqCustomization());
        mockConnection = fixture.Freeze<Mock<IEAConnection>>();

        mockConnection.Setup(x => x.SendPacket(It.IsAny<Packet>(), It.IsAny<CancellationToken>()))
                      .Callback<Packet, CancellationToken>((packet, ct) => responses.Enqueue(packet))
                      .Returns(Task.FromResult(true))
                      .Verifiable();

        handler = fixture.Create<FeslHandler>();
    }

    [Theory]
    [InlineData("beach-ps3")]
    public async Task ClientHello_Responds_ServerHelloAndMemCheck(string clientString, string? clientType = null)
    {
        Dictionary<string, string> requestData = new() {
            ["TXN"] = "Hello",
            ["clientString"] = clientString,
            ["sku"] = "ps3"
        };
        if (clientType is not null) requestData.Add("clientType", clientType);
        var request = new Packet("fsys", SinglePacketRequest, 0, requestData);

        await handler.HandlePacket(request);

        mockConnection.Verify();
        Assert.True(responses.TryDequeue(out var serverHello));
        Assert.Equal("fsys", serverHello.Type);
        Assert.Equal("Hello", serverHello.TXN);
        Assert.Equal("ps3", serverHello["domainPartition.domain"]);

        Assert.True(responses.TryDequeue(out var memCheck));
        Assert.Equal("fsys", memCheck .Type);
        Assert.Equal("MemCheck", memCheck.TXN);
    }
}