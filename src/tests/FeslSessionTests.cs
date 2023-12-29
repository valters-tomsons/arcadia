using Arcadia.EA;
using static Arcadia.EA.Constants.FeslTransmissionType;
using Arcadia.Handlers;
using AutoFixture;
using AutoFixture.AutoMoq;
using Moq;
using Xunit;
using System.Collections.Concurrent;

namespace tests;

public class FeslSessionTests
{
    private readonly IFixture fixture;
    private readonly Mock<IEAConnection> mockConnection;
    private readonly FeslHandler handler;

    private readonly ConcurrentQueue<Packet> responses = new();

    public FeslSessionTests()
    {
        fixture = new Fixture().Customize(new AutoMoqCustomization());
        mockConnection = fixture.Freeze<Mock<IEAConnection>>();

        mockConnection.Setup(x => x.SendPacket(It.IsAny<Packet>()))
                      .Callback<Packet>(responses.Enqueue)
                      .Returns(Task.FromResult(true))
                      .Verifiable();

        handler = fixture.Create<FeslHandler>();
    }

    [Theory]
    [InlineData("beach-ps3")]
    public async Task HandlePacket_ClientHello_ServerResponds(string clientString, string? clientType = null)
    {
        Dictionary<string, object> requestData = new() {
            ["TXN"] = "Hello",
            ["clientString"] = clientString };
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