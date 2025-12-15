using System.Collections.Immutable;
using System.Globalization;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.EA.Ports;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NPTicket;

namespace Arcadia.Handlers;

public class FeslHandler
{
    private readonly ImmutableDictionary<string, Func<Packet, Task>> _handlers;

    private readonly ILogger<FeslHandler> _logger;
    private readonly IOptions<ArcadiaSettings> _settings;
    private readonly SharedCounters _sharedCounters;
    private readonly ConnectionManager _sharedCache;
    private readonly IEAConnection _conn;
    private readonly StatsStorage _stats;
    private readonly Database _db;

    private PlasmaSession? _plasma;
    private string clientString = string.Empty;
    private string partitionId = string.Empty;
    private string subDomain = string.Empty;

    private readonly static TimeSpan PingPeriod = TimeSpan.FromSeconds(60);
    private readonly static TimeSpan MemCheckPeriod = TimeSpan.FromSeconds(120);
    private static int? DefaultTheaterPort;

    private readonly Timer _pingTimer;
    private readonly Timer _memchTimer;

    public FeslHandler(IEAConnection conn, ILogger<FeslHandler> logger, IOptions<ArcadiaSettings> settings, SharedCounters sharedCounters, ConnectionManager sharedCache, StatsStorage stats, Database db)
    {
        _logger = logger;
        _settings = settings;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
        _conn = conn;
        _stats = stats;
        _db = db;

        _pingTimer = new(async _ => await SendPing(), null, Timeout.Infinite, Timeout.Infinite);
        _memchTimer = new(async _ => await SendMemCheck(), null, Timeout.Infinite, Timeout.Infinite);
        DefaultTheaterPort ??= settings.Value.ListenPorts.First(PortExtensions.IsTheater);

        _handlers = new Dictionary<string, Func<Packet, Task>>()
        {
            ["fsys/Hello"] = HandleHello,
            ["fsys/MemCheck"] = HandleMemCheck,
            ["fsys/Ping"] = HandlePing,
            ["fsys/GetPingSites"] = HandleGetPingSites,
            ["fsys/Goodbye"] = HandleGoodbye,
            ["pnow/Start"] = HandlePlayNow,
            ["acct/NuPS3Login"] = HandleNuPs3Login,
            ["acct/PS3Login"] = HandlePs3Login,
            ["acct/NuLogin"] = HandleNuLogin,
            ["acct/NuGetPersonas"] = HandleNuGetPersonas,
            ["acct/NuLoginPersona"] = HandleNuLoginPersona,
            ["acct/NuGetTos"] = HandleGetTos,
            ["acct/GetTos"] = HandleGetTos,
            ["acct/NuPS3AddAccount"] = HandleAddAccount,
            ["acct/LookupUserInfo"] = HandleLookupUserInfo,
            ["acct/NuLookupUserInfo"] = HandleNuLookupUserInfo,
            ["acct/NuGetEntitlements"] = HandleNuGetEntitlements,
            ["acct/GetLockerURL"] = HandleGetLockerUrl,
            ["recp/GetRecord"] = HandleGetRecord,
            ["recp/GetRecordAsMap"] = HandleGetRecordAsMap,
            ["asso/GetAssociations"] = HandleGetAssociations,
            ["pres/PresenceSubscribe"] = HandlePresenceSubscribe,
            ["rank/GetStats"] = HandleGetStats,
            ["rank/GetTopNAndStats"] = HandleGetTopNAndStats,
            ["rank/GetRankedStats"] = HandleGetRankedStats,
            ["rank/GetRankedStatsForOwners"] = HandleGetRankedStatsForOwners,
            ["rank/UpdateStats"] = HandleUpdateStats,
            ["xmsg/GetMessages"] = HandleGetMessages,

            ["pres/SetPresenceStatus"] = AcknowledgeRequest,
            ["acct/NuGrantEntitlement"] = AcknowledgeRequest,
            ["acct/GetTelemetryToken"] = AcknowledgeRequest,
            ["xmsg/ModifySettings"] = AcknowledgeRequest,
            ["mtrx/ReportMetrics"] = AcknowledgeRequest,
            ["rank/ReportMetrics"] = AcknowledgeRequest,
            ["pnow/ReportMetrics"] = AcknowledgeRequest,
            ["pnow/Cancel"] = AcknowledgeRequest,
            ["achi/GetAchievementGroupDefinitions"] = AcknowledgeRequest,
            ["achi/GetAchievementDefinitionsByGroup"] = AcknowledgeRequest,
            ["achi/GetOwnerAchievementsByGroup"] = AcknowledgeRequest,
            ["achi/SynchAchievements"] = AcknowledgeRequest,
        }.ToImmutableDictionary();
    }

