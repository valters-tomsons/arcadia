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
        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["MAX-PLAYERS"] = request.DataDict["MAX-PLAYERS"],
            ["EKEY"] = "",
            ["UGID"] = Guid.NewGuid().ToString(),
            ["JOIN"] = request.DataDict["JOIN"],
            ["SECRET"] = "",
            ["LID"] = 255,
            ["J"] = request.DataDict["JOIN"],
            ["GID"] = _sharedCounters.GetNextGameId()
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
        var serverInfo = new Dictionary<string, object>
        {
            ["JP"] = 1,
            ["B-U-location"] = "nrt",
            ["HN"] = "beach.server.p",
            ["B-U-level"] = "levels/coral_sea",
            ["N"] = "nrtps3313601",
            ["I"] = _arcadiaSettings.Value.GameServerAddress,
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
            ["B-U-Frames"] = "T%3a 300 B%3a 0",
            ["B-version"] = "RETAIL421378",
            ["QP"] = 0,
            ["MP"] = 24,
            ["B-U-type"] = "RANKED",
            ["B-U-playgroup"] = "YES",
            ["B-U-public"] = "YES",
            ["GID"] = request.DataDict["GID"],
            ["PL"] = "PS3",
            ["B-U-elo"] = 1520,
            ["B-maxObservers"] = 0,
            ["PW"] = 0,
            ["TID"] = request.DataDict["TID"],
            ["B-U-coralsea"] = "YES",
            ["AP"] = 5
        };

        var packet = new Packet("GDAT", TheaterTransmissionType.OkResponse, 0, serverInfo);
        var data = await packet.Serialize();
        await _network.WriteAsync(data);

        await SendGDET(request);
    }

    private async Task SendGDET(Packet request)
    {
        _sessionCache["UGID"] = Guid.NewGuid().ToString();

        var serverInfo = new Dictionary<string, object>
        {
            ["LID"] = request.DataDict["LID"],
            ["UGID"] = _sessionCache["UGID"],
            ["GID"] = request.DataDict["GID"],
            ["TID"] = request.DataDict["TID"]
        };

        for (var i = 0; i < 24; i++)
        {
            serverInfo.Add($"D-pdat{i}", "|0|0|0|0");
        }

        var packet = new Packet("GDET", TheaterTransmissionType.OkResponse, 0, serverInfo);
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