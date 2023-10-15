using Xunit;
using Arcadia.EA;
using Arcadia;

namespace tests;

public class EAPacketTests
{

    [Fact]
    public void ClientHello_Decodes()
    {
        var request = @"66
73 79 73 c0 00 00 01 00 00 00 af 54 58 4e 3d 48
65 6c 6c 6f 0a 63 6c 69 65 6e 74 53 74 72 69 6e
67 3d 62 65 61 63 68 2d 70 73 33 0a 73 6b 75 3d
70 73 33 0a 6c 6f 63 61 6c 65 3d 65 6e 5f 55 53
0a 63 6c 69 65 6e 74 50 6c 61 74 66 6f 72 6d 3d
50 53 33 0a 63 6c 69 65 6e 74 56 65 72 73 69 6f
6e 3d 31 2e 30 0a 53 44 4b 56 65 72 73 69 6f 6e
3d 35 2e 31 2e 30 2e 30 2e 30 0a 70 72 6f 74 6f
63 6f 6c 56 65 72 73 69 6f 6e 3d 32 2e 30 0a 66
72 61 67 6d 65 6e 74 53 69 7a 65 3d 38 30 39 36
0a 63 6c 69 65 6e 74 54 79 70 65 3d 0a 00";

        var packet = new Packet(Utils.HexStringToBytes(request));

        Assert.Equal("fsys", packet.Type);
        Assert.Equal((uint)1, packet.Id);
        Assert.Equal(0xc0000000, packet.TransmissionType);
        Assert.Equal("Hello", packet["TXN"]);
    }
}