using System.Text;
using Org.BouncyCastle.Security;

namespace Arcadia.EA;

public static class PacketUtils
{
    private static readonly SecureRandom Random = new();

    public static string GenerateSalt()
    {
        var seed = Random.NextInt64(100000000, 999999999);
        return seed.ToString();
    }

    public static byte[] BuildPacketHeader(string type, uint transmissionType, uint packetId, string data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        // TODO Check if bitwise OR is enough or if we need to apply the bit mask first to not break anything with packet ids longer than 3 bytes
        var transmissionTypePacketIdBytes = UintToBytes(transmissionType | packetId);
        var packetLengthBytes = CalcPacketLength(data);

        return typeBytes.Concat(transmissionTypePacketIdBytes).Concat(packetLengthBytes).ToArray();
    }

    private static byte[] CalcPacketLength(string packetData)
    {
        var dataBytes = Encoding.ASCII.GetBytes(packetData);

        int length = dataBytes.Length + 12;
        byte[] bytes = BitConverter.GetBytes(length);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }

    private static byte[] UintToBytes(uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }
}