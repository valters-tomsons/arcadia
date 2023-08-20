using Xunit;
using Arcadia.Psn;

namespace tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        const string psnTicket = "$31000000000000";

        var ticket = TicketDecoder.DecodeFromASCIIString(psnTicket);
        var userData = ticket.Data[0];

        Assert.NotEmpty(ticket.Data);
        Assert.True(0x3000 <= userData.Id);
        Assert.NotEmpty(userData.SubData);
    }
}