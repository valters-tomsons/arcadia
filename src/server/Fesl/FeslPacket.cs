using System.Text;

namespace Arcadia.Fesl;

public class FeslPacket
{
    public FeslPacket(byte[] appData)
    {
        Type = Encoding.ASCII.GetString(appData, 0, 4);

        var firstSplit = Utils.SplitAt(appData, 12);
        Checksum = firstSplit[0][4..];

        Id = BitConverter.ToInt32(Checksum.AsSpan()[..4]);
        Length = BitConverter.ToInt32(Checksum.AsSpan()[4..]);

        Data = firstSplit[1];
    }

    public string Type { get; init; } = null!;
    public byte[] Checksum { get; init; } = null!;
    public int Id { get; init; }
    public int Length { get; init; }
    public byte[] Data { get; init; } = null!;
}