using Xunit;
using NPTicket;

namespace tests;

public class PSNTicketTests
{
    [Fact]
    public void ValidTicket_RPCN_Decode()
    {
        const string loginTicket = "$21010000000000f7300000a40008001432343937380000000000000000000000000000000001000433333333000700080000018ac52e4fc2000700080000018ac53c0b6200020008000000000000cfdc000400206661697468000000000000000000000000000000000000000000000000000000000800046272000000040004756e0000000800184550303030362d4e50454230303039325f30300000000000000100040000000000000000000000003002004b000800045250434e0008003f303d021c2f3888e03bb1d477cea3f3962e364819776641b7c3a6742727b05fa5021d00ec0ca690994bba9b8b261fb09924274a16cfe4180bb12f5e88e7b862";
        var ticketBytes = Convert.FromHexString(loginTicket[1..]);
        var ticket = Ticket.ReadFromBytes(ticketBytes);

        Assert.Equal("RPCN", ticket.SignatureIdentifier);

        Assert.Equal("EP0006-NPEB00092_00", ticket.ServiceId);
        Assert.Equal("un", ticket.Domain);
        Assert.Equal("br", ticket.Country);

        Assert.Equal("faith", ticket.Username);

        Assert.True(ticket.IssuedDate.Year == 2023 && ticket.IssuedDate.Month == 9 && ticket.IssuedDate.Day == 24);
        Assert.True(ticket.IssuedDate.Hour == 3 && ticket.IssuedDate.Minute == 14 && ticket.IssuedDate.Second == 21);

        Assert.True(ticket.ExpiryDate.Year == 2023 && ticket.ExpiryDate.Month == 9 && ticket.ExpiryDate.Day == 24);
        Assert.True(ticket.ExpiryDate.Hour == 3 && ticket.ExpiryDate.Minute == 29 && ticket.ExpiryDate.Second == 21);
    }

    [Fact]
    public void ValidTicket_PSN_Decode()
    {
        const string loginTicket = "$31000000000000f8300000ac00080014328845fa37db56a958f4aebcbb8e02392d6d92770001000400000100000700080000018b28d4510d000700080000018b2dfaa9600002000858725221887ac2780004002046616974684c560000000000000000000000000000000000000000000000000000080004676200010004000462350000000800184550303030362d4e50454230303039325f303000000000003011000407ce0302000100041900020030100000000000003002004400080004382de58d000800383036021900ead042d18a5a9dcc5dbd15556c9f809175af357246ac8c780219008766662cb2b8e112532feaa707271cd6eaba659220bc528e";
        var ticketBytes = Convert.FromHexString(loginTicket[1..]);
        var ticket = Ticket.ReadFromBytes(ticketBytes);

        Assert.Equal("FaithLV", ticket.Username);
        Assert.Equal("EP0006-NPEB00092_00", ticket.ServiceId);
    }
}