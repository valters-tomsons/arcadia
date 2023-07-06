using System.Text;

namespace server;

public class Packet
{
    private byte[] packetData;

    public Packet(byte[] packetData = null)
    {
        this.packetData = packetData;
    }

    public byte[] GenerateChecksum(uint packetId, uint packetCount)
    {
        var packetIdBytes = GeneratePacketId(packetId + packetCount);
        var packetLengthBytes = GeneratePacketLength();

        return packetIdBytes.Concat(packetLengthBytes).ToArray();
    }

    public byte[] GeneratePacketId(uint packetId)
    {
        var packetIdBytes = BitConverter.GetBytes(packetId);
        Array.Reverse(packetIdBytes);
        return packetIdBytes;
    }

    public byte[] GeneratePacketLength()
    {
        var length = BitConverter.GetBytes(packetData.Length + 12);
        Array.Reverse(length);
        return length;
    }

    public uint GetPacketId(byte[] packetId)
    {
        Array.Reverse(packetId);
        return BitConverter.ToUInt32(packetId, 0);
    }

    public bool VerifyPacketLength(byte[] packetLength)
    {
        var dataLen = BitConverter.GetBytes(packetData.Length);
        Array.Reverse(dataLen);

        return packetLength.SequenceEqual(dataLen);
    }

    public Dictionary<string, string> DataInterpreter()
    {
        var data = Encoding.UTF8.GetString(packetData).Split("\n");
        var result = new Dictionary<string, string>();

        foreach (var entry in data.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var split = entry.Split("=");
            var parameter = split[0];
            var value = split[1].Replace("\"", "");

            result[parameter] = value;
        }

        return result;
    }

    // This method might need more adjustments, because some parts are not clear in python's version
    public List<byte[]> GeneratePackets(byte packetType, uint packetId, uint packetCount)
    {
        var dataObj = DataInterpreter();
        var packetDataString = "";

        foreach (var entry in dataObj)
        {
            var parameter = entry.Key;
            var value = entry.Value;

            packetDataString += $"{parameter}={(value.Contains(" ") ? $"\"{value}\"" : value)}\n";
        }

        packetDataString = packetDataString.TrimEnd('\n');

        if (packetDataString.Length > 8096)
        {
            var decodedSize = packetDataString.Length;
            var encodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(packetDataString));
            var encodedSize = encodedData.Length;

            var packetDataChunks = new List<string>();
            for (var i = 0; i < encodedData.Length; i += 8096)
            {
                packetDataChunks.Add(encodedData.Substring(i, Math.Min(8096, encodedData.Length - i)));
            }

            var packets = new List<byte[]>();

            foreach (var data in packetDataChunks)
            {
                var packetDataFinal = $"decodedSize={decodedSize}\nsize={encodedSize}\ndata={data.Replace("=", "%3d")}\0";
                var newPacket = new byte[] { packetType }.Concat(GenerateChecksum(0xb0000000, packetCount)).Concat(Encoding.UTF8.GetBytes(packetDataFinal)).ToArray();

                packets.Add(newPacket);
            }

            return packets;
        }
        else
        {
            var packetDataFinal = $"{packetDataString}\0";
            var newPacket = new byte[] { packetType }.Concat(GenerateChecksum(packetId, packetCount)).Concat(Encoding.UTF8.GetBytes(packetDataFinal)).ToArray();

            return new List<byte[]> { newPacket };
        }
    }
}