using System.Globalization;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.EA.Ports;
using Arcadia.PSN;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Tls;

namespace Arcadia.EA.Handlers;

public class FeslHandler
{
    private readonly ILogger<FeslHandler> _logger;
    private readonly IOptions<ArcadiaSettings> _settings;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;
    private readonly IEAConnection _conn;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private FeslGamePort _servicePort;
    private PlasmaConnection? _plasma;

    public FeslHandler(IEAConnection conn, ILogger<FeslHandler> logger, IOptions<ArcadiaSettings> settings, SharedCounters sharedCounters, SharedCache sharedCache)
    {
        _logger = logger;
        _settings = settings;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
        _conn = conn;

        _handlers = new Dictionary<string, Func<Packet, Task>>()
        {
            ["fsys/Hello"] = HandleHello,
            ["fsys/MemCheck"] = HandleMemCheck,
            ["fsys/GetPingSites"] = HandleGetPingSites,
            ["pnow/Start"] = HandlePlayNow,
            ["acct/NuPS3Login"] = HandleNuPs3Login,
            ["acct/PS3Login"] = HandlePs3Login,
            ["acct/NuLogin"] = HandleNuLogin,
            ["acct/NuGetPersonas"] = HandleNuGetPersonas,
            ["acct/NuLoginPersona"] = HandleNuLoginPersona,
            ["acct/NuGetTos"] = HandleGetTos,
            ["acct/GetTelemetryToken"] = HandleTelemetryToken,
            ["acct/NuPS3AddAccount"] = HandleAddAccount,
            ["acct/NuLookupUserInfo"] = HandleLookupUserInfo,
            ["acct/NuGetEntitlements"] = HandleNuGetEntitlements,
            ["acct/NuGrantEntitlement"] = HandleNuGrantEntitlement,
            ["acct/GetLockerURL"] = HandleGetLockerUrl,
            ["recp/GetRecord"] = HandleGetRecord,
            ["recp/GetRecordAsMap"] = HandleGetRecordAsMap,
            ["asso/GetAssociations"] = HandleGetAssociations,
            ["pres/PresenceSubscribe"] = HandlePresenceSubscribe,
            ["pres/SetPresenceStatus"] = HandleSetPresenceStatus,
            ["rank/GetStats"] = HandleGetStats,
            ["xmsg/GetMessages"] = HandleGetMessages,
            ["xmsg/ModifySettings"] = HandleModifySettings
        };
    }

    public async Task<PlasmaConnection> HandleClientConnection(TlsServerProtocol tlsProtocol, string clientEndpoint, string serverEndpoint)
    {
        _conn.InitializeSecure(tlsProtocol, clientEndpoint, serverEndpoint);
        await foreach (var packet in _conn.StartConnection(_logger))
        {
            await HandlePacket(packet);
        }

        return _plasma ?? throw new NotImplementedException();
    }

    public async Task HandlePacket(Packet packet)
    {
        var reqTxn = packet.TXN;
        var packetType = packet.Type;
        _handlers.TryGetValue($"{packetType}/{reqTxn}", out var handler);

        if (handler is null)
        {
            _logger.LogWarning("Unknown packet type: {type}, TXN: {txn}", packet.Type, reqTxn);
            return;
        }

        await handler(packet);
    }

    private async Task HandleHello(Packet request)
    {
        var currentTime = DateTime.UtcNow.ToString("MMM-dd-yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var serverHelloData = new Dictionary<string, string>
                {
                    { "domainPartition.domain", "ps3" },
                    { "messengerIp", "127.0.0.1" },
                    { "messengerPort", "0" },
                    { "domainPartition.subDomain", "BFBC2" },
                    { "TXN", "Hello" },
                    { "activityTimeoutSecs", "0" },
                    { "curTime", currentTime },
                    { "theaterIp", _settings.Value.TheaterAddress },
                    { "theaterPort", "18126" }
                };

        var helloPacket = new Packet("fsys", FeslTransmissionType.SinglePacketResponse, request.Id, serverHelloData);
        await _conn.SendPacket(helloPacket);
        await SendMemCheck();
    }

    private async Task HandleTelemetryToken(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "GetTelemetryToken" },
        };

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePlayNow(Packet request)
    {
        var pnowId = _sharedCounters.GetNextPnowId();
        var data1 = new Dictionary<string, string>
        {
            { "TXN", "Start" },
            { "id.id", $"{pnowId}" },
            { "id.partition", "/ps3/BFBC2" },
        };

        var packet1 = new Packet("pnow", FeslTransmissionType.SinglePacketResponse, request.Id, data1);
        await _conn.SendPacket(packet1);

        var servers = _sharedCache.GetGameServers();
        var data2 = new Dictionary<string, string>
        {
            { "TXN", "Status" },
            { "id.id", $"{pnowId}" },
            { "id.partition", "/ps3/BFBC2" },
            { "sessionState", "COMPLETE" },
            { "props.{}", "3" },
            { "props.{resultType}", "JOIN" },
            { "props.{avgFit}", "1.0" },
            { "props.{games}.[]", $"{servers.Length}" },
        };

        for (var i = 0; i < servers.Length; i++)
        {
            data2.Add($"props.{{games}}.{i}.gid", servers[i].Data["GID"]);
            data2.Add($"props.{{games}}.{i}.lid", servers[i].Data["LID"]);
        }

        var packet2 = new Packet("pnow", FeslTransmissionType.SinglePacketResponse, request.Id, data2);
        await _conn.SendPacket(packet2);
    }

