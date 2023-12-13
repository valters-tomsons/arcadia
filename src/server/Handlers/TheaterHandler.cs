using System.Net.Sockets;
using System.Text;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Handlers;

public class TheaterHandler
{
    private NetworkStream _network = null!;
    private string _clientEndpoint = null!;

    private readonly ILogger<TheaterHandler> _logger;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;
    private readonly IOptions<ArcadiaSettings> _arcadiaSettings;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private readonly Dictionary<string, object> _sessionCache = new();

    private int _brackets = 0;

    public TheaterHandler(ILogger<TheaterHandler> logger, SharedCounters sharedCounters, SharedCache sharedCache, IOptions<ArcadiaSettings> arcadiaSettings)
    {
        _logger = logger;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
        _arcadiaSettings = arcadiaSettings;

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
        };
    }

    public async Task HandleClientConnection(NetworkStream network, string clientEndpoint)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;

        while (_network.CanRead)
        {
            int read;
            byte[] readBuffer = new byte[8096];

            try
            {
                read = await _network.ReadAsync(readBuffer.AsMemory());
            }
            catch
            {
                _logger.LogInformation("Connection has been closed: {endpoint}", _clientEndpoint);
                break;
            }

            if (read == 0)
            {
                continue;
            }

            var packet = new Packet(readBuffer[..read]);
            var type = packet.Type;

            _logger.LogDebug("Incoming Type: {type}", type);
            _logger.LogTrace("data:{data}", Encoding.ASCII.GetString(readBuffer[..read]));

            _handlers.TryGetValue(type, out var handler);

            if (handler is null)
            {
                _logger.LogWarning("Unknown packet type: {type}", type);
                continue;
            }

            await handler(packet);
        }
    }

    private async Task HandleCONN(Packet request)
    {
        var tid = request.DataDict["TID"];

        _logger.LogInformation("CONN: {tid}", tid);

        var response = new Dictionary<string, object>
        {
            ["TIME"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["TID"] = tid,
            ["activityTimeoutSecs"] = 0,
            ["PROT"] = request.DataDict["PROT"]
        };

        var packet = new Packet("CONN", TheaterTransmissionType.OkResponse, 0, response);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);
    }

    private async Task HandleUSER(Packet request)
    {
        var lkey = request.DataDict["LKEY"];
        var username = _sharedCache.GetUsernameByKey((string)lkey);

        _logger.LogInformation("USER: {name} {lkey}", username, lkey);

        var response = new Dictionary<string, object>
        {
            ["NAME"] = username,
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("USER", TheaterTransmissionType.OkResponse, 0, response);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);
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
            ["EKEY"] = "",
            ["UGID"] = Guid.NewGuid().ToString(),
            ["JOIN"] = request.DataDict["JOIN"],
            ["SECRET"] = "",
            ["LID"] = lid,
            ["J"] = request.DataDict["JOIN"],
            ["GID"] = gid
        };

        var packet = new Packet("CGAM", TheaterTransmissionType.OkResponse, 0, response);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);
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
        var data = await packet.Serialize();

        await _network.WriteAsync(data);
    }

    // EnterGameRequest
    private async Task HandleEGAM(Packet request)
    {
        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"]
        };

        var packet = new Packet("EGAM", TheaterTransmissionType.OkResponse, 0, response);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);

        await SendEGEG(request);
    }

    private async Task HandleEGRS(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("EGRS", TheaterTransmissionType.OkResponse, 0, serverInfo);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);

        await SendEGEG(request);
    }

    private async Task HandleGDAT(Packet request)
    {
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
            ["B-U-Punkbuster"] = serverInfo["B-U-Punkbuster"],
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
        var data = await packet.Serialize();
        await _network.WriteAsync(data);

        await SendGDET(request, serverInfo);
    }

    private async Task SendGDET(Packet request, IDictionary<string, object> serverInfo)
    {
        _sessionCache["UGID"] = Guid.NewGuid().ToString();

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

        for (var i = 0; i < 24; i++)
        {
            responseData.Add($"D-pdat{i}", "|0|0|0|0");
        }

        var packet = new Packet("GDET", TheaterTransmissionType.OkResponse, 0, responseData);
        var data = await packet.Serialize();

        _logger.LogTrace("Sending GDET to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }

    private async Task HandlePENT(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["PID"] = request.DataDict["PID"],
        };

        var packet = new Packet("PENT", TheaterTransmissionType.OkResponse, 0, serverInfo);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);
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
                    ["TID"] = originalTid + i
                };

                var packet = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0, data);
                await _network.WriteAsync(await packet.Serialize());
                Interlocked.Decrement(ref _brackets);
            }
        }
        else
        {
            Interlocked.Add(ref _brackets, 2);
        }
    }

    private Task HandleUGAM(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        _logger.LogInformation("Server GID={gid} updating info!", gid);
        _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);
        return Task.CompletedTask;
    }

    private async Task SendEGEG(Packet request)
    {
        var serverIp = _arcadiaSettings.Value.GameServerAddress;
        var serverPort = _arcadiaSettings.Value.GameServerPort;

        var serverInfo = new Dictionary<string, object>
        {
            ["PL"] = "ps3",
            ["TICKET"] = _sharedCounters.GetNextTicket(),
            ["PID"] = _sharedCounters.GetNextPid(),
            ["HUID"] = "201104017",
            ["EKEY"] = "",
            ["UGID"] = _sessionCache["UGID"],

            ["INT-IP"] = serverIp,
            ["INT-PORT"] = serverPort,

            ["I"] = serverIp,
            ["P"] = serverPort,

            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"]
        };

        var packet = new Packet("EGEG", TheaterTransmissionType.OkResponse, 0, serverInfo);
        var data = await packet.Serialize();

        _logger.LogTrace("Sending EGEG to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }

    private async Task SendEGRQ()
    {
        var serverIp = _arcadiaSettings.Value.GameServerAddress;
        var serverPort = _arcadiaSettings.Value.GameServerPort;

        var serverInfo = new Dictionary<string, object>
        {
            ["R-INT-PORT"] = serverPort,
            ["R-INT-IP"] = serverIp,
            ["PORT"] = serverPort,
            ["IP"] = serverIp,
            ["NAME"] = "arcadia-ps3",
            ["PTYPE"] = "P",
            ["TICKET"] = "-479505973",
            ["PID"] = 1,
            ["PID"] = 1,
            ["UID"] = 1000000000000,
            ["LID"] = 255,
            ["GID"] = 801000
        };

        var packet = new Packet("EGRQ", TheaterTransmissionType.OkResponse, 0, serverInfo);
        var data = await packet.Serialize();

        _logger.LogTrace("Sending EGRQ to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }
}