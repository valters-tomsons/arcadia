namespace Arcadia.PSN;

public enum TicketDataType
{
    Empty = 0,
    U32 = 1,
    U64 = 2,
    Time = 7,
    Binary = 8,
    BString = 4,
    Blob = 0x3000
}

public abstract class TicketData
{
    public abstract TicketDataType Type { get; }
    public ushort Length { get; set; }
    public ushort Id { get; set; }
}

public class EmptyData : TicketData
{
    public override TicketDataType Type => TicketDataType.Empty;
}

public class U32Data : TicketData
{
    public override TicketDataType Type => TicketDataType.U32;
    public uint Value { get; set; }
}

public class U64Data : TicketData
{
    public override TicketDataType Type => TicketDataType.U64;
    public ulong Value { get; set; }
}

public class TimeData : TicketData
{
    public override TicketDataType Type => TicketDataType.Time;
    public ulong Value { get; set; }
}

public class BinaryData : TicketData
{
    public override TicketDataType Type => TicketDataType.Binary;
    public byte[]? Value { get; set; }
}

public class BStringData : TicketData
{
    public override TicketDataType Type => TicketDataType.BString;
    public string? Value { get; set; }
}

public class BlobData : TicketData
{
    public override TicketDataType Type => TicketDataType.Blob;
    public byte Tag { get; set; }
    public List<TicketData>? Children { get; set; }
}