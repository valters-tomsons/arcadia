namespace Arcadia.Psn;

public enum TicketType
{
    Empty,
    U32,
    U64,
    BString,
    Time,
    Binary,
    Blob
}

public struct TicketData
{
    public TicketType Type;
    public ushort Id;
    public ushort Len;
    public byte[] Data;
}

public struct Ticket
{
    public uint Version;
    public uint Size;
    public List<TicketData> Data;
}