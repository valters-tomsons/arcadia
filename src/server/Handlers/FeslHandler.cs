using System.Globalization;
using System.Text;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.PSN;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Tls;

namespace Arcadia.Handlers;

public class FeslHandler
{
    private readonly ILogger<FeslHandler> _logger;
    private readonly IOptions<ArcadiaSettings> _settings;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;

    public FeslHandler(ILogger<FeslHandler> logger, IOptions<ArcadiaSettings> settings, SharedCounters sharedCounters, SharedCache sharedCache)
    {
        _logger = logger;
        _settings = settings;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
    }

    private readonly Dictionary<string, object> _sessionCache = new();
    private TlsServerProtocol _network = null!;
    private string _clientEndpoint = null!;

    private uint _feslTicketId;

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

            reqPacket.DataDict.TryGetValue("TXN", out var txn);
            var reqTxn = txn as string ?? string.Empty;

            _logger.LogDebug("Incoming Type: {type} | TXN: {txn}", reqPacket.Type, reqTxn);
            _logger.LogTrace("data:{data}", Encoding.ASCII.GetString(readBuffer[..read]));

            if (reqPacket.Type == "fsys" && reqTxn == "Hello")
            {
                await HandleHello(reqPacket);
            }
            else if(reqPacket.Type == "pnow" && reqTxn == "Start")
            {
                await HandlePlayNow(reqPacket);
            }
            else if (reqPacket.Type == "fsys" && reqTxn == "MemCheck")
            {
                await HandleMemCheck();
            }
            else if (reqPacket.Type == "fsys" && reqTxn == "GetPingSites")
            {
                await HandleGetPingSites(reqPacket);
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuPS3Login")
            {
                await HandleNuPs3Login(reqPacket);
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuLogin")
            {
                await HandleNuLogin(reqPacket);
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuGetPersonas")
            {
                await HandleNuGetPersonas(reqPacket);
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuLoginPersona")
            {
                await HandleNuLoginPersona(reqPacket);
            }
            else if(reqPacket.Type == "acct" && reqTxn == "NuGetTos")
            {
                await HandleGetTos(reqPacket);
            }
            else if(reqPacket.Type == "acct" && reqTxn == "GetTelemetryToken")
            {
                await HandleTelemetryToken(reqPacket);
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
            else if(reqPacket.Type == "pres" && reqTxn == "PresenceSubscribe")
            {
                await HandlePresenceSubscribe(reqPacket);
            }
            else if(reqPacket.Type == "pres" && reqTxn == "SetPresenceStatus")
            {
                await HandleSetPresenceStatus(reqPacket);
            }
            else if (reqPacket.Type == "rank" && reqTxn == "GetStats")
            {
                await HandleGetStats(reqPacket);
            }
            else
            {
                _logger.LogWarning("Unknown packet type: {type}, TXN: {txn}", reqPacket.Type, reqTxn);
                Interlocked.Increment(ref _feslTicketId);
            }
        }
    }

    private async Task HandleTelemetryToken(Packet request)
    {
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "GetTelemetryToken" },
        };

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await SendPacket(packet);
    }

    private async Task HandlePlayNow(Packet request)
    {
        var pnowId = _sharedCounters.GetNextPnowId();
        var gid = _sharedCounters.GetNextGameId();
        var lid = _sharedCounters.GetNextLobbyId();

        var data1 = new Dictionary<string, object>
        {
            { "TXN", "Start" },
            { "id.id", pnowId },
            { "id.partition", "/ps3/BEACH" },
        };

        var packet1 = new Packet("pnow", FeslTransmissionType.SinglePacketResponse, request.Id, data1);
        await SendPacket(packet1);

        var data2 = new Dictionary<string, object>
        {
            { "TXN", "Status" },
            { "id.id", pnowId },
            { "id.partition", "/ps3/BEACH" },
            { "sessionState", "COMPLETE" },
            { "props.{}", 3 },
            { "props.{resultType}", "JOIN" },
            { "props.{avgFit}", "0.8182313914386985" },
            { "props.{games}.[]", 1 },
            { "props.{games}.0.gid", gid },
            { "props.{games}.0.lid", lid }
        };

        var packet2 = new Packet("pnow", FeslTransmissionType.SinglePacketResponse, request.Id, data2);
        await SendPacket(packet2);
    }

    private async Task HandleGetStats(Packet request)
    {
        // TODO Not entirely sure if this works well with the game, since stats requests are usually sent as multi-packet queries with base64 encoded data
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "GetStats" },
            {"stats.[]", 0 }
        };

