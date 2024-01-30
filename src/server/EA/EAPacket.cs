using System.Text;

namespace Arcadia.EA;

public readonly struct Packet
{
    public Dictionary<string, object> DataDict { get; }
    public string this[string key]
    {
        get => DataDict.GetValueOrDefault(key) as string ?? string.Empty;
        set => DataDict[key] = value;
    }

    /// <summary>
    /// Helper method for quick access to TXN. Used only in fesl/plasma connections.
    /// </summary>
    public string TXN => DataDict.GetValueOrDefault(nameof(TXN)) as string ?? string.Empty;

    /// <summary>
    /// Deserializes into the first packet from a given byte buffer, used for reading incoming data.
    /// </summary>
    public Packet(byte[] packet)
    {
        Type = Encoding.ASCII.GetString(packet, 0, 4);

        var headerSplit = Utils.SplitAt(packet, 12);
        Checksum = headerSplit[0][4..];

        var bigEndianChecksum = (BitConverter.IsLittleEndian ? Checksum.Reverse().ToArray() : Checksum).AsSpan();
        Length = BitConverter.ToUInt32(bigEndianChecksum[..4]);
        var idAndTransmissionType = BitConverter.ToUInt32(bigEndianChecksum[4..]);
        TransmissionType = idAndTransmissionType & 0xff000000;
        Id = idAndTransmissionType & 0x00ffffff;

        Data = headerSplit[1][..((int)Length - 12)];
        DataDict = Utils.ParseFeslPacketToDict(Data);
    }

    /// <summary>
    /// Initializes a new packet from the given parameters, used for outgoing data before it is Serialize()'d for wire.
    /// </summary>
    public Packet(string type, uint transmissionType, uint packetId, Dictionary<string, object>? dataDict = null)
    {
        Type = type.Trim();
        TransmissionType = transmissionType;
        Id = packetId;
        DataDict = dataDict ?? [];
    }

    /// <summary>
    /// Serialize the packet into a binary buffer, complete with header. Ready for transfer.
    /// </summary>
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
    public byte[]? Data { get; }
    public byte[]? Checksum { get; }
}