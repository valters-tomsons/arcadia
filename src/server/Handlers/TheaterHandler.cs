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

    private PlasmaSession? _session;
    private string? _platform;
    private long? _UBRA_TID = null;

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
            ["PING"] = HandlePING,
            ["PCNT"] = HandlePCNT,
            ["UPLA"] = HandleUPLA,
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

        _logger.LogInformation("Closing Theater connection: {clientEndpoint} | {name}", clientEndpoint, _session?.User.Username);
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
        _session = _sharedCache.PairTheaterConnection(_conn, lkey);

        var response = new Dictionary<string, string>
        {
            ["NAME"] = _session.User.Username,
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
        var game = _sharedCache.GetGameByGid(_session!.PartitionId, gid);

        if (game is not null && isGid && _session.User.UserId == game.UID)
        {
            await _sharedCache.RemoveGameListing(game);
        }

        var packet = new Packet("ECNL", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // EnterGameRequest
    private async Task HandleEGAM(Packet request)
    {
        if (_session is null) throw new NotImplementedException();

        var reqGid = request["GID"];
        var gid = string.IsNullOrWhiteSpace(reqGid) ? 0 : long.Parse(reqGid);

        var game = _sharedCache.GetGameByGid(_session!.PartitionId, gid);
        if (game is null)
        {
            var joinPlayerName = request["USER"];
            game = _sharedCache.FindGameWithPlayer(_session!.PartitionId, joinPlayerName);

            if (game is null)
            {
                await SendError(request);
                return;
            }
        }

        if (game.UID != _session.User.UserId)
        {
            var canJoin = await AwaitOpenGame(game);
            if (!canJoin)
            {
                await SendError(request);
                return;
            }
        }

        _session.EGAM_TID = long.Parse(request["TID"]);

        game.EnqueuePlayer(_session);

        await SendEGRQ_ToGameHost(request, _session, game);
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

    private static async Task SendEGRQ_ToGameHost(Packet request, PlasmaSession player, GameServerListing server)
    {
        if (player.TheaterConnection is null) throw new NotImplementedException();
        if (server.TheaterConnection is null) throw new NotImplementedException();

        var gameId = server.GID;
        var response = new Dictionary<string, string>
        {
            ["R-INT-PORT"] = $"{request["R-INT-PORT"]}",
            ["R-INT-IP"] = $"{request["R-INT-IP"]}",
            ["PORT"] = $"{request["PORT"]}",
            ["NAME"] = player.User.Username,
            ["PTYPE"] = $"{request["PTYPE"]}",
            ["TICKET"] = $"{server.Data["TICKET"]}",
            ["PID"] = $"{player.PID}",
            ["UID"] = $"{player.User.UserId}",
            ["LID"] = $"{server.LID}",
            ["GID"] = $"{gameId}",
            ["IP"] = player.TheaterConnection.RemoteAddress,
        };

        if (request.DataDict.TryGetValue("R-U-passkey", out var passkey) && !string.IsNullOrWhiteSpace(passkey))
        {
            response.Add("R-U-passkey", passkey);
        }

        if (request.DataDict.TryGetValue("R-U-invited", out var invited) && !string.IsNullOrWhiteSpace(invited))
        {
            response.Add("R-U-invited", invited);
        }

        var enterGameRequestPacket = new Packet("EGRQ", TheaterTransmissionType.OkResponse, 0, response);
        await server.TheaterConnection.SendPacket(enterGameRequestPacket);
    }

    private async Task HandleGDAT(Packet request)
    {
        if (!long.TryParse(request["GID"], out var serverGid))
        {
            if (request["TYPE"] == "G")
            {
                await HandleGLST(request);
                return;
            }
        }

        var game = _sharedCache.GetGameByGid(_session!.PartitionId, serverGid);

        if (game is null || game.TheaterConnection is null)
        {
            await SendError(request);
            return;
        }

        await SendGameData(request, game);
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

    private async Task FinishPlayerEnterGameRequest(Packet serverResponse, int gid)
    {
        var server = _sharedCache.GetGameByGid(_session!.PartitionId, gid) ?? throw new NotImplementedException();
        if (server.TheaterConnection is null) throw new NotImplementedException();

        var pid = int.Parse(serverResponse["PID"]);
        var player = server.DequeuePlayer(pid);
        if (player?.TheaterConnection is null) return;

        var egamResp = new Dictionary<string, string>
        {
            ["TID"] = $"{player.EGAM_TID}",
            ["LID"] = $"{server.LID}",
            ["GID"] = $"{server.GID}",
            ["ALLOWED"] = serverResponse["ALLOWED"]
        };

        await player.TheaterConnection.SendPacket(new("EGAM", TheaterTransmissionType.OkResponse, 0, egamResp));

        if (serverResponse["ALLOWED"] != "1")
        {
            _logger.LogWarning("Host disallowed player join!");
            egamResp.Add("REASON", serverResponse["REASON"]);

            // Send it anyway...
        }

        server.ConnectedPlayers.TryAdd(player.User.UserId, player);

        var serverData = server.Data;
        var egegResp = new Dictionary<string, string>
        {
            ["PL"] = server.Platform,
            ["TICKET"] = $"{serverData["TICKET"]}",
            ["PID"] = $"{serverResponse["PID"]}",
            ["P"] = $"{serverData["PORT"]}",
            ["HUID"] = $"{server.UID}",
            ["INT-PORT"] = $"{serverData["INT-PORT"]}",
            ["EKEY"] = $"{server.EKEY}",
            ["INT-IP"] = $"{serverData["INT-IP"]}",
            ["UGID"] = $"{server.UGID}",
            ["I"] = server.TheaterConnection.RemoteAddress,
            ["LID"] = $"{server.LID}",
            ["GID"] = $"{server.GID}"
        };

        await player.TheaterConnection.SendPacket(new("EGEG", TheaterTransmissionType.OkResponse, 0, egegResp));
    }

    private async Task HandleLLST(Packet request)
    {
        if (_session is null) throw new NotImplementedException();

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
            ["NAME"] = _session.PartitionId.Split('/', StringSplitOptions.TrimEntries).LastOrDefault()?.ToLower() ?? "arcadia",
            ["LOCALE"] = $"{"en_US"}",
            ["MAX-GAMES"] = $"{1000}",
            ["FAVORITE-GAMES"] = $"{0}",
            ["FAVORITE-PLAYERS"] = $"{0}",
            ["NUM-GAMES"] = $"{_sharedCache.GetPartitionServers(_session.PartitionId).Length}"
        };
        
        await _conn.SendPacket(new Packet("LDAT", TheaterTransmissionType.OkResponse, 0, lobbyData));
    }

    public async Task HandleGLST(Packet request)
    {
        if (_session is null) throw new NotImplementedException();

        var games = _sharedCache
                .GetPartitionServers(_session.PartitionId)
                .Where(x => x.CanJoin)
                .ToList();

        if (_session.PartitionId.EndsWith("LOTR"))
        {
            games = [.. games.Where(x => x.Data["B-U-FriendsOnly"] == request["FILTER-ATTR-U-FriendsOnly"])];
        }

        await _conn.SendPacket(new("GLST", TheaterTransmissionType.OkResponse, 0)
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["LOBBY-NUM-GAMES"] = $"{games.Count}",
            ["LOBBY-MAX-GAMES"] = "100",
            ["FAVORITE-GAMES"] = "0",
            ["FAVORITE-PLAYERS"] = "0",
            ["NUM-GAMES"] = $"{games.Count}"
        });

        foreach (var game in games)
        {
            await SendGameData(request, game);
        }
    }

    private async Task SendGameData(Packet request, GameServerListing game)
    {
        if (game.TheaterConnection is null) return;
        if (_session is null) return;

        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["GID"] = $"{game.GID}",
            ["HU"] = $"{game.UID}",
            ["HN"] = game.NAME,
            ["I"] = game.TheaterConnection.RemoteAddress,
            ["P"] = $"{game.Data["PORT"]}",
            ["N"] = game.NAME.Replace("\"", string.Empty),
            ["AP"] = $"{game.ConnectedPlayers.Count}",
            ["MP"] = $"{game.Data["MAX-PLAYERS"]}",
            ["JP"] = "0",
            ["PL"] = game.Platform,
            ["PW"] = "0",
            ["QP"] = "0",
            ["TYPE"] = $"{game.Data["TYPE"]}",
            ["J"] = $"{game.Data["JOIN"]}",
            ["B-version"] = $"{game.Data["B-version"]}",
            ["B-numObservers"] = $"{game.Data["B-numObservers"]}",
            ["B-maxObservers"] = $"{game.Data["B-maxObservers"]}",
            ["F"] = "0",
            ["NF"] = "0",
        };

        var subdomain = game.PartitionId.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
        switch (subdomain)
        {
            case "MERCS2":
                response.Add("B-U-Oil", game.Data["B-U-Oil"] ?? "0");
                response.Add("B-U-Money", game.Data["B-U-Money"] ?? "0");
                response.Add("B-U-Duration", game.Data["B-U-Duration"] ?? "0");
                response.Add("B-U-Character", game.Data["B-U-Character"] ?? "0");
                response.Add("B-U-Mission", game.Data["B-U-Mission"] ?? string.Empty);
                break;
            case "GODFATHER2":
                response.Add("B-U-DonFlow", game.Data["B-U-DonFlow"] ?? "0");
                response.Add("B-U-DonMode", game.Data["B-U-DonMode"] ?? "0");
                response.Add("B-U-DonWager", game.Data["B-U-DonWager"] ?? "0");
                response.Add("B-U-IsStrictNAT", game.Data["B-U-IsStrictNAT"] ?? "0");
                response.Add("B-U-MapHash", game.Data["B-U-MapHash"] ?? "0");
                response.Add("B-U-MatchTypeIndex", game.Data["B-U-MatchTypeIndex"] ?? "0");
                response.Add("B-U-ModeIdx", game.Data["B-U-ModeIdx"] ?? "0");
                response.Add("B-U-ModeRequested", game.Data["B-U-ModeRequested"] ?? "0");
                response.Add("B-U-PackedAttributes", game.Data["B-U-PackedAttributes"] ?? "0");
                response.Add("B-U-RoundScore", game.Data["B-U-RoundScore"] ?? "0");
                response.Add("B-U-ScoreLimit", game.Data["B-U-ScoreLimit"] ?? "0");
                response.Add("B-U-WeaponIdx", game.Data["B-U-WeaponIdx"] ?? "0");
                break;
            case "LOTR":
                response.Add("B-U-LevelKey", game.Data["B-U-LevelKey"]);
                response.Add("B-U-LevelName", game.Data["B-U-LevelName"]);
                response.Add("B-U-Mode", game.Data["B-U-Mode"]);
                break;
            case "AO3":
                if (game.Data["B-U-Mode"] == "CAMPAIGN")
                {
                    response.Add("B-U-Difficulty", game.Data["B-U-Difficulty"]);
                    response.Add("B-U-Friendly", game.Data["B-U-Friendly"]);
                    response.Add("B-U-PlayerSkin", game.Data["B-U-PlayerSkin"]);
                }
                else
                {
                    response.Add("B-U-BucketSessionCalibre", game.Data["B-U-BucketSessionCalibre"]);
                    response.Add("B-U-FirewallStrict", game.Data["B-U-FirewallStrict"]);
                    response.Add("B-U-RealSessionCalibre", game.Data["B-U-RealSessionCalibre"]);
                    response.Add("B-U-RoundLength", game.Data["B-U-RoundLength"]);
                    response.Add("B-U-PlayerCount", game.Data["B-U-PlayerCount"]);
                    response.Add("B-U-MapPlaylist", game.Data["B-U-MapPlaylist"]);
                    response.Add("B-U-MaxPlayers", game.Data["B-U-MaxPlayers"]);
                    response.Add("B-U-MinPlayers", game.Data["B-U-MinPlayers"]);
                    response.Add("B-U-GameType", game.Data["B-U-GameType"]);
                }

                response.Add("B-U-ChangeList", game.Data["B-U-ChangeList"]);
                response.Add("B-U-DLC", game.Data["B-U-DLC"]);
                response.Add("B-U-Map", game.Data["B-U-Map"]);
                response.Add("B-U-Mode", game.Data["B-U-Mode"]);
                response.Add("B-U-NAT", game.Data["B-U-NAT"]);
                response.Add("B-U-Private", game.Data["B-U-Private"]);
                response.Add("B-U-RB", game.Data["B-U-RB"]);
                response.Add("B-U-RBHost", game.Data["B-U-RBHost"]);
                response.Add("B-U-RBState", game.Data["B-U-RBState"]);
                response.Add("B-U-ping_site", game.Data["B-U-ping_site"]);
                break;
        }

        var packet = new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    // CreateGame
    private async Task HandleCGAM(Packet request)
    {
        if (_session is null) throw new NotImplementedException();
        if (_platform is null) throw new Exception("Cannot create game with null platform!");

        if (_session!.PartitionId.EndsWith("MERCS2") || _session.PartitionId.EndsWith("GODFATHER2"))
        {
            request["NAME"] = _session.User.Username;
        }

        if (request["RESERVE-HOST"] == "1")
        {
            _logger.LogWarning("Client creating game in '{PartitionId}' with RESERVE-HOST=1", _session.PartitionId);
        }

        var game = new GameServerListing()
        {
            PartitionId = _session.PartitionId,
            TheaterConnection = _conn,
            UID = _session.User.UserId,
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
        await FinishPlayerEnterGameRequest(request, gid);
    }

    private async Task HandleUBRA(Packet request)
    {
        var tid = long.Parse(request["TID"]);

        if (request["START"] == "1")
        {
            _UBRA_TID = tid;
        }
        else
        {
            if (1 > _UBRA_TID) throw new NotImplementedException("Tried to UBRA without START");

            for (var i = _UBRA_TID; i < tid + 1; i++)
            {
                await _conn.SendPacket(new(request.Type, TheaterTransmissionType.OkResponse, 0)
                {
                    ["TID"] = $"{i}"
                });
            }

            _UBRA_TID = null;
        }
    }

    private Task HandleUGDE(Packet request)
    {
        var gid = long.Parse(request["GID"]);
        var game = _sharedCache.GetGameByGid(_session!.PartitionId, gid) ?? throw new NotImplementedException();

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
        var game = _sharedCache.GetGameByGid(_session!.PartitionId, gid) ?? throw new NotImplementedException();

        if (game.UID == _session.User.UserId)
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
        var game = _sharedCache.GetGameByGid(_session!.PartitionId, gid) ?? throw new NotImplementedException();

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
        var game = _sharedCache.GetGameByGid(_session!.PartitionId, gid) ?? throw new NotImplementedException();

        var pid = int.Parse(request["PID"]);
        var player = game.ConnectedPlayers.Values.FirstOrDefault(x => x.PID == pid);
        if (player is not null)
        {
            game.ConnectedPlayers.Remove(player.User.UserId, out var _);
        }

        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"]
        };

        var packet = new Packet("PLVT", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePCNT(Packet request)
    {
        if (_session is null) throw new NotImplementedException();

        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"],
            ["LID"] = request["LID"],
            ["COUNT"] = $"{_sharedCache.GetPartitionPlayerCount(_session.PartitionId)}"
        };

        var packet = new Packet("PCNT", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandleUPLA(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            ["TID"] = request["TID"],
        };

        var packet = new Packet("UPLA", TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private Task HandlePING(Packet _)
    {
        return Task.CompletedTask;
    }
}