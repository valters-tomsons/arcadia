using System.Globalization;
using System.Text;
using Arcadia.Fesl.Structures;
using Org.BouncyCastle.Tls;

namespace Arcadia.Fesl;

public class ArcadiaFesl
{
    private readonly TlsServerProtocol _network;
    private readonly string _clientEndpoint;

    private uint _plasmaTicketId;

    public ArcadiaFesl(TlsServerProtocol network, string clientEndpoint)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;
    }

    public async Task HandleClientConnection()
    {
        while (_network.IsConnected)
        {
            int read;
            byte[]? readBuffer;

            try
            {
                (read, readBuffer) = await Utils.ReadApplicationDataAsync(_network);
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

            if (reqPacket.Id != 0x80000000)
            {
                Interlocked.Increment(ref _plasmaTicketId);
            }

            if (reqTxn != "MemCheck")
            {
                Console.WriteLine($"Type: {reqPacket.Type}");
                Console.WriteLine($"TXN: {reqTxn}");
            }

            if (reqPacket.Type == "fsys" && reqTxn == "Hello")
            {
                await HandleHello();
            }
            else if (reqPacket.Type == "fsys" && reqTxn == "MemCheck")
            {
                await HandleMemCheck();
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuPS3Login")
            {
                await HandleLogin(reqPacket);
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuGetTos")
            {
                await HandleGetTos();
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuPS3AddAccount")
            {
                await HandleAddAccount(reqPacket);
            }
            else
            {
                Console.WriteLine($"Unknown packet type: {reqPacket.Type} TXN: {reqTxn}");
                Interlocked.Increment(ref _plasmaTicketId);
            }
        }
    }

    private async Task HandleHello()
    {
        var currentTime = DateTime.UtcNow.ToString("MMM-dd-yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var serverHelloData = new Dictionary<string, object>
                {
                    { "domainPartition.domain", "ps3" },
                    { "messengerIp", "127.0.0.1" },
                    { "messengerPort", 0 },
                    { "domainPartition.subDomain", "BEACH" },
                    { "TXN", "Hello" },
                    { "activityTimeoutSecs", 0 },
                    { "curTime", currentTime},
                    { "theaterIp", "127.0.0.1" },
                    { "theaterPort", 18236 }
                };

        var helloPacket = new FeslPacket("fsys", 0x80000000, serverHelloData);
        var helloResponse = await helloPacket.ToPacket(_plasmaTicketId);

        _network.WriteApplicationData(helloResponse.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(helloResponse));

        await SendMemCheck();
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

        var packet = new FeslPacket("acct", 0x80000000, data);
        var response = await packet.ToPacket(_plasmaTicketId);

        _network.WriteApplicationData(response.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(response));
    }

    private async Task HandleLogin(FeslPacket request)
    {
        var encryptedSet = request.DataDict.TryGetValue("returnEncryptedInfo", out var returnEncryptedInfo);
        Console.WriteLine($"returnEncryptedInfo: {returnEncryptedInfo} ({encryptedSet})");

        var loginResponseData = new Dictionary<string, object>();

        var tosAccepted = request.DataDict.TryGetValue("tosVersion", out var tosAcceptedValue);

        // loginResponseData.Add("TXN", request.Type);
        // loginResponseData.Add("localizedMessage", "The password the user specified is incorrect");
        // loginResponseData.Add("errorContainer.[]", "0");
        // loginResponseData.Add("errorCode", "122");

        if (!tosAccepted || string.IsNullOrEmpty(tosAcceptedValue as string))
        {
            loginResponseData.Add("TXN", request.Type);
            loginResponseData.Add( "localizedMessage", "The user was not found" );
            loginResponseData.Add( "errorContainer.[]", 0 );
            loginResponseData.Add( "errorCode", 101 );
        }
        else
        {
            const string keyTempl = "W5NyZzx{0}Cki6GQAAKDw.";
            var lkey = string.Format(keyTempl, "SaUr4131g");

            loginResponseData.Add("lkey", lkey);
            loginResponseData.Add("TXN", "NuPS3Login");
            loginResponseData.Add("userId", 1000000000000);
            loginResponseData.Add("personaName", "faith");
        }

        var loginPacket = new FeslPacket("acct", 0x80000000, loginResponseData);
        var loginResponse = await loginPacket.ToPacket(_plasmaTicketId);

        _network.WriteApplicationData(loginResponse.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(loginResponse));
    }

    private async Task HandleAddAccount(FeslPacket request)
    {
        var data = new Dictionary<string, object>
        {
            {"TXN", "NuPS3AddAccount"}
        };

        var email = request.DataDict["nuid"] as string;
        var pass = request.DataDict["password"] as string;

        Console.WriteLine($"Trying to register user {email} with password {pass}");

        var resultPacket = new FeslPacket("acct", 0x80000000, data);
        var response = await resultPacket.ToPacket(_plasmaTicketId);

        _network.WriteApplicationData(response.AsSpan());
        Console.WriteLine(Encoding.ASCII.GetString(response));
    }

    private async Task HandleMemCheck()
    {
        await Task.Delay(1000);
        await SendMemCheck();
    }

    private async Task SendMemCheck()
    {
        var memCheckData = new Dictionary<string, object>
                {
                    { "TXN", "MemCheck" },
                    { "memcheck.[]", "0" },
                    { "type", "0" },
                    { "salt", PacketUtils.GenerateSalt() }
                };

        var memcheckPacket = new FeslPacket("fsys", 0x80000000, memCheckData);
        var memcheckResponse = await memcheckPacket.ToPacket(_plasmaTicketId);

        _network.WriteApplicationData(memcheckResponse.AsSpan());
    }
}