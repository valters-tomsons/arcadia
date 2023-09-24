using System.Text;

namespace Arcadia.PSN;

public static class TicketDecoder
{
    public static TicketData[] DecodeFromASCIIString(string asciiString)
    {
        var bytes = Convert.FromHexString(asciiString[1..]);
        return DecodeFromBuffer(bytes).Where(x => x.Type != TicketDataType.Empty).ToArray();
    }

    private static IEnumerable<TicketData> DecodeFromBuffer(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        while(reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var ticket = ReadTicketData(reader);
            if (ticket is not null) yield return ticket;
        }
    }

    private static TicketData? ReadTicketData(BinaryReader reader)
    {
        ushort id = BitConverter.ToUInt16(BitConverter.GetBytes(reader.ReadUInt16()).Reverse().ToArray(), 0);
        ushort len = BitConverter.ToUInt16(BitConverter.GetBytes(reader.ReadUInt16()).Reverse().ToArray(), 0);
        var type = (TicketDataType)(id & 0x0FFF);

        switch (type)
        {
            case TicketDataType.Empty:
                return new EmptyData() { Id = id, Length = len };

            case TicketDataType.U32:
                byte[] u32Bytes = reader.ReadBytes(4);
                Array.Reverse(u32Bytes);
                return new U32Data { Value = BitConverter.ToUInt32(u32Bytes, 0), Id = id, Length = len };

            case TicketDataType.U64:
                byte[] u64Bytes = reader.ReadBytes(8);
                Array.Reverse(u64Bytes);
                return new U64Data { Value = BitConverter.ToUInt64(u64Bytes, 0), Id = id, Length = len };

            case TicketDataType.Time:
                byte[] timeBytes = reader.ReadBytes(8);
                Array.Reverse(timeBytes);
                return new TimeData { Value = BitConverter.ToUInt64(timeBytes, 0), Id = id, Length = len };

            case TicketDataType.Binary:
                return new BinaryData { Value = reader.ReadBytes(len), Id = id, Length = len };

            case TicketDataType.BString:
                var str = Encoding.UTF8.GetString(reader.ReadBytes(len));
                return new BStringData { Value = str, Id = id, Length = len };

            case TicketDataType.Blob:
                var blobData = new BlobData
                {
                    Tag = reader.ReadByte(),
                    Children = new List<TicketData>(),
                    Id = id,
                    Length = len
                };

                ushort remainingLength = (ushort)(len - 1); // Subtract 1 for the tag

                while (remainingLength > 0)
                {
                    var child = ReadTicketData(reader);
                    blobData.Children.Add(child);
                    remainingLength -= (ushort)(4 + child.Length); // 4 bytes for id and len
                }

                return blobData;

            default:
                Console.WriteLine($"Unknown or unhandled type: {id}");
                reader.BaseStream.Seek(len, SeekOrigin.Current);  // Skip the unknown type
                return null;
        }
    }
}