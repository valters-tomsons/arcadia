using System.Globalization;
using System.Text;
using Arcadia.Fesl.Structures;
using Org.BouncyCastle.Tls;

namespace Arcadia.Fesl;

public class ArcadiaFesl
{
    private readonly TlsServerProtocol _network;
    private readonly string _clientEndpoint;
    private uint _ticketCounter;

    public ArcadiaFesl(TlsServerProtocol network, string clientEndpoint)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;
    }

    public async Task HandleClientConnection()
    {
        var readBuffer = new byte[1514];
        while (_network.IsConnected)
        {
            int read;

            try
            {
                read = _network.ReadApplicationData(readBuffer, 0, readBuffer.Length);
            }
            catch
            {
                Console.WriteLine($"Connection has been closed with {_clientEndpoint}");
                break;
            }

            if (read == 0)
            {
                continue;
            }

            var reqPacket = new FeslPacket(readBuffer[..read]);
            var reqTxn = (string)reqPacket.DataDict["TXN"];

            Console.WriteLine($"Type: {reqPacket.Type}");
            Console.WriteLine($"TXN: {reqTxn}");

            if (reqPacket.Type == "fsys" && reqTxn == "Hello")
            {
                await HandleHello();
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuPS3Login")
            {
                await HandleLogin();
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuGetTos")
            {
                await HandleGetTos();
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuPS3AddAccount")
            {
                await HandleAddAccount(reqPacket);
            }
        }
    }

    private async Task HandleAddAccount(FeslPacket request)
    {
        var data = new Dictionary<string, object>
        {
            {"TXN", "NuPS3AddAccount"},
        };

        var email = request.DataDict["nuid"] as string;
        var pass = request.DataDict["password"] as string;

        Console.WriteLine($"Trying to register user {email} with password {pass}");

        var id = Interlocked.Increment(ref _ticketCounter);
        var packet = new FeslPacket("acct", 0x80000000, data);
        var response = await packet.ToPacket(id);

        _network.WriteApplicationData(response.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(response));
    }

    private async Task HandleGetTos()
    {
        const string tos = "Welcome to Arcadia!\nBeware, here be dragons!";

        var data = new Dictionary<string, object>
        {
            { "TXN", "NuGetTos" },
            { "version", "20426_17.20426_17" },
            { "tos", $"{System.Net.WebUtility.UrlEncode(tos).Replace('+', ' ')}" },
        };

        var id = Interlocked.Increment(ref _ticketCounter);
        var packet = new FeslPacket("acct", 0x80000000, data);
        var response = await packet.ToPacket(id);

        _network.WriteApplicationData(response.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(response));
    }

    private async Task HandleLogin()
    {
        var loginResponseData = new Dictionary<string, object>
        {
            { "localizedMessage", "The user was not found" },
            { "errorContainer.[]", 0 },
            { "TXN", "NuPS3Login" },
            { "errorCode", 101 }
        };

        var loginId = _ticketCounter;
        var loginPacket = new FeslPacket("acct", 0x80000000, loginResponseData);
        var loginResponse = await loginPacket.ToPacket(loginId);

        _network.WriteApplicationData(loginResponse.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(loginResponse));
    }

    private async Task HandleHello()
    {
        var currentTime = DateTime.UtcNow.ToString("MMM-dd-yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var serverHelloData = new Dictionary<string, object>
                {
                    { "domainPartition.domain", "ps3" },
                    { "messengerIp", "beach-ps3.fesl.ea.com" },
                    { "messengerPort", 0 },
                    { "domainPartition.subDomain", "BEACH" },
                    { "TXN", "Hello" },
                    { "activityTimeoutSecs", 0 },
                    { "curTime", currentTime},
                    { "theaterIp", "beach-ps3.fesl.ea.com" },
                    { "theaterPort", 18236 }
                };

        var helloId = Interlocked.Increment(ref _ticketCounter);
        var helloPacket = new FeslPacket("fsys", 0x80000000, serverHelloData);
        var helloResponse = await helloPacket.ToPacket(helloId);

        _network.WriteApplicationData(helloResponse.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(helloResponse));

        var memCheckData = new Dictionary<string, object>
                {
                    { "TXN", "MemCheck" },
                    { "memcheck.[]", "0" },
                    { "type", "0" },
                    { "salt", PacketUtils.GenerateSalt() }
                };

        var memcheckId = Interlocked.Increment(ref _ticketCounter);
        var memcheckPacket = new FeslPacket("fsys", 0x80000000, memCheckData);
        var memcheckResponse = await memcheckPacket.ToPacket(memcheckId);

        _network.WriteApplicationData(memcheckResponse.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(memcheckResponse));
    }
}