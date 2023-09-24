using System.Text;

namespace Arcadia.PSN;

public record PSNTicket
{
    public ulong TicketId { get; init; }
    public uint IssuerId { get; init; }
    public DateTime IssuedDate { get; init; }
    public DateTime ExpireDate { get; init; }
    public ulong UserId { get; init; }
    public string OnlineId { get; init; }
    public string Region { get; init; }
    public string Domain { get; init; }
    public string ServiceId { get; init; }


    public PSNTicket(TicketData[] ticketData)
    {
        TicketId = ulong.Parse(Encoding.UTF8.GetString((ticketData[0] as BinaryData).Value).TrimEnd('\0'));
        IssuerId = (ticketData[1] as U32Data).Value;
        IssuedDate = DateTimeOffset.FromUnixTimeMilliseconds((long)((ticketData[2] as TimeData).Value)).UtcDateTime;
        ExpireDate = DateTimeOffset.FromUnixTimeMilliseconds((long)((ticketData[3] as TimeData).Value)).UtcDateTime;
        UserId = (ticketData[4] as U64Data).Value;
        OnlineId = (ticketData[5] as BStringData).Value.TrimEnd('\0');
        Region = Encoding.UTF8.GetString((ticketData[6] as BinaryData).Value).TrimEnd('\0');
        Domain = (ticketData[7] as BStringData).Value.TrimEnd('\0');
        ServiceId = Encoding.UTF8.GetString((ticketData[8] as BinaryData).Value).TrimEnd('\0');
    }
}