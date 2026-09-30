using Arcadia.EA;
using static Arcadia.EA.Constants.FeslTransmissionType;
using Xunit;

namespace tests;

public class EAConnectionTests
{
    private sealed class ChunkedStream(byte[] data, int chunkSize) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, chunkSize), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(100)]
    [InlineData(8192)]
    public async Task ReceiveAsync_ReassemblesPacketsAcrossArbitraryReads(int chunkSize)
    {
        var sent = new[]
        {
            new Packet("fsys", SinglePacketRequest, 1, new() { ["TXN"] = "Ping" }),
            new Packet("acct", SinglePacketRequest, 2, new() { ["TXN"] = "NuPS3Login", ["macAddr"] = "$000000000000" }),
            new Packet("PING", SinglePacketRequest, 0, new() { ["TID"] = "7" }),
        };

        var wire = new List<byte>();
        foreach (var packet in sent) wire.AddRange(await packet.Serialize());

        var connection = new EAConnection();
        connection.Initialize(new ChunkedStream([.. wire], chunkSize), "127.0.0.1:1", "127.0.0.1:2", CancellationToken.None);

        var received = new List<Packet>();
        await foreach (var packet in connection.ReceiveAsync(null)) received.Add(packet);

        Assert.Equal(sent.Select(p => p.Type), received.Select(p => p.Type));
        Assert.Equal("Ping", received[0]["TXN"]);
        Assert.Equal("$000000000000", received[1]["macAddr"]);
        Assert.Equal("7", received[2]["TID"]);
    }
}
