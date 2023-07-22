using System.Text;

namespace Arcadia.EA;

public readonly struct Packet
{
    public Packet(byte[] packet)
    {
        Type = Encoding.ASCII.GetString(packet, 0, 4);

        var firstSplit = Utils.SplitAt(packet, 12);
        Checksum = firstSplit[0][4..];

        var bigEndianChecksum = (BitConverter.IsLittleEndian ? Checksum.Reverse().ToArray() : Checksum).AsSpan();
        Length = BitConverter.ToUInt32(bigEndianChecksum[..4]);
        Id = BitConverter.ToUInt32(bigEndianChecksum[4..]);

        Data = firstSplit[1];
        DataDict = Utils.ParseFeslPacketToDict(Data);
    }

    public Packet(string type, uint packetId, Dictionary<string, object>? dataDict = null)
    {
        Type = type.Trim();
        Length = packetId;
        DataDict = dataDict ?? new Dictionary<string, object>();
    }

    public async Task<byte[]> ToPacket(uint ticketId)
    {
        var data = Utils.DataDictToPacketString(DataDict).ToString();
        var checksum = PacketUtils.GeneratePacketChecksum(data, Length + ticketId);

        var typeBytes = Encoding.ASCII.GetBytes(Type);
        var dataBytes = Encoding.ASCII.GetBytes(data);

        using var response = new MemoryStream(typeBytes.Length + checksum.Length + dataBytes.Length);

        await response.WriteAsync(typeBytes);
        await response.WriteAsync(checksum);
        await response.WriteAsync(dataBytes);
        await response.FlushAsync();

        return response.ToArray();
    }

    public string Type { get; }
    public uint Id { get; }
    public uint Length { get; }
    public Dictionary<string, object> DataDict { get; }
    public byte[]? Data { get; }
    public byte[]? Checksum { get; }
}