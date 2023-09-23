using System.Net.Sockets;
using System.Text;
using Arcadia.EA;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;

namespace Arcadia.Handlers;

public class TheaterHandler
{
    private NetworkStream _network = null!;
    private string _clientEndpoint = null!;

    private readonly ILogger<TheaterHandler> _logger;
    private readonly SharedCounters _sharedCounters;
    private readonly SharedCache _sharedCache;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private readonly Dictionary<string, object> _sessionCache = new();

    private readonly string _serverIp = "192.168.0.164"; 
    private readonly int _serverPort = 1003;

    public TheaterHandler(ILogger<TheaterHandler> logger, SharedCounters sharedCounters, SharedCache sharedCache)
    {
        _logger = logger;
        _sharedCounters = sharedCounters;
        _sharedCache = sharedCache;

        _handlers = new Dictionary<string, Func<Packet, Task>>
        {
            ["CONN"] = HandleCONN,
            ["USER"] = HandleUSER,
            ["CGAM"] = HandleCGAM,
            ["ECNL"] = HandleECNL,
            ["EGAM"] = HandleEGAM,
            ["EGRS"] = HandleEGRS,
            ["PENT"] = HandlePENT,
            ["GDAT"] = HandleGDAT
        };
    }

    public async Task HandleClientConnection(NetworkStream network, string clientEndpoint)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;

        while (_network.CanRead)
        {
            int read;
            byte[] readBuffer = new byte[1514];

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

            _logger.LogDebug("Type: {type}", type);
            _logger.LogTrace("Data: {data}", Encoding.ASCII.GetString(readBuffer[..read]));

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

        var packet = new Packet("CONN", 0x00000000, response);
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

        var packet = new Packet("USER", 0x00000000, response);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);
    }

    // CreateGame
    private async Task HandleCGAM(Packet request)
    {
        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["MAX-PLAYERS"] = request.DataDict["MAX-PLAYERS"],
            ["EKEY"] = "",
            ["UGID"] = request.DataDict["UGID"],
            ["JOIN"] = request.DataDict["JOIN"],
            ["SECRET"] = "",
            ["LID"] = 255,
            ["J"] = request.DataDict["JOIN"],
            ["GID"] = _sharedCounters.GetNextGameId()
        };

        var packet = new Packet("CGAM", 0x00000000, response);
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

        var packet = new Packet("ECNL", 0x00000000, response);
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

        var packet = new Packet("EGAM", 0x00000000, response);
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

        var packet = new Packet("EGRS", 0x00000000, serverInfo);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);

        await SendEGEG(request);
    }

    private async Task HandleGDAT(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["JP"] = 1,
            ["B-U-location"] = "nrt",
            ["HN"] = "beach.server.p",
            ["B-U-level"] = "levels/coral_sea",
            ["N"] = "nrtps3313601",
            ["I"] = _serverIp,
            ["J"] = 0,
            ["HU"] = 201104017,
            ["B-U-Time"] = "T%3a0.00 S%3a 6.65 L%3a 0.00",
            ["V"] = "1.0",
            ["B-U-gamemode"] = "CONQUEST",
            ["B-U-trial"] = "RETAIL",
            ["P"] = "38681",
            ["B-U-balance"] = "NORMAL",
            ["B-U-hash"] = "2AC3F219-3614-F46A-843B-A02E03E849E1",
            ["B-numObservers"] = 0,
            ["TYPE"] = "G",
            ["LID"] = request.DataDict["LID"],
            ["B-U-Frames"] = "T%3a 205 B%3a 0",
            ["B-version"] = "RETAIL421378",
            ["QP"] = 0,
            ["MP"] = 24,
            ["B-U-type"] = "RANKED",
            ["B-U-playgroup"] = "YES",
            ["B-U-public"] = "YES",
            ["GID"] = request.DataDict["GID"],
            ["PL"] = "PC",
            ["B-U-elo"] = 1000,
            ["B-maxObservers"] = 0,
            ["PW"] = 0,
            ["TID"] = request.DataDict["TID"],
            ["B-U-coralsea"] = "YES",
            ["AP"] = 0
        };

        var packet = new Packet("GDAT", 0x00000000, serverInfo);
        var data = await packet.Serialize();
        await _network.WriteAsync(data);

        await SendGDET(request);
    }

    private async Task SendGDET(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["LID"] = request.DataDict["LID"],
            ["UGID"] = Guid.NewGuid().ToString(),
            ["GID"] = request.DataDict["GID"],
            ["TID"] = request.DataDict["TID"]
        };

        for (var i = 0; i < 24; i++)
        {
            serverInfo.Add($"D-pdat{i}", "|0|0|0|0");
        }

        var packet = new Packet("GDET", 0x00000000, serverInfo);
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

        var packet = new Packet("PENT", 0x00000000, serverInfo);
        var data = await packet.Serialize();

        await _network.WriteAsync(data);
    }

    private async Task SendEGEG(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["PL"] = "ps3",
            ["TICKET"] = _sharedCounters.GetNextTicket(),
            ["PID"] = _sharedCounters.GetNextPid(),
            ["HUID"] = "201104017",
            ["EKEY"] = "",
            ["UGID"] = Guid.NewGuid().ToString(),

            ["INT-IP"] = _serverIp,
            ["INT-PORT"] = _serverPort,

            ["I"] = _serverIp,
            ["P"] = _serverPort,

            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"]
        };

        var packet = new Packet("EGEG", 0x00000000, serverInfo);
        var data = await packet.Serialize();

        _logger.LogTrace("Sending EGEG to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }

    private async Task SendEGRQ()
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["R-INT-PORT"] = _serverPort,
            ["R-INT-IP"] = _serverIp,
            ["PORT"] = _serverPort,
            ["IP"] = _serverIp,
            ["NAME"] = "arcadia-ps3",
            ["PTYPE"] = "P",
            ["TICKET"] = "-479505973",
            ["PID"] = 1,
            ["PID"] = 1,
            ["UID"] = 1000000000000,
            ["LID"] = 255,
            ["GID"] = 801000
        };

        var packet = new Packet("EGRQ", 0x00000000, serverInfo);
        var data = await packet.Serialize();

        _logger.LogTrace("Sending EGRQ to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }
}