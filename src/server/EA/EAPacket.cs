using System.Text;

namespace Arcadia.EA;

public readonly struct Packet
{
    public string TXN => DataDict.GetValueOrDefault(nameof(TXN)) as string ?? string.Empty;

    public string this[string key]
    {
        get => DataDict.GetValueOrDefault(key) as string ?? string.Empty;
        set => DataDict[key] = value;
    }

    public Packet(byte[] packet)
    {
        Type = Encoding.ASCII.GetString(packet, 0, 4);

        var firstSplit = Utils.SplitAt(packet, 12);
        Checksum = firstSplit[0][4..];

        var bigEndianChecksum = (BitConverter.IsLittleEndian ? Checksum.Reverse().ToArray() : Checksum).AsSpan();
        Length = BitConverter.ToUInt32(bigEndianChecksum[..4]);
        var idAndTransmissionType = BitConverter.ToUInt32(bigEndianChecksum[4..]);
        TransmissionType = idAndTransmissionType & 0xff000000;
        Id = idAndTransmissionType & 0x00ffffff;

        Data = firstSplit[1];
        DataDict = Utils.ParseFeslPacketToDict(Data);
    }

    public Packet(string type, uint transmissionType, uint packetId, Dictionary<string, object>? dataDict = null)
    {
        Type = type.Trim();
        TransmissionType = transmissionType;
        Id = packetId;
        // TODO Packet length needs to be set here
        DataDict = dataDict ?? [];
    }

    public Packet Clone()
    {
        return new Packet(Type, TransmissionType, Id, DataDict);
    }

    public async Task<byte[]> Serialize()
    {
        var data = Utils.DataDictToPacketString(DataDict).ToString();
        var header = PacketUtils.BuildPacketHeader(Type, TransmissionType, Id, data);

        var dataBytes = Encoding.ASCII.GetBytes(data);

        using var response = new MemoryStream(header.Length + dataBytes.Length);

        await response.WriteAsync(header);
        await response.WriteAsync(dataBytes);
        await response.FlushAsync();

        return response.ToArray();
    }

    public string Type { get; }
    public uint Id { get; }
    public uint TransmissionType { get;  }
    public uint Length { get; }
    public Dictionary<string, object> DataDict { get; }
    public byte[]? Data { get; }
    public byte[]? Checksum { get; }
}