    public async Task<PlasmaSession> HandleClientConnection(Stream network, string clientEndpoint, string serverEndpoint, CancellationToken ct)
    {
        try
        {
            _conn.Initialize(network, clientEndpoint, serverEndpoint, ct);
            await foreach (var packet in _conn.ReceiveAsync(_logger))
            {
                await HandlePacket(packet);
            }

        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in fesl: {Message}", e.Message);
        }
        finally
        {
            await DisposeTimers();
        }

        if (_plasma is not null)
        {
            await _sharedCache.RemovePlasmaSession(_plasma);
        }

        _logger.LogInformation("Closing FESL connection: {clientEndpoint} | {name}", clientEndpoint, _plasma?.NAME);

        return _plasma ?? new()
        {
            FeslConnection = _conn,
            UID = 0,
            NAME = string.Empty,
            LKEY = string.Empty,
            ClientString = string.Empty,
            PartitionId = string.Empty
        };
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
        clientString = request["clientString"];
        subDomain = clientString.Split('-').First().ToUpperInvariant();
        partitionId = $"/{request["sku"]}/{subDomain}";

        const string hostName = "theater.ps3.arcadia";

        var currentTime = DateTime.UtcNow.ToString("MMM-dd-yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var serverHelloData = new Dictionary<string, string>
                {
                    { "domainPartition.domain", request["sku"] },
                    { "messengerIp", _settings.Value.MessengerAddress ?? hostName },
                    { "messengerPort", $"{_settings.Value.MessengerPort}" },
                    { "domainPartition.subDomain", subDomain },
                    { "TXN", "Hello" },
                    { "activityTimeoutSecs", "0" },
                    { "curTime", currentTime },
                    { "theaterIp", _settings.Value.TheaterAddress ?? hostName },
                    { "theaterPort", $"{DefaultTheaterPort}" }
                };

        var helloPacket = new Packet("fsys", FeslTransmissionType.SinglePacketResponse, request.Id, serverHelloData);
        await _conn.SendPacket(helloPacket);
        await SendMemCheck();
    }

    private async Task HandleGoodbye(Packet request)
    {
        await _conn.Terminate();
    }

    private async Task HandlePlayNow(Packet request)
    {
        var pnowId = _sharedCounters.GetNextPnowId();

        await _conn.SendPacket(
            new(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, new()
            {
                { "TXN", "Start" },
                { "id.id", $"{pnowId}" },
                { "id.partition", partitionId },
            })
        );

        var pnowResult = new Dictionary<string, string>
        {
            { "TXN", "Status" },
            { "id.id", $"{pnowId}" },
            { "id.partition", partitionId },
            { "sessionState", "COMPLETE" },
        };

        var servers = _sharedCache.GetPartitionServers(partitionId).Where(x => x.CanJoin).ToArray();
        if (servers.Length > 0)
        {
            pnowResult.Add("props.{}", "3");
            pnowResult.Add("props.{resultType}", PlayNowResultType.JOIN);
            pnowResult.Add("props.{avgFit}", "1.0");
            pnowResult.Add("props.{games}.[]", $"{servers.Length}");

            for (var i = 0; i < servers.Length; i++)
            {
                pnowResult.Add($"props.{{games}}.{i}.gid", $"{servers[i].GID}");
                pnowResult.Add($"props.{{games}}.{i}.lid", $"{servers[i].LID}");
            }
        }
        else
        {
            pnowResult.Add("props.{}", "1");
            pnowResult.Add("props.{resultType}", PlayNowResultType.NOSERVER);
        }

        await _conn.SendPacket(new(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, pnowResult));
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

    private async Task HandleGetStats(Packet request)
    {
        // TODO: Implement multi-packet responses 
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "GetStats" },
        };

        // Override BF1943 minimum player count requirement
        if (request["keys.1"] == "pm_minplayers")
        {
            responseData.Add("stats.[]", "1");
            responseData.Add("stats.0.key", "pm_minplayers");
            responseData.Add("stats.0.value", "1.0");
        }
        else
        {
            responseData.Add("stats.[]", "0");
        }

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

    private async Task HandleGetRankedStats(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "stats.[]", "0" },
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGetTopNAndStats(Packet request)
    {
        var responseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "stats.[]", "0" }, // rankCount
            { "stats.0.addStats.[]", "0" } // statCount
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGetRankedStatsForOwners(Packet request)
    {
        var statCount = int.Parse(request.DataDict.GetValueOrDefault("keys.[]", "0"));
        var ownerCount = int.Parse(request.DataDict.GetValueOrDefault("owners.[]", "0"));

        var responseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "rankedStats.[]", $"{ownerCount}" },
            { "rankedStats.0.rankedStats.[]", $"{statCount}" }
        };

