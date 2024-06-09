using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;

namespace Arcadia.Handlers;

public class TheaterClientHandler
{
    private readonly ILogger<TheaterClientHandler> _logger;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;
    private readonly IEAConnection _conn;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private TheaterClient? _session;

    public TheaterClientHandler(IEAConnection conn, ILogger<TheaterClientHandler> logger, SharedCounters sharedCounters, SharedCache sharedCache)
    {
        _logger = logger;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
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
            ["EGRS"] = HandleEGRS,
            ["UBRA"] = HandleUBRA,
            ["UGAM"] = HandleUGAM
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
        var lkey = request["LKEY"];

        _session = new()
        {
            UID = _sharedCounters.GetNextUserId() - 1,
            LKEY = lkey,
            NAME = _sharedCache.GetUsernameByLKey(lkey),
            TheaterConnection = _conn
        };
        _sharedCache.AddTheaterConnection(_session);

        _logger.LogInformation("USER: {name} {lkey}", _session.NAME, lkey);
        var response = new Dictionary<string, object>
        {
            ["NAME"] = _session.NAME,
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("USER", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // LeaveGame
    private async Task HandleECNL(Packet request)
    {
        var response = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["GID"] = request["GID"],
        };

        var packet = new Packet("ECNL", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // EnterGameRequest
    private async Task HandleEGAM(Packet request)
    {
        if (_session is null) throw new NotImplementedException();

        var reqGid = request["GID"];
        var gid = string.IsNullOrWhiteSpace(reqGid) ? 0 : int.Parse(reqGid);

        GameServerListing? game;
        if (gid == 0)
        {
            gid = (int)_sharedCache.ListGameGids().First();
            game = _sharedCache.GetGameByGid(gid);
        }
        else
        {
            game = _sharedCache.GetGameByGid(gid);
        }

        if (game is null)
        {
            throw new NotImplementedException();
        }

        var clientResponse = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["LID"] = game.LID,
            ["GID"] = gid,
        };

        var clientPacket = new Packet("EGAM", TheaterTransmissionType.OkResponse, 0, clientResponse);
        await _conn.SendPacket(clientPacket);

        _session.PID = game.ConnectedPlayers.Count + game.JoiningPlayers.Count + 1;
        await SendEGRQToHost(request, _session, game);
    }

    private static async Task SendEGRQToHost(Packet request, TheaterClient session, GameServerListing server)
    {
        var gameId = server.GID;
        var serverMessage = new Dictionary<string, object>
        {
            ["R-INT-PORT"] = request["R-INT-PORT"],
            ["R-INT-IP"] = request["R-INT-IP"],
            ["PORT"] = request["PORT"],
            ["NAME"] = session.NAME,
            ["PTYPE"] = request["PTYPE"],
            ["TICKET"] = server.Data["TICKET"],
            ["PID"] = session.PID,
            ["UID"] = session.UID,
            ["LID"] = server.LID,
            ["GID"] = gameId
        };

        var enterGameRequestPacket = new Packet("EGRQ", TheaterTransmissionType.OkResponse, 0, serverMessage);
        await server.TheaterConnection.SendPacket(enterGameRequestPacket);
        server.JoiningPlayers.Enqueue(session);
    }

    private async Task HandleGDAT(Packet request)
    {
        var reqGid = request["GID"];
        var serverGid = string.IsNullOrWhiteSpace(reqGid) ? 0 : int.Parse(reqGid);
        if (serverGid == 0)
        {
            serverGid = (int)_sharedCache.ListGameGids().First();
        }

        var game = _sharedCache.GetGameByGid(serverGid) ?? throw new NotImplementedException();
        var serverInfo = game.Data;

        var serverInfoResponse = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["LID"] = game.LID,
            ["GID"] = game.GID,
            ["HU"] = game.UID,
            ["HN"] = game.NAME,

            ["I"] = serverInfo["INT-PORT"],
            ["P"] = serverInfo["PORT"],

            ["N"] = game.NAME,
            ["AP"] = 0,
            ["MP"] = serverInfo["MAX-PLAYERS"],
            ["JP"] = game.JoiningPlayers.Count,
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

        await SendGDET(request, game);
    }

    private async Task SendGDET(Packet request, GameServerListing game)
    {
        var responseData = new Dictionary<string, object>
        {
            ["LID"] = game.LID,
            ["GID"] = game.GID,
            ["TID"] = request["TID"],
            ["UGID"] = game.UGID
        };

        var maxPlayers = int.Parse((string)game.Data["MAX-PLAYERS"]);
        for (var i = 0; i < maxPlayers; i++)
        {
            var pdatId = $"D-pdat{i:D2}";
            var validId = game.Data.TryGetValue(pdatId, out var pdat);
            if (!validId | string.IsNullOrEmpty(pdat as string)) break;
            responseData.Add(pdatId, pdat!);
        }

        _logger.LogTrace("Sending GDET to client at {endpoint}", _conn.ClientEndpoint);
        var packet = new Packet("GDET", TheaterTransmissionType.OkResponse, 0, responseData);
        await _conn.SendPacket(packet);
    }

    private async Task SendEGEGToPlayer(Packet request, int gid)
    {
        var game = _sharedCache.GetGameByGid(gid) ?? throw new NotImplementedException();
        var joining = game.JoiningPlayers.TryDequeue(out var player);

        if (request["ALLOWED"] != "1" || joining != true)
        {
            _logger.LogWarning("Host disallowed player join!");
            return;
        }

        var serverData = game.Data;
        var serverInfo = new Dictionary<string, object>
        {
            ["PL"] = "ps3",
            ["TICKET"] = serverData["TICKET"],
            ["PID"] = request["PID"],
            ["P"] = serverData["INT-IP"],
            ["HUID"] = game.UID,
            ["INT-PORT"] = serverData["INT-PORT"],
            ["EKEY"] = game.EKEY,
            ["INT-IP"] = serverData["INT-IP"],
            ["UGID"] = game.UGID,
            ["I"] = serverData["INT-IP"],
            ["LID"] = game.LID,
            ["GID"] = game.GID
        };

        _logger.LogTrace("Sending EGEG to client at {endpoint}", _conn.ClientEndpoint);
        var packet = new Packet("EGEG", TheaterTransmissionType.OkResponse, 0, serverInfo);

        game.ConnectedPlayers.Add(player);
        await player.TheaterConnection.SendPacket(packet);
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

        var gameServers = _sharedCache.ListGameGids();
        
        foreach(var serverGid in gameServers)
        {
            var game = _sharedCache.GetGameByGid(serverGid) ?? throw new NotImplementedException();
            var gameData = new Dictionary<string, object>
            {
                ["TID"] = request["TID"],
                ["LID"] = request["LID"],
                ["GID"] = serverGid,
                ["HN"] = game.NAME,
                ["HU"] = game.UID,
                ["N"] = game.NAME,

                ["I"] = game.Data["INT-IP"],
                ["P"] = game.Data["PORT"],

                ["MP"] = game.Data["MAX-PLAYERS"],

                ["F"] = 0,
                ["NF"] = 0,
                ["J"] = game.Data["JOIN"],
                ["TYPE"] = game.Data["TYPE"],
                ["PW"] = 0,

                ["B-version"] = game.Data["B-version"],

                ["B-numObservers"] = game.Data["B-numObservers"],
                ["B-maxObservers"] = game.Data["B-maxObservers"],
            };

            await _conn.SendPacket(new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, gameData));
        }
    }

    // CreateGame
    private async Task HandleCGAM(Packet request)
    {
        if (_session is null) throw new NotImplementedException();

        var game = new GameServerListing()
        {
            TheaterConnection = _conn,
            UID = _session.UID,
            GID = _sharedCounters.GetNextGameId(),
            LID = 257,
            UGID = "NOGUID",
            EKEY = "NOENCYRPTIONKEY", // Yes, that's the actual string
            SECRET = "NOSECRET",
            Data = new()
            {
                ["RESERVE-HOST"] = request["RESERVE-HOST"],
                ["NAME"] = request["NAME"],
                ["PORT"] = request["PORT"],
                ["HTTYPE"] = request["HTTYPE"],
                ["TYPE"] = request["TYPE"],
                ["QLEN"] = request["QLEN"],
                ["DISABLE-AUTO-DEQUEUE"] = request["DISABLE-AUTO-DEQUEUE"],
                ["HXFR"] = request["HXFR"],
                ["INT-PORT"] = request["INT-PORT"],
                ["INT-IP"] = request["INT-IP"],
                ["MAX-PLAYERS"] = request["MAX-PLAYERS"],
                ["B-maxObservers"] = request["B-maxObservers"],
                ["B-numObservers"] = request["B-numObservers"],
                ["B-version"] = request["B-version"],
                ["JOIN"] = request["JOIN"],
                ["RT"] = request["RT"],
                ["TICKET"] = _sharedCounters.GetNextTicket()
            }
        };

        _sharedCache.AddGame(game);

        var response = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["MAX-PLAYERS"] = game.Data["MAX-PLAYERS"],
            ["EKEY"] = game.EKEY,
            ["UGID"] = game.UGID,
            ["JOIN"] = game.Data["JOIN"],
            ["LID"] = game.LID,
            ["SECRET"] = game.SECRET,
            ["J"] = game.Data["JOIN"],
            ["GID"] = game.GID,
        };

        var packet = new Packet("CGAM", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePENT(Packet request)
    {
        var response = new Dictionary<string, object>
        {
            ["TID"] = request["TID"],
            ["PID"] = request["PID"]
        };

        var packet = new Packet("PENT", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandleEGRS(Packet request)
    {
        var response = new Dictionary<string, object>
        {
            ["TID"] = request["TID"]
        };

        var packet = new Packet("EGRS", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);

        var gid = int.Parse(request["GID"]);
        await SendEGEGToPlayer(request, gid);
    }

    private int _brackets = 0;
    private async Task HandleUBRA(Packet request)
    {
        if (request["START"] == "1")
        {
            Interlocked.Add(ref _brackets, 2);
        }
        else
        {
            var brackets = Thread.VolatileRead(ref _brackets);
            var reqTid = int.Parse(request["TID"]);
            var originalTid = reqTid - brackets / 2;

            for (var packet = 0; packet < brackets; packet++)
            {
                var response = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0);
                response.DataDict["TID"] = originalTid + packet;
                await _conn.SendPacket(response);
                Interlocked.Decrement(ref _brackets);
            }
        }
    }

    private Task HandleUGAM(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        var game = _sharedCache.GetGameByGid(gid) ?? throw new NotImplementedException();

        if (game.UID == _session?.UID)
        {
            _logger.LogInformation("Updating server data");
            _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);
        }

        return Task.CompletedTask;
    }
}