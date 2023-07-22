using System.Globalization;
using Arcadia.EA;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace Arcadia.Fesl;

public class FeslHandler
{
    private readonly ILogger<FeslHandler> _logger;

    private TlsServerProtocol _network = null!;
    private string _clientEndpoint = null!;

    private uint _feslTicketId;

    private long _playerId = 1000000001337;

    public FeslHandler(ILogger<FeslHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleClientConnection(TlsServerProtocol network, string clientEndpoint)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;

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
                _logger.LogInformation("Connection has been closed with {endpoint}", _clientEndpoint);
                break;
            }

            if (read == 0)
            {
                continue;
            }

            var reqPacket = new Packet(readBuffer[..read]);
            var reqTxn = (string)reqPacket.DataDict["TXN"];

            if (reqPacket.Id != 0x80000000)
            {
                Interlocked.Increment(ref _feslTicketId);
            }

            if (reqTxn != "MemCheck")
            {
                _logger.LogInformation("Type: {type}", reqPacket.Type);
                _logger.LogInformation("TXN: {txn}", reqTxn);
            }

            if (reqPacket.Type == "fsys" && reqTxn == "Hello")
            {
                await HandleHello();
            }
            else if (reqPacket.Type == "fsys" && reqTxn == "MemCheck")
            {
                await HandleMemCheck();
            }
            else if (reqPacket.Type == "fsys" && reqTxn == "GetPingSites")
            {
                await HandleGetPingSites();
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
            else if(reqPacket.Type == "acct" && reqTxn == "NuLookupUserInfo")
            {
                await HandleLookupUserInfo(reqPacket);
            }
            else if(reqPacket.Type == "asso" && reqTxn == "GetAssociations")
            {
                await HandleGetAssociations(reqPacket);
            }
            else
            {
                _logger.LogWarning("Unknown packet type: {type}, TXN: {txn}", reqPacket.Type, reqTxn);
                Interlocked.Increment(ref _feslTicketId);
            }
        }
    }

    private async Task HandleLookupUserInfo(Packet reqPacket)
    {
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "NuLookupUserInfo" },
            { "userInfo.[]", "1" },
            { "userInfo.0.userName", "faith" },
        };
    }

    private async Task HandleGetAssociations(Packet request)
    {
        var assoType = request.DataDict["type"] as string;
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "GetAssociations" },
            { "domainPartition.domain", request.DataDict["domainPartition.domain"] },
            { "domainPartition.subDomain", request.DataDict["domainPartition.subDomain"] },
            { "owner.id", _playerId },
            { "owner.type", "1" },
            { "type", assoType },
            { "members.[]", "0" },
        };

        if (assoType == "PlasmaMute")
        {
            responseData.Add("maxListSize", 100);
            responseData.Add("owner.name", "faith");
        }
        else
        {
            _logger.LogWarning("Unknown association type: {assoType}", assoType);
        }

        var packet = new Packet("asso", 0x80000000, responseData);
        var response = await packet.ToPacket(_feslTicketId);

        _network.WriteApplicationData(response.AsSpan());
    }

    private async Task HandleGetPingSites()
    {
        const string serverIp = "127.0.0.1";

        var responseData = new Dictionary<string, object>
        {
            { "TXN", "GetPingSites" },
            { "pingSite.[]", "4"},
            { "pingSite.0.addr", serverIp },
            { "pingSite.0.type", "0"},
            { "pingSite.0.name", "eu1"},
            { "minPingSitesToPing", "0"}
        };

        var packet = new Packet("fsys", 0x80000000, responseData);
        var response = await packet.ToPacket(_feslTicketId);

        _network.WriteApplicationData(response.AsSpan());
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

        var helloPacket = new Packet("fsys", 0x80000000, serverHelloData);
        var helloResponse = await helloPacket.ToPacket(_feslTicketId);

        _network.WriteApplicationData(helloResponse.AsSpan());
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

        var packet = new Packet("acct", 0x80000000, data);
        var response = await packet.ToPacket(_feslTicketId);

        _network.WriteApplicationData(response.AsSpan());
    }

    private async Task HandleLogin(Packet request)
    {
        var encryptedSet = request.DataDict.TryGetValue("returnEncryptedInfo", out var returnEncryptedInfo);
        _logger.LogInformation("returnEncryptedInfo: {returnEncryptedInfo} ({encryptedSet})", returnEncryptedInfo, encryptedSet);

        var loginResponseData = new Dictionary<string, object>();

        var tosAccepted = request.DataDict.TryGetValue("tosVersion", out var tosAcceptedValue);

        // if (false)
        // {
        //     loginResponseData.Add("TXN", request.Type);
        //     loginResponseData.Add("localizedMessage", "The password the user specified is incorrect");
        //     loginResponseData.Add("errorContainer.[]", "0");
        //     loginResponseData.Add("errorCode", "122");
        // }

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

        var loginPacket = new Packet("acct", 0x80000000, loginResponseData);
        var loginResponse = await loginPacket.ToPacket(_feslTicketId);

        _network.WriteApplicationData(loginResponse.AsSpan());
    }

    private async Task HandleAddAccount(Packet request)
    {
        var data = new Dictionary<string, object>
        {
            {"TXN", "NuPS3AddAccount"}
        };

        var email = request.DataDict["nuid"] as string;
        var pass = request.DataDict["password"] as string;

        _logger.LogInformation("Trying to register user {email} with password {pass}", email, pass);

        var resultPacket = new Packet("acct", 0x80000000, data);
        var response = await resultPacket.ToPacket(_feslTicketId);

        _network.WriteApplicationData(response.AsSpan());
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

        var memcheckPacket = new Packet("fsys", 0x80000000, memCheckData);
        var memcheckResponse = await memcheckPacket.ToPacket(_feslTicketId);

        _network.WriteApplicationData(memcheckResponse.AsSpan());
    }
}