    private async Task HandleGetRecord(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "localizedMessage", "Nope" },
            { "errorContainer.[]", "0" },
            { "errorCode", "5000" },
        };

        await _conn.SendPacket(new Packet("recp", FeslTransmissionType.SinglePacketResponse, request.Id, responseData));
    }

    private async Task HandleGetRecordAsMap(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "TTL", "0" },
            { "state", "1" },
            { "values.{}", "0" }
        };

        await _conn.SendPacket(new Packet("recp", FeslTransmissionType.SinglePacketResponse, request.Id, responseData));
    }

    private async Task HandleNuGrantEntitlement(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN }
        };

        await _conn.SendPacket(new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, responseData));
    }


    private async Task HandleGetStats(Packet request)
    {
        // TODO Not entirely sure if this works well with the game, since stats requests are usually sent as multi-packet queries with base64 encoded data
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "GetStats" },
            { "stats.[]", "0" }
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
        await _conn.SendPacket(packet);
    }

    private async Task HandlePresenceSubscribe(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "PresenceSubscribe" },
            { "responses.0.outcome", "0" },
            { "responses.[]", "1" },
            { "responses.0.owner.type", "1" },
            { "responses.0.owner.id", _plasma.UID.ToString() },
        };

        var packet = new Packet("pres", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleSetPresenceStatus(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "SetPresenceStatus" },
        };

        var packet = new Packet("pres", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleLookupUserInfo(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "NuLookupUserInfo" },
            { "userInfo.[]", "1" },
            { "userInfo.0.userName", _plasma.NAME },
        };

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGetAssociations(Packet request)
    {
        var assoType = request.DataDict["type"] as string ?? string.Empty;
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "GetAssociations" },
            { "domainPartition.domain", request.DataDict["domainPartition.domain"] },
            { "domainPartition.subDomain", request.DataDict["domainPartition.subDomain"] },
            { "owner.id", _plasma.UID.ToString() },
            { "owner.type", "1" },
            { "type", assoType },
            { "members.[]", "0" },
        };

        if (assoType == "PlasmaMute")
        {
            responseData.Add("maxListSize", "100");
            responseData.Add("owner.name", _plasma.NAME);
        }
        else
        {
            _logger.LogWarning("Unknown association type: {assoType}", assoType);
        }

        var packet = new Packet("asso", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGetPingSites(Packet request)
    {
        var serverIp = _plasma!.FeslConnection!.ServerAddress;
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "GetPingSites" },
            { "pingSite.[]", "4"},
            { "pingSite.0.addr", serverIp },
            { "pingSite.0.type", "0"},
            { "pingSite.0.name", "eu1"},
            { "minPingSitesToPing", "0"}
        };

        var packet = new Packet("fsys", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGetTos(Packet request)
    {
        // TODO Same as with stats, usually sent as multi-packed response
        const string tos = "Welcome to Arcadia!\nBeware, here be dragons!";

        var data = new Dictionary<string, string>
        {
            { "TXN", "NuGetTos" },
            { "version", "20426_17.20426_17" },
            { "tos", $"{System.Net.WebUtility.UrlEncode(tos).Replace('+', ' ')}" },
        };

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, data);
        await _conn.SendPacket(packet);
    }

    private async Task HandleNuLogin(Packet request)
    {
        var nuid = request["nuid"];
        if (nuid.Contains('@'))
        {
            nuid = nuid.Split('@')[0];
        }

        _plasma = _sharedCache.CreatePlasmaConnection(_conn, nuid);

        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "encryptedLoginInfo", "Ciyvab0tregdVsBtboIpeChe4G6uzC1v5_-SIxmvSL" + "bjbfvmobxvmnawsthtgggjqtoqiatgilpigaqqzhejglhbaokhzltnstufrfouwrvzyphyrspmnzprxcocyodg" }
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(loginPacket);
    }

    private async Task HandleNuGetPersonas(Packet request)
    {
        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "personas.[]", "1" },
            { "personas.0", _plasma.NAME },
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(packet);
    }
    
    private Task HandleNuLoginPersona(Packet request)
    {
        throw new NotImplementedException();
        // _sessionCache["LKEY"] = SharedCounters.GenerateLKey();

        // var uid = _sharedCounters.GetNextUserId().ToString();
        // _sessionCache["UID"] = uid;

        // _sharedCache.AddUserWithLKey(_sessionCache["LKEY"], _sessionCache["personaName"]);
        // var loginResponseData = new Dictionary<string, string>
        // {
        //     { "TXN", request.TXN },
        //     { "lkey", _sessionCache["LKEY"] },
        //     { "profileId", uid },
        //     { "userId", uid },
        // };

        // var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        // await _conn.SendPacket(packet);
    }

    private async Task HandleNuGetEntitlements(Packet request)
    {
        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "entitlements.[]", "0" }
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGetLockerUrl(Packet request)
    {
        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "url", "http://127.0.0.1/arcadia.jsp" }
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(packet);
    }

    // BC1, AO2
    private async Task HandlePs3Login(Packet request)
    {
        var loginTicket = request["ticket"];
        var ticketData = TicketDecoder.DecodeFromASCIIString(loginTicket, _logger);
        var onlineId = (ticketData[5] as BStringData)?.Value?.TrimEnd('\0');

        if (string.IsNullOrWhiteSpace(onlineId)) throw new NotImplementedException();

        _plasma = _sharedCache.CreatePlasmaConnection(_conn, onlineId);
        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "lkey", _plasma.LKEY },
            { "userId", _plasma.UID.ToString() },
            { "screenName", _plasma.NAME }
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(loginPacket);
    }

    // BC2, 1943
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

        var loginTicket = request.DataDict["ticket"] ?? string.Empty;
        var ticketData = TicketDecoder.DecodeFromASCIIString(loginTicket, _logger);
        var onlineId = (ticketData[5] as BStringData)?.Value?.TrimEnd('\0') ?? throw new NotImplementedException();

        _plasma = _sharedCache.CreatePlasmaConnection(_conn, onlineId);
        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", "NuPS3Login" },
            { "lkey", _plasma.LKEY },
            { "userId", _plasma.UID.ToString() },
            { "personaName", _plasma.NAME }
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(loginPacket);
    }

    private async Task SendInvalidLogin1943(Packet request)
    {
        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", "NuPS3Login" },
            { "localizedMessage", "Nope" },
            { "errorContainer.[]", "0" },
            { "errorCode", "101" }

            // 101: unknown user
            // 102: account disabled
            // 103: account banned
            // 120: account not entitled
            // 121: too many login attempts
            // 122: invalid password
            // 123: game has not been registered (?)
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(loginPacket);
    }

    private async Task HandleAddAccount(Packet request)
    {
        var data = new Dictionary<string, string>
        {
            {"TXN", "NuPS3AddAccount"}
        };

        var email = request.DataDict["nuid"] as string;
        var pass = request.DataDict["password"] as string;

        _logger.LogDebug("Trying to register user {email} with password {pass}", email, pass);

        var resultPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, data);
        await _conn.SendPacket(resultPacket);
    }

    private async Task HandleGetMessages(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "messages.[]", "0" }
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandleModifySettings(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            { "TXN", request.TXN }
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, response);
        await _conn.SendPacket(packet);
    }

    private static Task HandleMemCheck(Packet _)
    {
        return Task.CompletedTask;
    }

    private async Task SendMemCheck()
    {
        var memCheckData = new Dictionary<string, string>
                {
                    { "TXN", "MemCheck" },
                    { "memcheck.[]", "0" },
                    { "type", "0" },
                    { "salt", PacketUtils.GenerateSalt() }
                };

        // FESL backend is requesting the client to respond to the memcheck, so this is a request
        // But since memchecks are not part of the meaningful conversation with the client, they don't have a packed id
        var memcheckPacket = new Packet("fsys", FeslTransmissionType.SinglePacketRequest, 0, memCheckData);
        await _conn.SendPacket(memcheckPacket);
    }
}