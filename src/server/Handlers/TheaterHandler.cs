using System.Collections.Immutable;
using System.Diagnostics;
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
    private readonly ConnectionManager _sharedCache;
    private readonly IEAConnection _conn;
    private readonly DebugSettings _dbgSettings;

    private readonly ImmutableDictionary<string, Func<Packet, Task>> _handlers;

    private PlasmaSession? _plasma;
    private string? _platform;

    public TheaterHandler(IEAConnection conn, ILogger<TheaterHandler> logger, SharedCounters sharedCounters, ConnectionManager sharedCache, IOptions<DebugSettings> dbgOptions)
    {
        _logger = logger;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
        _conn = conn;
        _dbgSettings = dbgOptions.Value;

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
            ["UGAM"] = HandleUGAM,
            ["RGAM"] = HandleRGAM,
            ["PLVT"] = HandlePLVT,
            ["UGDE"] = HandleUGDE,
            ["PING"] = HandlePING
        }.ToImmutableDictionary();
    }

    public async Task HandleClientConnection(NetworkStream network, string clientEndpoint, string serverEndpoint, CancellationToken ct)
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
            _logger.LogError(e, "Error in theater: {Message}", e.Message);
        }

        _logger.LogInformation("Closing Theater connection: {clientEndpoint} | {name}", clientEndpoint, _plasma?.NAME);
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
        _platform = request["PLAT"]?.ToLower();

        var tid = request["TID"];
        var response = new Dictionary<string, string>
        {
            ["TIME"] = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            ["TID"] = $"{tid}",
            ["activityTimeoutSecs"] = "0",
            ["PROT"] = request.DataDict["PROT"]
        };

        var packet = new Packet("CONN", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandleUSER(Packet request)
    {
        var lkey = request["LKEY"];
        _plasma = _sharedCache.PairTheaterConnection(_conn, lkey);

        var response = new Dictionary<string, string>
        {
            ["NAME"] = _plasma.NAME,
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("USER", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // LeaveGame
    private async Task HandleECNL(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["GID"] = request["GID"],
        };

        var isGid = long.TryParse(request["GID"], out var gid);
        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, gid);

        if (game is not null && isGid && _plasma?.UID == game.UID)
        {
            await _sharedCache.RemoveGameListing(game);
        }

        var packet = new Packet("ECNL", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // EnterGameRequest
    private async Task HandleEGAM(Packet request)
    {
        if (_plasma is null) throw new NotImplementedException();

        var reqGid = request["GID"];
        var gid = string.IsNullOrWhiteSpace(reqGid) ? 0 : long.Parse(reqGid);

        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, gid);
        if (game is null)
        {
            var joinPlayerName = request["USER"];
            game = _sharedCache.FindGameWithPlayer(_plasma!.PartitionId, joinPlayerName);

            if (game is null)
            {
                await SendError(request);
                return;
            }
        }

        if (game.UID != _plasma.UID)
        {
            var canJoin = await AwaitOpenGame(game);
            if (!canJoin)
            {
                await SendError(request);
                return;
            }
        }

        var response = new Dictionary<string, string>
        {
            ["TID"] = $"{request["TID"]}",
            ["LID"] = $"{game.LID}",
            ["GID"] = $"{game.GID}",
        };

        var clientPacket = new Packet("EGAM", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(clientPacket);

        _plasma.PID = game.ConnectedPlayers.Count + game.JoiningPlayers.Count + 1;
        await SendEGRQ_ToGameHost(request, _plasma, game);
    }

    private async Task SendError(Packet request)
    {
        Debugger.Break();

        var response = new Dictionary<string, string>
        {
            ["TID"] = $"{request["TID"]}",
            ["localizedMessage"] = "Generic error",
            ["errorContainer"] = "0",
            ["errorCode"] = "100"
        };

        var clientPacket = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(clientPacket);
    }

    private async Task<bool> AwaitOpenGame(GameServerListing game)
    {
        if (_dbgSettings.DisableTheaterJoinTimeout) return true;

        int retries = 0;
        while (!game.CanJoin)
        {
            _logger.LogDebug("Waiting for host game to open...");

            retries++;
            await Task.Delay(1000);

            if (game.TheaterConnection is null || game.TheaterConnection.NetworkStream is null)
            {
                _logger.LogError("Not connecting to game, host not connected");
                return false;
            }

            if (retries == 15)
            {
                _logger.LogWarning("Timeout reached");
                return false;
            }
        }

        return true;
    }

    private static async Task SendEGRQ_ToGameHost(Packet request, PlasmaSession session, GameServerListing server)
    {
        if (session.TheaterConnection is null) throw new NotImplementedException();
        if (server.TheaterConnection is null) throw new NotImplementedException();

        var gameId = server.GID;
        var response = new Dictionary<string, string>
        {
            ["R-INT-PORT"] = $"{request["R-INT-PORT"]}",
            ["R-INT-IP"] = $"{request["R-INT-IP"]}",
            ["PORT"] = $"{request["PORT"]}",
            ["NAME"] = $"{session.NAME}",
            ["PTYPE"] = $"{request["PTYPE"]}",
            ["TICKET"] = $"{server.Data["TICKET"]}",
            ["PID"] = $"{session.PID}",
            ["UID"] = $"{session.UID}",
            ["LID"] = $"{server.LID}",
            ["GID"] = $"{gameId}",
            ["IP"] = session.TheaterConnection.RemoteAddress
        };

        var enterGameRequestPacket = new Packet("EGRQ", TheaterTransmissionType.OkResponse, 0, response);
        await server.TheaterConnection.SendPacket(enterGameRequestPacket);
        server.JoiningPlayers.Enqueue(session);
    }

    private async Task HandleGDAT(Packet request)
    {
        var reqGid = request["GID"];
        var serverGid = string.IsNullOrWhiteSpace(reqGid) ? 0 : int.Parse(reqGid);
        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, serverGid);

        if (game is null || game.TheaterConnection is null)
        {
            await SendError(request);
            return;
        }

        var serverInfo = game.Data;
        var response = new Dictionary<string, string>
        {
            ["TID"] = $"{request["TID"]}",
            ["LID"] = $"{game.LID}",
            ["GID"] = $"{game.GID}",
            ["HU"] = $"{game.UID}",
            ["HN"] = $"{game.NAME}",
            ["I"] = game.TheaterConnection.RemoteAddress,
            ["P"] = $"{serverInfo["PORT"]}",
            ["N"] = $"{game.NAME}",
            ["AP"] = $"{game.ConnectedPlayers.Count}",
            ["MP"] = $"{serverInfo["MAX-PLAYERS"]}",
            ["JP"] = $"{game.JoiningPlayers.Count}",
            ["PL"] = game.Platform,
            ["PW"] = "0",
            ["TYPE"] = $"{serverInfo["TYPE"]}",
            ["J"] = $"{serverInfo["JOIN"]}",
            ["B-version"] = $"{serverInfo["B-version"]}",
            ["B-numObservers"] = $"{serverInfo["B-numObservers"]}",
            ["B-maxObservers"] = $"{serverInfo["B-maxObservers"]}"
        };

        var packet = new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);

        await SendGDET(request, game);
    }

    private async Task SendGDET(Packet request, GameServerListing game)
    {
        var response = new Dictionary<string, string>
        {
            ["LID"] = $"{game.LID}",
            ["GID"] = $"{game.GID}",
            ["TID"] = $"{request["TID"]}",
            ["UGID"] = $"{game.UGID}"
        };

        var maxPlayers = int.Parse(game.Data["MAX-PLAYERS"]);
        for (var i = 0; i < maxPlayers; i++)
        {
            var pdatId = $"D-pdat{i:D2}";
            var validId = game.Data.TryGetValue(pdatId, out var pdat);
            if (!validId | string.IsNullOrEmpty(pdat)) break;
            response.Add(pdatId, pdat!);
        }

        var packet = new Packet("GDET", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task SendEGEG_ToPlayerInQueue(Packet request, int gid)
    {
        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, gid) ?? throw new NotImplementedException();
        var joining = game.JoiningPlayers.TryDequeue(out var player);

        if (request["ALLOWED"] != "1" || joining != true)
        {
            _logger.LogWarning("Host disallowed player join!");
            return;
        }

        if (game.TheaterConnection is null) throw new NotImplementedException();
        if (player?.TheaterConnection is null) throw new NotImplementedException();

        var serverData = game.Data;
        var response = new Dictionary<string, string>
        {
            ["PL"] = game.Platform,
            ["TICKET"] = $"{serverData["TICKET"]}",
            ["PID"] = $"{request["PID"]}",
            ["P"] = $"{serverData["PORT"]}",
            ["HUID"] = $"{game.UID}",
            ["INT-PORT"] = $"{serverData["INT-PORT"]}",
            ["EKEY"] = $"{game.EKEY}",
            ["INT-IP"] = $"{serverData["INT-IP"]}",
            ["UGID"] = $"{game.UGID}",
            ["I"] = game.TheaterConnection.RemoteAddress,
            ["LID"] = $"{game.LID}",
            ["GID"] = $"{game.GID}"
        };

        var packet = new Packet("EGEG", TheaterTransmissionType.OkResponse, 0, response);
        game.ConnectedPlayers.TryAdd(player.UID, player);
        await player.TheaterConnection.SendPacket(packet);
    }

    private async Task HandleLLST(Packet request)
    {
        var lobbyList = new Dictionary<string, string>
        {
            ["TID"] = $"{request["TID"]}",
            ["NUM-LOBBIES"] = $"{1}"
        };
        await _conn.SendPacket(new Packet("LLST", TheaterTransmissionType.OkResponse, 0, lobbyList));

        var lobbyData = new Dictionary<string, string>
        {
            ["TID"] = $"{request["TID"]}",
            ["LID"] = $"{request["LID"]}",
            ["PASSING"] = $"{1}",
            ["NAME"] = $"{"bfbc2_01"}",
            ["LOCALE"] = $"{"en_US"}",
            ["MAX-GAMES"] = $"{1000}",
            ["FAVORITE-GAMES"] = $"{0}",
            ["FAVORITE-PLAYERS"] = $"{0}",
            ["NUM-GAMES"] = $"{1}"
        };
        await _conn.SendPacket(new Packet("LDAT", TheaterTransmissionType.OkResponse, 0, lobbyData));
    }

    public async Task HandleGLST(Packet request)
    {
        var games = _sharedCache.GetPartitionServers(_plasma!.PartitionId).Where(x => x.CanJoin).ToList();

        var gameList = new Dictionary<string,string>
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["LOBBY-NUM-GAMES"] = $"{games.Count}",
            ["LOBBY-MAX-GAMES"] = "100",
            ["FAVORITE-GAMES"] = "0",
            ["FAVORITE-PLAYERS"] = "0",
            ["NUM-GAMES"] = $"{games.Count}"
        };
        await _conn.SendPacket(new Packet("GLST", TheaterTransmissionType.OkResponse, 0, gameList));

        foreach (var game in games)
        {
            var gameData = new Dictionary<string, string>
            {
                ["TID"] = request["TID"],
                ["LID"] = request["LID"],
                ["GID"] = $"{game.GID}",
                ["HN"] = game.NAME,
                ["HU"] = $"{game.UID}",
                ["N"] = game.NAME,
                ["I"] = game.TheaterConnection?.RemoteAddress ?? throw new NotImplementedException(),
                ["P"] = game.Data["PORT"],
                ["MP"] = game.Data["MAX-PLAYERS"],
                ["F"] = "0",
                ["NF"] = "0",
                ["J"] = game.Data["JOIN"],
                ["TYPE"] = game.Data["TYPE"],
                ["PW"] = "0",
                ["B-version"] = game.Data["B-version"],
                ["B-numObservers"] = game.Data["B-numObservers"],
                ["B-maxObservers"] = game.Data["B-maxObservers"],
                ["AP"] = $"{game.ConnectedPlayers.Count}"
            };

            await _conn.SendPacket(new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, gameData));
        }
    }

    // CreateGame
    private async Task HandleCGAM(Packet request)
    {
        if (_plasma is null) throw new NotImplementedException();
        if (_platform is null) throw new Exception("Cannot create game with null platform!");

        var game = new GameServerListing()
        {
            PartitionId = _plasma.PartitionId,
            TheaterConnection = _conn,
            UID = _plasma.UID,
            GID = _sharedCounters.GetNextGameId(),
            Platform = _platform,
            LID = 257,
            UGID = "NOGUID",
            EKEY = "NOENCYRPTIONKEY",
            SECRET = "NOSECRET",
            NAME = request["NAME"],
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
                ["TICKET"] = $"{_sharedCounters.GetNextTicket()}"
            }
        };

        if (string.IsNullOrWhiteSpace(game.NAME))
        {
            await SendError(request);
            return;
        }

        await _sharedCache.AddGameListing(game, request.DataDict);

        var response = new Dictionary<string, string>
        {
            ["TID"] = $"{request["TID"]}",
            ["MAX-PLAYERS"] = $"{game.Data["MAX-PLAYERS"]}",
            ["EKEY"] = $"{game.EKEY}",
            ["UGID"] = $"{game.UGID}",
            ["JOIN"] = $"{game.Data["JOIN"]}",
            ["LID"] = $"{game.LID}",
            ["SECRET"] = $"{game.SECRET}",
            ["J"] = $"{game.Data["JOIN"]}",
            ["GID"] = $"{game.GID}",
            ["HXFR"] = game.Data["HXFR"]
        };

        var packet = new Packet("CGAM", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePENT(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"],
            ["PID"] = request["PID"]
        };

        var packet = new Packet("PENT", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandleEGRS(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"]
        };

        var packet = new Packet("EGRS", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);

        var gid = int.Parse(request["GID"]);
        await SendEGEG_ToPlayerInQueue(request, gid);
    }

    private int _brackets = 0;
    private async Task HandleUBRA(Packet request)
    {
        if (request["START"] == "1")
        {
            _brackets += 2;
        }
        else
        {
            var reqTid = int.Parse(request["TID"]);
            var originalTid = reqTid - _brackets / 2;

            for (var packet = 0; packet < _brackets; packet++)
            {
                var response = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0);
                response["TID"] = $"{originalTid + packet}";

                await _conn.SendPacket(response);
                _brackets--;
            }
        }
    }

    private Task HandleUGDE(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, gid) ?? throw new NotImplementedException();

        _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);

        if (!string.IsNullOrWhiteSpace(request["B-U-level"]))
        {
            game.CanJoin = true;
        }

        return Task.CompletedTask;
    }

    private Task HandleUGAM(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, gid) ?? throw new NotImplementedException();

        if (game.UID == _plasma?.UID)
        {
            _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);

            if (!string.IsNullOrWhiteSpace(request["JOIN"]))
            {
                game.CanJoin = true;
            }
        }

        return Task.CompletedTask;
    }

    private async Task HandleRGAM(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, gid) ?? throw new NotImplementedException();

        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"]
        };

        var packet = new Packet("RGAM", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);

        await _sharedCache.RemoveGameListing(game);
    }

    private async Task HandlePLVT(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        var game = _sharedCache.GetGameByGid(_plasma!.PartitionId, gid) ?? throw new NotImplementedException();

        var pid = int.Parse(request["PID"]);
        var player = game.ConnectedPlayers.Values.SingleOrDefault(x => x.PID == pid);
        if (player is not null)
        {
            game.ConnectedPlayers.Remove(player.UID, out var _);
        }

        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"]
        };

        var packet = new Packet("PLVT", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private Task HandlePING(Packet _)
    {
        return Task.CompletedTask;
    }
}