        for (var i = 0; i < ownerCount; i++)
        {
            var ownerId = request.DataDict[$"owners.{i}.ownerId"];
            responseData.Add($"rankedStats.{i}.ownerId", ownerId);
            responseData.Add($"rankedStats.{i}.ownerType", "1");

            for (var j = 0; j < statCount; j++)
            {
                var statName = request.DataDict[$"keys.{j}"];
                responseData.Add($"rankedStats.{i}.rankedStats.{j}.key", statName);
            }
        }

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePresenceSubscribe(Packet request)
    {
        if (_plasma is null) throw new NotImplementedException();

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

    private async Task HandleLookupUserInfo(Packet request)
    {
        var queryCount = int.Parse(request["userInfo.[]"]);
        var users = Enumerable.Range(0, queryCount)
            .Select(i => request[$"userInfo.{i}.userName"])
            .Select(query => new
            {
                query,
                user = _sharedCache.FindPartitionSessionByUser(partitionId, query)
            })
            .Where(x => x.user is not null)
            .ToArray();

        var responseData = new Dictionary<string, string>
        {
            { "TXN", "LookupUserInfo" },
            { "userInfo.[]", users.Length.ToString() }
        };

        for (var i = 0; i < users.Length; i++)
        {
            var result = users[i];
            responseData.Add($"userInfo.{i}.userName", result.user!.NAME);
        }

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleNuLookupUserInfo(Packet request)
    {
        var queryCount = int.Parse(request["userInfo.[]"]);
        var users = Enumerable.Range(0, queryCount)
            .Select(i => request[$"userInfo.{i}.userName"])
            .Select(query => new
            {
                query,
                user = _sharedCache.FindPartitionSessionByUser(partitionId, query)
            })
            .Where(x => x.user is not null)
            .ToArray();

        var responseData = new Dictionary<string, string>
        {
            { "TXN", "NuLookupUserInfo" },
            { "userInfo.[]", users.Length.ToString() }
        };

        for (var i = 0; i < users.Length; i++)
        {
            var result = users[i];
            responseData.Add($"userInfo.{i}.userName", result.user!.NAME);
        }

        var packet = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGetAssociations(Packet request)
    {
        if (_plasma is null) throw new NotImplementedException();

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
        var serverIp = _plasma!.FeslConnection!.LocalAddress;
        var responseData = new Dictionary<string, string>
        {
            { "TXN", "GetPingSites" },
            { "pingSite.[]", "0"},
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
            { "TXN", request.TXN },
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

        _plasma = await _sharedCache.CreatePlasmaConnection(_conn, nuid, clientString, partitionId);

        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            
            // ! TODO: This is not good!
            { "encryptedLoginInfo", "Ciyvab0tregdVsBtboIpeChe4G6uzC1v5_-SIxmvSL" + "bjbfvmobxvmnawsthtgggjqtoqiatgilpigaqqzhejglhbaokhzltnstufrfouwrvzyphyrspmnzprxcocyodg" }
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(loginPacket);
    }

    private async Task HandleNuGetPersonas(Packet request)
    {
        if (_plasma is null) throw new NotImplementedException();

        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "personas.[]", "1" },
            { "personas.0", _plasma.NAME },
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(packet);
    }
    
    private async Task HandleNuLoginPersona(Packet request)
    {
        var loginResponseData = new Dictionary<string, string>
        {
            { "TXN", request.TXN },
            { "lkey", _plasma!.LKEY },
            { "profileId", _plasma.UID.ToString() },
            { "userId", _plasma.UID.ToString() }
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, loginResponseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandleNuGetEntitlements(Packet request)
    {
        var response = new Dictionary<string, string>()
        {
            { "TXN", request.TXN },
        };

        var groupName = request["groupName"];
        switch(groupName)
        {
            case "BFBC2PS3":
                response.AddEntitlements(_plasma!.UID, [
                    (groupName, 1111100001, "BFBC2:PS3:ONSLAUGHT_PDLC"),
                    (groupName, 1111100002, "BFBC2:PS3:CIAB"),
                    (groupName, 1111100003, "BFBC2:PS3:VIP_PDLC")
                ]);
                break;
            case "BattlefieldBadCompany2":
                response.AddEntitlements(_plasma!.UID, [
                    (groupName, 1100000001, "BFBC2:COMMON:GAMESTOP")
                ]);
                break;
            default:
                response.Add("entitlements.[]", "0");
                break;
        }

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, response);
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
        var ticketPayload = request["ticket"];
        var ticketBytes = Convert.FromHexString(ticketPayload[1..]);
        var ticket = Ticket.ReadFromBytes(ticketBytes);
        var onlineId = ticket.Username;

        if (string.IsNullOrWhiteSpace(onlineId)) throw new NotImplementedException();

        _db.RecordLoginMetric(ticket);

        _plasma = await _sharedCache.CreatePlasmaConnection(_conn, onlineId, clientString, partitionId);
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
        // if (false)
        // {
        //     await SendError(request, 122, "The password the user specified is incorrect");
        //     return;
        // }

        // var tosAccepted = request.DataDict.TryGetValue("tosVersion", out var tosAcceptedValue);
        // if (!tosAccepted || string.IsNullOrEmpty(tosAcceptedValue as string))
        // {
        //     await SendError(request, 101, "The user was not found");
        //     return;
        // }

        var ticketPayload = request["ticket"];
        var ticketBytes = Convert.FromHexString(ticketPayload[1..]);
        var ticket = Ticket.ReadFromBytes(ticketBytes);
        var onlineId = ticket.Username;

        var existingSession = _sharedCache.FindPartitionSessionByUser(partitionId, onlineId);
        if (existingSession is not null)
        {
            await SendError(request, 102);
            return;
        }

        _db.RecordLoginMetric(ticket);

        _plasma = await _sharedCache.CreatePlasmaConnection(_conn, onlineId, clientString, partitionId);
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
            // 105: account pending authorization
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

        var email = request.DataDict["nuid"];
        var pass = request.DataDict["password"];

        // TODO: maybe stop logging this eventually
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

    private async Task HandleUpdateStats(Packet request)
    {
        if (!int.TryParse(request["u.0.s.[]"], out var statsCount) || statsCount < 1)
        {
            await AcknowledgeRequest(request);
            return;
        }

        _logger.LogTrace("Client submitted {statsCount} stats!", statsCount);

        var onslaughtStats = new List<OnslaughtLevelCompleteMessage>();

        for (var i = 0; i < statsCount; i++)
        {
            var statKey = request[$"u.0.s.{i}.k"];
            _logger.LogTrace("Processing stat {index}: {statKey}", i, statKey);

            if (statKey.StartsWith("time_mp") && statKey.EndsWith("_c") && clientString.Equals("BFBC2-PS3", StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.LogInformation("Trying to update Onslaught map stats...");

                try
                {
                    var uid = long.Parse(request["u.0.o"]);
                    var player = _sharedCache.FindSessionByUID(uid) ?? throw new Exception("Player not online?");
                    var server = _sharedCache.FindGameWithPlayerByUid(partitionId, uid) ?? throw new Exception("Player not in server?");

                    if (_plasma is null)
                    {
                        throw new Exception("Denying update from client!");
                    }

                    if (server.UID != _plasma.UID)
                    {
                        _logger.LogWarning("Non-server request stats update");
                    }

                    if (uid != _plasma.UID)
                    {
                        _logger.LogWarning("Non-self stats update");
                    }

                    var playtime = Math.Abs(double.Parse(request[$"u.0.s.{i}.v"]));
                    var onslaughtMessage = new OnslaughtLevelCompleteMessage
                    {
                        PlayerName = player.NAME,
                        MapKey = statKey.Replace("time_mp", string.Empty).Replace("_c", string.Empty),
                        Difficulty = server.Data["B-U-difficulty"],
                        GameTime = TimeSpan.FromSeconds(playtime)
                    };

                    _logger.LogTrace("Posting Onslaught stats message: {Message}", onslaughtMessage);
                    onslaughtStats.Add(onslaughtMessage);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to process onslaught stats update!");
                }
            }
        }

        _stats.PostLevelComplete([.. onslaughtStats]);

        await AcknowledgeRequest(request);
    }

    private async Task AcknowledgeRequest(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            { "TXN", request.TXN }
        };

        var packet = new Packet(request.Type, FeslTransmissionType.SinglePacketResponse, request.Id, response);
        await _conn.SendPacket(packet);
    }

    private Task HandlePing(Packet _)
    {
        _pingTimer.Change(PingPeriod, PingPeriod);
        return Task.CompletedTask;
    }

    private Task HandleMemCheck(Packet packet)
    {
        _memchTimer.Change(MemCheckPeriod, MemCheckPeriod);
        HandlePing(packet);

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

        try
        {
            await _conn.SendPacket(memcheckPacket);
        }
        catch
        {
            await DisposeTimers();
        }
    }

    private async Task SendPing()
    {
        if (_plasma?.FeslConnection?.NetworkStream?.CanWrite != true || _plasma.TheaterConnection?.NetworkStream?.CanWrite != true)
        {
            return;
        }

        var feslPing = new Packet("fsys", FeslTransmissionType.SinglePacketRequest, 0, new() { { "TXN", "Ping" } });
        var theaterPing = new Packet("PING", TheaterTransmissionType.Request, 0);

        try
        {
            await _conn.SendPacket(feslPing);
            await _plasma.TheaterConnection.SendPacket(theaterPing);
        }
        catch
        {
            await DisposeTimers();
        }
    }

    private async Task SendError(Packet req, int code, string? localizedMessage = null)
    {
        var response = new Dictionary<string, string>
        {
            { "TXN", req.TXN },
            { "localizedMessage", localizedMessage ?? string.Empty },
            { "errorContainer.[]", "0" },
            { "errorCode", $"{code}" }
        };

        var loginPacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, req.Id, response);
        await _conn.SendPacket(loginPacket);
    }

    private async Task DisposeTimers()
    {
        _pingTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _memchTimer.Change(Timeout.Infinite, Timeout.Infinite);
        await _pingTimer.DisposeAsync();
        await _memchTimer.DisposeAsync();
    }
}