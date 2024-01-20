using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;

namespace Arcadia.Handlers;

public class TheaterServerHandler
{
    private readonly ILogger<TheaterServerHandler> _logger;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;
    private readonly IEAConnection _conn;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private readonly Dictionary<string, object> _sessionCache = [];

    private int _brackets = 0;

    public TheaterServerHandler(IEAConnection conn, ILogger<TheaterServerHandler> logger, SharedCounters sharedCounters, SharedCache sharedCache)
    {
        _logger = logger;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;
        _conn = conn;

        _handlers = new Dictionary<string, Func<Packet, Task>>
        {
            ["CONN"] = HandleCONN,
            ["USER"] = HandleUSER,
            ["CGAM"] = HandleCGAM,
            ["UBRA"] = HandleUBRA,
            ["UGAM"] = HandleUGAM,
            ["EGRS"] = HandleEGRS,
            ["UGDE"] = HandleUGDE
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
        var ugid = Guid.NewGuid().ToString();

        _sharedCache.UpsertGameServerDataByGid(gid, request.DataDict);
        _sharedCache.UpsertGameServerValueByGid(gid, "UGID", ugid);

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

    private static int joiningPlayers = 0;
}