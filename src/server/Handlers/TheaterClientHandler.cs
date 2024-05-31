using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Handlers;

public class TheaterClientHandler
{
    private readonly ILogger<TheaterClientHandler> _logger;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;
    private readonly IOptions<ArcadiaSettings> _arcadiaSettings;
    private readonly IEAConnection _conn;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private readonly Dictionary<string, object> _sessionCache = [];

    public TheaterClientHandler(IEAConnection conn, ILogger<TheaterClientHandler> logger, SharedCounters sharedCounters, SharedCache sharedCache, IOptions<ArcadiaSettings> arcadiaSettings)
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
            ["ECNL"] = HandleECNL,
            ["EGAM"] = HandleEGAM,
            ["GDAT"] = HandleGDAT,
            ["LLST"] = HandleLLST,
            ["GLST"] = HandleGLST,
            ["CGAM"] = HandleCGAM,
            ["PENT"] = HandlePENT,
        };
    }

    public async Task HandleClientConnection(NetworkStream network, string clientEndpoint)
    {
        _conn.InitializeInsecure(network, clientEndpoint);
        await foreach (var packet in _conn.StartConnection(_logger))
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

        await handler(packet);
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
        await _conn.SendPacket(packet);
    }

    private async Task HandleUSER(Packet request)
    {
        var lkey = request.DataDict["LKEY"];
        var username = _sharedCache.GetUsernameByLKey((string)lkey);

        _sessionCache["NAME"] = username;
        _sessionCache["UID"] = _sharedCounters.GetNextUserId();
        _sessionCache["PID"] = _sharedCounters.GetNextPid();
        _logger.LogInformation("USER: {name} {lkey}", username, lkey);

        var response = new Dictionary<string, object>
        {
            ["NAME"] = username,
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("USER", TheaterTransmissionType.OkResponse, 0, response);
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
            // ["LID"] = request.DataDict["LID"],
            // ["GID"] = request.DataDict["GID"],
        };

        var clientPacket = new Packet("EGAM", TheaterTransmissionType.OkResponse, 0, clientResponse);

        var ticket = _sharedCounters.GetNextTicket();
        _sessionCache["TICKET"] = ticket;

        // var srvData = _sharedCache.GetGameServerDataByGid(long.Parse(request["GID"])) ?? throw new NotImplementedException();
        var srvData = _sharedCache.GetGameServerDataByGid(_sharedCache.ListServersGIDs().First());
        _sessionCache["UGID"] = srvData["UGID"];

        await SendEGRQToHost(request, ticket);
        await _conn.SendPacket(clientPacket);
        await SendEGEG(request, ticket, srvData);
    }

    private async Task SendEGRQToHost(Packet request, long ticket)
    {
        var serverNetwork = _sharedCounters.GetServerTheaterNetworkStream();
        if (serverNetwork != null)
        {
            var serverMessage = new Dictionary<string, object>
            {
                ["R-INT-PORT"] = request["R-INT-PORT"],
                ["R-INT-IP"] = request["R-INT-IP"],
                ["PORT"] = request["PORT"],
                ["NAME"] = _sessionCache["NAME"],
                ["PTYPE"] = request["PTYPE"],
                // ["TICKET"] = ticket,
                ["PID"] = _sessionCache["PID"],
                ["UID"] = _sessionCache["UID"],
                ["IP"] = _conn.ClientEndpoint.Split(":")[0],
                // ["LID"] = request.DataDict["LID"],
                // ["GID"] = request.DataDict["GID"],
            };

            var serverPacket = new Packet("EGRQ", TheaterTransmissionType.OkResponse, 0, serverMessage);
            var serverData = await serverPacket.Serialize();
            await serverNetwork.WriteAsync(serverData);
        }
    }

    private async Task HandleGDAT(Packet request)
    {
        var serverGid = long.Parse(request["GID"]);
        var serverInfo = _sharedCache.GetGameServerDataByGid(serverGid) ?? throw new NotImplementedException();

        var serverInfoResponse = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["LID"] = serverInfo["LID"],
            ["GID"] = serverInfo["GID"],

            ["HU"] = "1000000000001",
            ["HN"] = "bfbc.server.ps3",

            ["I"] = _arcadiaSettings.Value.GameServerAddress,
            ["P"] = serverInfo["PORT"],

            ["N"] = serverInfo["NAME"],
            ["AP"] = 0,
            ["MP"] = serverInfo["MAX-PLAYERS"],
            ["JP"] = 1,
            ["PL"] = "ps3",

            ["PW"] = 0,
            ["TYPE"] = serverInfo["TYPE"],
            ["J"] = serverInfo["JOIN"],

            ["B-version"] = serverInfo["B-version"],
            ["B-numObservers"] = serverInfo["B-numObservers"],
            ["B-maxObservers"] = serverInfo["B-maxObservers"]
        };

        var packet = new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, serverInfoResponse);
        await _conn.SendPacket(packet);

        await SendGDET(request, serverInfo);
    }

    private async Task SendGDET(Packet request, IDictionary<string, object> serverInfo)
    {
        var responseData = new Dictionary<string, object>
        {
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
            ["TID"] = request.DataDict["TID"],
            ["UGID"] = serverInfo["UGID"],
        };

        var maxPlayers = int.Parse((string)serverInfo["MAX-PLAYERS"]);
        for (var i = 0; i < maxPlayers; i++)
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

    private async Task SendEGEG(Packet request, long ticket, IDictionary<string, object> serverData)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["PL"] = "pc",
            ["TICKET"] = ticket,
            ["PID"] = _sessionCache["PID"],
            ["HUID"] = serverData["UID"],
            ["EKEY"] = "AIBSgPFqRDg0TfdXW1zUGa4%3d",
            ["UGID"] = serverData["UGID"],

            ["INT-IP"] = serverData["INT-IP"],
            ["INT-PORT"] = serverData["PORT"],

            ["I"] = serverData["INT-IP"],
            ["P"] = serverData["PORT"],

            ["LID"] = serverData["LID"],
            ["GID"] = serverData["GID"]
        };

        _logger.LogTrace("Sending EGEG to client at {endpoint}", _conn.ClientEndpoint);
        var packet = new Packet("EGEG", TheaterTransmissionType.OkResponse, 0, serverInfo);
        await _conn.SendPacket(packet);
    }

    private async Task HandleLLST(Packet request)
    {
        var lobbyList = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["NUM-LOBBIES"] = 1
        };
        await _conn.SendPacket(new Packet("LLST", TheaterTransmissionType.OkResponse, 0, lobbyList));

        var lobbyData = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["PASSING"] = 1,
            ["NAME"] = "bfbc2_01",
            ["LOCALE"] = "en_US",
            ["MAX-GAMES"] = 1000,
            ["FAVORITE-GAMES"] = 0,
            ["FAVORITE-PLAYERS"] = 0,
            ["NUM-GAMES"] = 1,
        };
        await _conn.SendPacket(new Packet("LDAT", TheaterTransmissionType.OkResponse, 0, lobbyData));
    }

    public async Task HandleGLST(Packet request)
    {
        var gameList = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["LOBBY-NUM-GAMES"] = 1,
            ["LOBBY-MAX-GAMES"] = 1000,
            ["FAVORITE-GAMES"] = 0,
            ["FAVORITE-PLAYERS"] = 0,
            ["NUM-GAMES"] = 1
        };
        await _conn.SendPacket(new Packet("GLST", TheaterTransmissionType.OkResponse, 0, gameList));

        var gameServers = _sharedCache.ListServersGIDs();
        
        foreach(var serverGid in gameServers)
        {
            var serverInfo = _sharedCache.GetGameServerDataByGid(serverGid) ?? throw new NotImplementedException();
            // var serverHn = _sharedCache.GetUsernameByLKey((string)serverLkey);
            // var serverLkey = _sharedCache.GetLKeyByUsername(serverHn);

            var gameData = new Dictionary<string, object>
            {
                ["TID"] = request["TID"],
                ["LID"] = request["LID"],
                ["GID"] = serverGid,
                ["HN"] = serverInfo["PID"],
                ["HU"] = 10,
                ["N"] = serverInfo["NAME"],

                ["I"] = serverInfo["INT-IP"],
                ["P"] = serverInfo["PORT"],

                ["MP"] = serverInfo["MAX-PLAYERS"],

                ["F"] = 0,
                ["NF"] = 0,
                ["J"] = serverInfo["JOIN"],
                ["TYPE"] = serverInfo["TYPE"],
                ["PW"] = 0,

                ["B-version"] = serverInfo["B-version"],

                ["B-numObservers"] = serverInfo["B-numObservers"],
                ["B-maxObservers"] = serverInfo["B-maxObservers"],
            };

            await _conn.SendPacket(new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, gameData));
        }
    }

    // CreateGame
    private async Task HandleCGAM(Packet request)
    {
        var gid = _sharedCounters.GetNextGameId();
        var lid = _sharedCounters.GetNextLid();
        var ugid = Guid.NewGuid().ToString();

        _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);
        _sharedCache.UpsertGameServerValueByGid(gid, "UGID", ugid);
        _sharedCache.UpsertGameServerValueByGid(gid, "GID", gid);
        _sharedCache.UpsertGameServerValueByGid(gid, "LID", lid);
        _sharedCache.UpsertGameServerValueByGid(gid, "UID", _sessionCache["UID"]);

        _sharedCounters.SetServerTheaterNetworkStream(_conn.NetworkStream);

        var response = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["MAX-PLAYERS"] = request["MAX-PLAYERS"],
            ["EKEY"] = "AIBSgPFqRDg0TfdXW1zUGa4%3d",
            ["UGID"] = ugid,
            ["JOIN"] = request["JOIN"],
            ["SECRET"] = request["SECRET"],
            ["LID"] = lid,
            ["J"] = request["JOIN"],
            ["GID"] = gid
        };

        var packet = new Packet("CGAM", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePENT(Packet request)
    {
        // var response = new Dictionary<string, object>
        // {
        //     ["TID"] = request["TID"],
        //     ["PID"] = request["PID"],
        //     ["GID"] = request["GID"],
        //     ["LID"] = request["LID"],
        // };

        // var packet = new Packet("PENT", TheaterTransmissionType.OkResponse, 0, response);
        // await _conn.SendPacket(packet);
    }
}