using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Handlers;

public class TheaterHandler
{
    private readonly ILogger<TheaterHandler> _logger;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;
    private readonly IOptions<ArcadiaSettings> _arcadiaSettings;
    private readonly IEAConnection _conn;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private readonly Dictionary<string, object> _sessionCache = new();

    private int _brackets = 0;

    public TheaterHandler(IEAConnection conn, ILogger<TheaterHandler> logger, SharedCounters sharedCounters, SharedCache sharedCache, IOptions<ArcadiaSettings> arcadiaSettings)
    {
        _logger = logger;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
        _arcadiaSettings = arcadiaSettings;
        _conn = conn;

        _handlers = new Dictionary<string, Func<Packet, Task>>
        {
            ["CONN"] = HandleCONN,
            ["USER"] = HandleUSER,
            ["CGAM"] = HandleCGAM,
            ["ECNL"] = HandleECNL,
            ["EGAM"] = HandleEGAM,
            ["EGRS"] = HandleEGRS,
            ["PENT"] = HandlePENT,
            ["GDAT"] = HandleGDAT,
            ["UBRA"] = HandleUBRA,
            ["UGAM"] = HandleUGAM,
            ["UGDE"] = HandleUGDE
        };
    }

    public async Task HandleClientConnection(NetworkStream network, string clientEndpoint)
    {
        _conn.InitializeInsecure(network, clientEndpoint);
        await foreach (var packet in _conn.StartConnection())
        {
            await HandlePacket(packet);
        }
    }

    public async Task HandlePacket(Packet packet)
    {
        var packetType = packet.Type;
        _handlers.TryGetValue(packetType, out var handler);

        if (handler is null)
        {
            _logger.LogWarning("Unknown packet type: {type}", packetType);
            return;
        }

        _logger.LogDebug("Incoming Type: {type}", packetType);
        await handler(packet);
    }

