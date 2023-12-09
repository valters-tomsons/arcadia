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

        uint maskedTransmissionType = (transmissionType << 24) & 0xFF000000; // ensure it only occupies the high-order byte
        uint maskedPacketId = packetId & 0x00FFFFFF; // make sure it fits in the lower 3 bytes
        var transmissionTypePacketIdBytes = UintToBytes(maskedTransmissionType | maskedPacketId);

        var packetLengthBytes = CalculatePacketLength(data);

        return typeBytes.Concat(transmissionTypePacketIdBytes).Concat(packetLengthBytes).ToArray();
    }

    private static byte[] CalculatePacketLength(string packetData)
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