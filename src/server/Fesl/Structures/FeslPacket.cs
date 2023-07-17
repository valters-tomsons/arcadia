using System.Text;

namespace Arcadia.Fesl.Structures;

public readonly struct FeslPacket
{
    public FeslPacket(byte[] appData)
    {
        Type = Encoding.ASCII.GetString(appData, 0, 4);

        var firstSplit = Utils.SplitAt(appData, 12);
        Checksum = firstSplit[0][4..];

        var bigEndianChecksum = (BitConverter.IsLittleEndian ? Checksum.Reverse().ToArray() : Checksum).AsSpan();
        Id = BitConverter.ToUInt32(bigEndianChecksum[..4]);
        Length = BitConverter.ToUInt32(bigEndianChecksum[4..]);

        Data = firstSplit[1];
        DataDict = Utils.ParseFeslPacketToDict(Data);
    }

    public FeslPacket(string type, uint id)
    {
        Type = type;
    }

    public string Type { get; }
    public byte[] Checksum { get; }
    public uint Id { get; }
    public uint Length { get; }
    public byte[] Data { get; }
    public Dictionary<string, object> DataDict { get; }
}