    private async Task HandleCONN(Packet request)
    {
        var tid = request.DataDict["TID"];

        _logger.LogInformation("CONN: {tid}", tid);

        if (request["PLAT"] == "PC")
        {
            _sharedCounters.SetServerNetworkStream(_conn.NetworkStream!);
        }

        var response = new Dictionary<string, object>
        {
            ["TIME"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["TID"] = tid,
            ["activityTimeoutSecs"] = 0,
            ["PROT"] = request.DataDict["PROT"]
        };

        var packet = new Packet("CONN", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandleUSER(Packet request)
    {
        var lkey = request.DataDict["LKEY"];
        var username = _sharedCache.GetUsernameByLKey((string)lkey);

        _sessionCache["NAME"] = username;
        _sessionCache["UID"] = _sharedCounters.GetNextUserId();
        _logger.LogInformation("USER: {name} {lkey}", username, lkey);

        var response = new Dictionary<string, object>
        {
            ["NAME"] = username,
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("USER", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // CreateGame
    private async Task HandleCGAM(Packet request)
    {
        var gid = _sharedCounters.GetNextGameId();
        var lid = _sharedCounters.GetNextLid();

        _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);

        var response = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["MAX-PLAYERS"] = request["MAX-PLAYERS"],
            ["EKEY"] = "AIBSgPFqRDg0TfdXW1zUGa4%3d",
            ["UGID"] = Guid.NewGuid().ToString(),
            ["JOIN"] = request["JOIN"],
            ["SECRET"] = request["SECRET"],
            ["LID"] = lid,
            ["J"] = request["JOIN"],
            ["GID"] = gid
        };

        var packet = new Packet("CGAM", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // LeaveGame
    private async Task HandleECNL(Packet request)
    {
        // !TODO: set gid to a valid game id
        // !TODO: set lid to a valid lobby id

        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
        };

        var packet = new Packet("ECNL", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // EnterGameRequest
    private async Task HandleEGAM(Packet request)
    {
        var clientResponse = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
        };

        var clientPacket = new Packet("EGAM", TheaterTransmissionType.OkResponse, 0, clientResponse);

        var ticket = _sharedCounters.GetNextTicket();
        _sessionCache["TICKET"] = ticket;

        await SendEGRQ(request, ticket);
        await _conn.SendPacket(clientPacket);
        await SendEGEG(request, ticket);
    }

    private async Task SendEGRQ(Packet request, long ticket)
    {
        var serverMessage = new Dictionary<string, object>
        {
            ["R-INT-PORT"] = request["R-INT-PORT"],
            ["R-INT-IP"] = request["R-INT-IP"],
            ["PORT"] = request["PORT"],
            ["NAME"] = _sessionCache["NAME"],
            ["PTYPE"] = request["PTYPE"],
            ["TICKET"] = ticket,
            ["PID"] = _sessionCache["UID"],
            ["UID"] = _sessionCache["UID"],
            ["IP"] = "192.168.0.39",

            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
        };

        var serverPacket = new Packet("EGRQ", TheaterTransmissionType.OkResponse, 0, serverMessage);
        var serverData = await serverPacket.Serialize();
        var serverNetwork = _sharedCounters.GetServerNetworkStream();
        await serverNetwork!.WriteAsync(serverData);
    }

    private async Task HandleEGRS(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("EGRS", TheaterTransmissionType.OkResponse, 0, serverInfo);
        await _conn.SendPacket(packet);

        var ticket = long.Parse((string)request.DataDict["PID"]);
        await SendEGEG(request, ticket);
    }

    private async Task HandleGDAT(Packet request)
    {
        var requestGid = request["GID"];

        if (string.IsNullOrWhiteSpace(requestGid))
        {
            var pk = new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, request.DataDict);
            await _conn.SendPacket(pk);
            return;
        }

        var serverGid = long.Parse(request["GID"]);
        var serverInfo = _sharedCache.GetGameServerDataByGid(serverGid);

        if (serverInfo is null)
        {
            _logger.LogWarning("Almost sent GDAT for a non-existant server!");
            return;
        }

        var serverInfoResponse = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["LID"] = serverInfo["LID"],
            ["GID"] = serverInfo["GID"],

            ["HU"] = "1000000000001",
            ["HN"] = "bfbc.server.ps3@ea.com",

            ["I"] = _arcadiaSettings.Value.GameServerAddress,
            ["P"] = serverInfo["PORT"],

            ["N"] = serverInfo["NAME"],
            ["AP"] = 0,
            ["MP"] = serverInfo["MAX-PLAYERS"],
            ["QP"] = serverInfo["B-U-QueueLength"],
            ["JP"] = 1,
            ["PL"] = "PS3",

            ["PW"] = 0,
            ["TYPE"] = serverInfo["TYPE"],
            ["J"] = serverInfo["JOIN"],

            ["B-U-Softcore"] = serverInfo["B-U-Softcore"],
            ["B-U-Hardcore"] = serverInfo["B-U-Hardcore"],
            ["B-U-HasPassword"] = serverInfo["B-U-HasPassword"],
            ["B-U-Punkbuster"] = 0,
            ["B-U-EA"] = serverInfo["B-U-EA"],
            ["B-version"] = serverInfo["B-version"],
            ["V"] = "851434",
            ["B-U-level"] = serverInfo["B-U-level"],
            ["B-U-gamemode"] = serverInfo["B-U-gamemode"],
            ["B-U-sguid"] = serverInfo["B-U-sguid"],
            ["B-U-Time"] = serverInfo["B-U-Time"],
            ["B-U-hash"] = serverInfo["B-U-hash"],
            ["B-U-region"] = serverInfo["B-U-region"],
            ["B-U-public"] = serverInfo["B-U-public"],
            ["B-U-elo"] = serverInfo["B-U-elo"],
            ["B-numObservers"] = serverInfo["B-numObservers"],
            ["B-maxObservers"] = serverInfo["B-maxObservers"],
            ["B-U-Provider"] = serverInfo["B-U-Provider"],
            ["B-U-gameMod"] = serverInfo["B-U-gameMod"],
            ["B-U-QueueLength"] = serverInfo["B-U-QueueLength"]
        };

        var packet = new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, serverInfoResponse);
        await _conn.SendPacket(packet);

        await SendGDET(request, serverInfo);
    }

    private async Task SendGDET(Packet request, IDictionary<string, object> serverInfo)
    {
        UGID = Guid.NewGuid().ToString();
        _sessionCache["UGID"] = UGID;

        var responseData = new Dictionary<string, object>
        {
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
            ["TID"] = request.DataDict["TID"],
            ["UGID"] = _sessionCache["UGID"],
            ["D-AutoBalance"] = serverInfo["D-AutoBalance"],
            ["D-Crosshair"] = serverInfo["D-Crosshair"],
            ["D-FriendlyFire"] = serverInfo["D-FriendlyFire"],
            ["D-KillCam"] = serverInfo["D-KillCam"],
            ["D-Minimap"] = serverInfo["D-Minimap"],
            ["D-MinimapSpotting"] = serverInfo["D-MinimapSpotting"],
            ["D-ServerDescriptionCount"] = 0,
            ["D-ThirdPersonVehicleCameras"] = serverInfo["D-ThirdPersonVehicleCameras"],
            ["D-ThreeDSpotting"] = serverInfo["D-ThreeDSpotting"]
        };

        for (var i = 0; i < 32; i++)
        {
            responseData.Add($"D-pdat{i}", "|0|0|0|0");
        }

        _logger.LogTrace("Sending GDET to client at {endpoint}", _conn.ClientEndpoint);
        var packet = new Packet("GDET", TheaterTransmissionType.OkResponse, 0, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePENT(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["PID"] = request.DataDict["PID"],
        };

        var packet = new Packet("PENT", TheaterTransmissionType.OkResponse, 0, serverInfo);
        await _conn.SendPacket(packet);
    }

    private async Task HandleUBRA(Packet request)
    {
        if (request["START"] != "1")
        {
            var originalTid = (request.DataDict["TID"] as int?) - (_brackets / 2) ?? 0;
            for (var i = 0; i < _brackets; i++)
            {
                var data = new Dictionary<string, object>
                {
                    //TODO: Server responds with unknown if tid=i+1?
                    ["TID"] = request["TID"]
                };

                var packet = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0, data);
                await _conn.SendPacket(packet);
                Interlocked.Decrement(ref _brackets);
            }
        }
        else
        {
            Interlocked.Add(ref _brackets, 2);
        }
    }


    // UpdateGameDetails
    private Task HandleUGDE(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        _logger.LogInformation("Server GID={gid} updating details!", gid);
        _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);
        return Task.CompletedTask;
    }

    // UpdateGameData
    private Task HandleUGAM(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        _logger.LogInformation("Server GID={gid} updating data!", gid);
        _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);
        return Task.CompletedTask;
    }

    private static string UGID = string.Empty;

    private async Task SendEGEG(Packet request, long ticket)
    {
        var serverIp = _arcadiaSettings.Value.GameServerAddress;
        var serverPort = _arcadiaSettings.Value.GameServerPort;

        var ugid = _sessionCache.GetValueOrDefault("UGID");
        if (string.IsNullOrWhiteSpace((string?)ugid))
        {
            _sessionCache["UGID"] = UGID;
        }

        var serverInfo = new Dictionary<string, object>
        {
            ["PL"] = "ps3",
            ["TICKET"] = ticket,
            ["PID"] = _sessionCache["UID"],
            ["HUID"] = "201104017",
            ["EKEY"] = "AIBSgPFqRDg0TfdXW1zUGa4%3d",
            ["UGID"] = _sessionCache["UGID"],

            ["INT-IP"] = serverIp,
            ["INT-PORT"] = serverPort,

            ["I"] = serverIp,
            ["P"] = serverPort,

            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"]
        };

        _logger.LogTrace("Sending EGEG to client at {endpoint}", _conn.ClientEndpoint);
        var packet = new Packet("EGEG", TheaterTransmissionType.OkResponse, 0, serverInfo);
        await _conn.SendPacket(packet);
    }
}