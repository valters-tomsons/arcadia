using Xunit;
using Arcadia.Psn;

namespace tests;

public class RPCNTicketTests
{
    [Fact]
    public void ValidTicket_Decode()
    {
        const string loginTicket = "$21010000000000f7300000a40008001432343937380000000000000000000000000000000001000433333333000700080000018ac52e4fc2000700080000018ac53c0b6200020008000000000000cfdc000400206661697468000000000000000000000000000000000000000000000000000000000800046272000000040004756e0000000800184550303030362d4e50454230303039325f30300000000000000100040000000000000000000000003002004b000800045250434e0008003f303d021c2f3888e03bb1d477cea3f3962e364819776641b7c3a6742727b05fa5021d00ec0ca690994bba9b8b261fb09924274a16cfe4180bb12f5e88e7b862";
        var ticketData = RPCNTicketDecoder.DecodeFromASCIIString(loginTicket);

        var onlineId = ticketData.FirstOrDefault(x => x?.Type == TicketDataType.BString) as BStringData;
        Assert.True(onlineId?.Value.TrimEnd('\0').Equals("faith"));

        var dateData = ticketData.Where(x => x?.Type == TicketDataType.Time).ToArray();

        var issuedDate = DateTimeOffset.FromUnixTimeMilliseconds((long)((TimeData)dateData[0]).Value).UtcDateTime;
        Assert.True(issuedDate.Year == 2023 && issuedDate.Month == 9 && issuedDate.Day == 24);
        Assert.True(issuedDate.Hour == 3 && issuedDate.Minute == 14 && issuedDate.Second == 21);

        var expireDate = DateTimeOffset.FromUnixTimeMilliseconds((long)((TimeData)dateData[1]).Value).UtcDateTime;
        Assert.True(expireDate.Year == 2023 && expireDate.Month == 9 && expireDate.Day == 24);
        Assert.True(expireDate.Hour == 3 && expireDate.Minute == 29 && expireDate.Second == 21);
    }
}