using Xunit;
using Arcadia.PSN;

namespace tests;

public class RPCNTicketTests
{
    [Fact]
    public void ValidTicket_Decode()
    {
        const string loginTicket = "$21010000000000f7300000a40008001432343937380000000000000000000000000000000001000433333333000700080000018ac52e4fc2000700080000018ac53c0b6200020008000000000000cfdc000400206661697468000000000000000000000000000000000000000000000000000000000800046272000000040004756e0000000800184550303030362d4e50454230303039325f30300000000000000100040000000000000000000000003002004b000800045250434e0008003f303d021c2f3888e03bb1d477cea3f3962e364819776641b7c3a6742727b05fa5021d00ec0ca690994bba9b8b261fb09924274a16cfe4180bb12f5e88e7b862";

        var ticketData = TicketDecoder.DecodeFromASCIIString(loginTicket);
        var ticket = new PSNTicket(ticketData!);

        Assert.True(ticket.ServiceId.Equals("EP0006-NPEB00092_00"));
        Assert.True(ticket.Domain.Equals("un"));
        Assert.True(ticket.Region.Equals("br"));

        Assert.True(ticket.OnlineId.Equals("faith"));

        Assert.True(ticket.IssuedDate.Year == 2023 && ticket.IssuedDate.Month == 9 && ticket.IssuedDate.Day == 24);
        Assert.True(ticket.IssuedDate.Hour == 3 && ticket.IssuedDate.Minute == 14 && ticket.IssuedDate.Second == 21);

        Assert.True(ticket.ExpireDate.Year == 2023 && ticket.ExpireDate.Month == 9 && ticket.ExpireDate.Day == 24);
        Assert.True(ticket.ExpireDate.Hour == 3 && ticket.ExpireDate.Minute == 29 && ticket.ExpireDate.Second == 21);
    }
}