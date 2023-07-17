using System.Text;
using Org.BouncyCastle.Security;

namespace Arcadia.Fesl;

public static class PacketUtils
{
    private static readonly SecureRandom Random = new();

    public static string GenerateSalt()
    {
        var seed = Random.NextInt64(900000000, 999999999);
        return seed.ToString();
    }

    public static byte[] GeneratePacketLength(string packetData)
    {
        var dataBytes = Encoding.ASCII.GetBytes(packetData);

        int length = dataBytes.Length + 12;
        byte[] bytes = BitConverter.GetBytes(length);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }

    public static byte[] GeneratePacketId(uint packetId)
    {
        byte[] bytes = BitConverter.GetBytes(packetId);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }

    public static byte[] GenerateChecksum(string data, uint id)
    {
        byte[] packetIdBytes = GeneratePacketId(id);
        byte[] packetLengthBytes = GeneratePacketLength(data);

        return packetIdBytes.Concat(packetLengthBytes).ToArray();
    }
}