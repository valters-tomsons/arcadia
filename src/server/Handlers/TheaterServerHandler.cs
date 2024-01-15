using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Handlers;

public class TheaterServerHandler
{
    private readonly ILogger<TheaterServerHandler> _logger;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;
    private readonly IOptions<ArcadiaSettings> _arcadiaSettings;
    private readonly IEAConnection _conn;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private readonly Dictionary<string, object> _sessionCache = [];

    private int _brackets = 0;

    public TheaterServerHandler(IEAConnection conn, ILogger<TheaterServerHandler> logger, SharedCounters sharedCounters, SharedCache sharedCache, IOptions<ArcadiaSettings> arcadiaSettings)
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
            ["UBRA"] = HandleUBRA,
            ["UGAM"] = HandleUGAM,
            ["GDAT"] = HandleGDAT,
            ["EGRS"] = HandleEGRS,
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
            _sharedCounters.SetServerTheaterNetworkStream(_conn.NetworkStream!);
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

    private async Task HandleEGRS(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
        };

        if (request["ALLOWED"] == "1")
        {
            Interlocked.Increment(ref joiningPlayers);
        }

        var packet = new Packet("EGRS", TheaterTransmissionType.OkResponse, 0, serverInfo);
        await _conn.SendPacket(packet);
    }

    private async Task HandleGDAT(Packet request)
    {
        var requestGid = request["GID"];

        if (string.IsNullOrWhiteSpace(requestGid))
        {
            var earlyExit = new Dictionary<string, object>
            {
                ["TID"] = request["TID"]
            };

            var pk = new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, earlyExit);
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
            ["AP"] = activePlayers,
            ["MP"] = serverInfo["MAX-PLAYERS"],
            ["JP"] = joiningPlayers,
            ["PL"] = "PS3",

            ["PW"] = 0,
            ["TYPE"] = serverInfo["TYPE"],
            ["J"] = serverInfo["JOIN"],

            ["B-U-Hardcore"] = serverInfo["B-U-Hardcore"],
            ["B-U-HasPassword"] = serverInfo["B-U-HasPassword"],
            ["B-U-Punkbuster"] = 0,
            ["B-version"] = serverInfo["B-version"],
            ["V"] = "515757",
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

            // Not in R11
            // ["QP"] = serverInfo["B-U-QueueLength"],
            // ["B-U-Softcore"] = serverInfo["B-U-Softcore"],
            // ["B-U-EA"] = serverInfo["B-U-EA"],
            // ["B-U-Provider"] = serverInfo["B-U-Provider"],
            // ["B-U-gameMod"] = serverInfo["B-U-gameMod"],
            // ["B-U-QueueLength"] = serverInfo["B-U-QueueLength"]
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
            var pdatId = $"D-pdat{i:D2}";
            var validId = serverInfo.TryGetValue(pdatId, out var pdat);
            if (!validId | string.IsNullOrEmpty(pdat as string)) break;
            responseData.Add(pdatId, pdat!);
        }

        _logger.LogTrace("Sending GDET to client at {endpoint}", _conn.ClientEndpoint);
        var packet = new Packet("GDET", TheaterTransmissionType.OkResponse, 0, responseData);
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
    private static int activePlayers = 0;
    private static int joiningPlayers = 0;
}