        // TODO: Add some stats
        // var keysStr = request.DataDict["keys.[]"] as string ?? string.Empty;
        // var reqKeys = int.Parse(keysStr, CultureInfo.InvariantCulture);
        // for (var i = 0; i < reqKeys; i++)
        // {
        //     var key = request.DataDict[$"keys.{i}"];

        //     responseData.Add($"stats.{i}.key", key);
        //     responseData.Add($"stats.{i}.value", 0.0);
        // }

        var packet = new Packet("rank", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await SendPacket(packet);
    }

    private async Task HandlePresenceSubscribe(Packet request)
    {
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "PresenceSubscribe" },
            { "responses.0.outcome", "0" },
            { "responses.[]", "1" },
            { "responses.0.owner.type", "1" },
            { "responses.0.owner.id", _sessionCache["UID"] },
        };

        var packet = new Packet("pres", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await SendPacket(packet);
    }

    private async Task HandleSetPresenceStatus(Packet request)
    {
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "SetPresenceStatus" },
        };

        var packet = new Packet("pres", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await SendPacket(packet);
    }

    private async Task HandleLookupUserInfo(Packet request)
    {
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "NuLookupUserInfo" },
            { "userInfo.[]", "1" },
            { "userInfo.0.userName", _sessionCache["personaName"] },
        };

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await SendPacket(packet);
    }

    private async Task HandleGetAssociations(Packet request)
    {
        var assoType = request.DataDict["type"] as string ?? string.Empty;
        var responseData = new Dictionary<string, object>
        {
            { "TXN", "GetAssociations" },
            { "domainPartition.domain", request.DataDict["domainPartition.domain"] },
            { "domainPartition.subDomain", request.DataDict["domainPartition.subDomain"] },
            { "owner.id", _sessionCache["UID"] },
            { "owner.type", "1" },
            { "type", assoType },
            { "members.[]", "0" },
        };

        if (assoType == "PlasmaMute")
        {
            responseData.Add("maxListSize", 100);
            responseData.Add("owner.name", _sessionCache["personaName"]);
        }
        else
        {
            _logger.LogWarning("Unknown association type: {assoType}", assoType);
        }

        var packet = new Packet("asso", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await SendPacket(packet);
    }

    private async Task HandleGetPingSites(Packet request)
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

        var packet = new Packet("fsys", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await SendPacket(packet);
    }

    private async Task HandleHello(Packet request)
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
                    { "theaterIp", _settings.Value.TheaterAddress },
                    { "theaterPort", _settings.Value.TheaterPort }
                };

        var helloPacket = new Packet("fsys", FeslTransmissionType.SinglePacketResponse, request.Id, serverHelloData);
        await SendPacket(helloPacket);
        await SendMemCheck();
    }

    private async Task HandleGetTos(Packet request)
    {
        // TODO Same as with stats, usually sent as multi-packed response
        const string tos = "Welcome to Arcadia!\nBeware, here be dragons!";

        var data = new Dictionary<string, object>
        {
            { "TXN", "NuGetTos" },
            { "version", "20426_17.20426_17" },
            { "tos", $"{System.Net.WebUtility.UrlEncode(tos).Replace('+', ' ')}" },
        };

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, data);
        await SendPacket(packet);
    }

    private async Task HandleNuLogin(Packet request)
    {
        _sessionCache["personaName"] = request["nuid"];

        var loginResponseData = new Dictionary<string, object>
        {
            { "TXN", request.TXN },
            { "encryptedLoginInfo", "Ciyvab0tregdVsBtboIpeChe4G6uzC1v5_-SIxmvSL" + "bjbfvmobxvmnawsthtgggjqtoqiatgilpigaqqzhejglhbaokhzltnstufrfouwrvzyphyrspmnzprxcocyodg" }
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await SendPacket(loginPacket);
    }

    private async Task HandleNuGetPersonas(Packet request)
    {
        var loginResponseData = new Dictionary<string, object>
        {
            { "TXN", request.TXN },
            { "personas.[]", 1 },
            { "personas.0", _sessionCache["personaName"] },
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await SendPacket(packet);
    }
    
    private async Task HandleNuLoginPersona(Packet request)
    {
        _sessionCache["LKEY"] = _sharedCounters.GetNextLkey();

        var uid = _sharedCounters.GetNextUserId();
        _sessionCache["UID"] = uid;

        _sharedCache.AddUserWithKey((string)_sessionCache["LKEY"], (string)_sessionCache["personaName"]);
        var loginResponseData = new Dictionary<string, object>
        {
            { "TXN", request.TXN },
            { "lkey", _sessionCache["LKEY"] },
            { "profileId", uid },
            { "userId", uid },
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await SendPacket(packet);
    }

    private async Task HandleNuPs3Login(Packet request)
    {
        // var tosAccepted = request.DataDict.TryGetValue("tosVersion", out var tosAcceptedValue);
        // if (false)
        // {
        //     loginResponseData.Add("TXN", request.Type);
        //     loginResponseData.Add("localizedMessage", "The password the user specified is incorrect");
        //     loginResponseData.Add("errorContainer.[]", "0");
        //     loginResponseData.Add("errorCode", "122");
        // }

        // if (!tosAccepted || string.IsNullOrEmpty(tosAcceptedValue as string))
        // {
        //     loginResponseData.Add("TXN", request.Type);
        //     loginResponseData.Add( "localizedMessage", "The user was not found" );
        //     loginResponseData.Add( "errorContainer.[]", 0 );
        //     loginResponseData.Add( "errorCode", 101 );
        // }
        // else

        var loginTicket = request.DataDict["ticket"] as string ?? string.Empty;
        var ticketData = TicketDecoder.DecodeFromASCIIString(loginTicket);
        var onlineId = (ticketData[5] as BStringData).Value.TrimEnd('\0');

        _sessionCache["personaName"] = onlineId;
        _sessionCache["LKEY"] = _sharedCounters.GetNextLkey();
        _sessionCache["UID"] = _sharedCounters.GetNextUserId();

        _sharedCache.AddUserWithKey((string)_sessionCache["LKEY"], (string)_sessionCache["personaName"]);

        var loginResponseData = new Dictionary<string, object>
        {
            { "TXN", "NuPS3Login" },
            { "lkey", _sessionCache["LKEY"] },
            { "userId", _sessionCache["UID"] },
            { "personaName", _sessionCache["personaName"] }
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await SendPacket(loginPacket);
    }

    private async Task SendInvalidLogin(Packet request)
    {
        var loginResponseData = new Dictionary<string, object>
        {
            { "TXN", "NuPS3Login" },
            { "localizedMessage", "Nope" },
            { "errorContainer.[]", 0 },
            { "errorCode", 101 }

            // 101: unknown user
            // 102: account disabled
            // 103: account banned
            // 120: account not entitled
            // 121: too many login attempts
            // 122: invalid password
            // 123: game has not been registered (?)
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await SendPacket(loginPacket);
    }

    private async Task HandleAddAccount(Packet request)
    {
        var data = new Dictionary<string, object>
        {
            {"TXN", "NuPS3AddAccount"}
        };

        var email = request.DataDict["nuid"] as string;
        var pass = request.DataDict["password"] as string;

        _logger.LogDebug("Trying to register user {email} with password {pass}", email, pass);

        var resultPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, data);
        await SendPacket(resultPacket );
    }

    private static Task HandleMemCheck()
    {
        return Task.CompletedTask;
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

        // FESL backend is requesting the client to respond to the memcheck, so this is a request
        // But since memchecks are not part of the meaningful conversation with the client, they don't have a packed id
        var memcheckPacket = new Packet("fsys", FeslTransmissionType.SinglePacketRequest, 0, memCheckData);
        await SendPacket(memcheckPacket);
    }

    private async Task SendPacket(Packet packet)
    {
        var serializedData = await packet.Serialize();
        _network.WriteApplicationData(serializedData.AsSpan());
    }
}