using System.Net.Sockets;
using System.Text;
using Arcadia.EA;
using Microsoft.Extensions.Logging;

namespace Arcadia.Theater;

public class TheaterHandler
{
    private NetworkStream _network = null!;
    private string _clientEndpoint = null!;

    private readonly ILogger<TheaterHandler> _logger;

    private readonly Dictionary<string, Func<Packet, Task>> _handlers;

    private readonly Dictionary<string, object> _sessionCache = new();

    private readonly string _serverIp = "192.168.0.164"; 
    private readonly int _serverPort = 1003;

    public TheaterHandler(ILogger<TheaterHandler> logger)
    {
        _logger = logger;

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
        var prot = request.DataDict["PROT"];

        _logger.LogInformation("CONN: {tid} {prot}", tid, prot);

        var response = new Dictionary<string, object>
        {
            ["TIME"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["TID"] = tid,
            ["activityTimeoutSecs"] = 240,
            ["PROT"] = prot
        };

        var packet = new Packet("CONN", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);
    }

    private async Task HandleUSER(Packet request)
    {
        var lkey = request.DataDict["LKEY"];

        _logger.LogInformation("USER: {lkey}", lkey);

        // !TODO: compare with fesl sessions

        var response = new Dictionary<string, object>
        {
            ["NAME"] = "arcadia-ps3",
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("USER", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);
    }

    // CreateGame
    private async Task HandleCGAM(Packet request)
    {
        // !TODO: set gid to a valid game id
        // !TODO: figure out ekey and secret

        _sessionCache["UGID"] = request.DataDict["UGID"];
        _sessionCache["EKEY"] = "Esnq0vjAFedKXcUZKtpOWw%3d%3d";

        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["MAX-PLAYERS"] = request.DataDict["MAX-PLAYERS"],
            ["EKEY"] = _sessionCache["EKEY"],
            ["UGID"] = request.DataDict["UGID"],
            ["JOIN"] = request.DataDict["JOIN"],
            ["SECRET"] = "ivR7O1eYEzUQLcwnt8/dsGKE0T1W81JZ8BhkMcEpRdiYwV/oy9gMyTp5DpckPOl4GK1tmraNiN3ugPm11NfuBg%3d%3d",
            ["LID"] = 255,
            ["J"] = request.DataDict["JOIN"],
            ["GID"] = 801000
        };

        var packet = new Packet("CGAM", 0x00000000, response);
        var data = await packet.ToPacket(0);

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
            ["LID"] = 10,
            ["GID"] = 1
        };

        var packet = new Packet("ECNL", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);
    }

    // EnterGameRequest
    private async Task HandleEGAM(Packet request)
    {
        _sessionCache["R-INT-PORT"] = request.DataDict["R-INT-PORT"];
        _sessionCache["R-INT-IP"] = request.DataDict["R-INT-IP"];
        _sessionCache["PORT"] = request.DataDict["PORT"];
        _sessionCache["TID"] = request.DataDict["TID"];

        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
        };

        var packet = new Packet("EGAM", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);

        // await SendEGRQ();
        await SendEGEG();
    }

    private async Task HandleEGRS(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"]
        };

        var packet = new Packet("EGRS", 0x00000000, serverInfo);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);

        await SendEGEG();
    }

    private async Task HandlePENT(Packet request)
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["PID"] = request.DataDict["PID"],
        };

        var packet = new Packet("PENT", 0x00000000, serverInfo);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);
    }

    private async Task HandleGDAT(Packet request)
    {
        _sessionCache["TID"] = request.DataDict["TID"];

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
            ["P"] = 38681,
            ["B-U-balance"] = "NORMAL",
            ["B-U-hash"] = "8FF089DA-0DE7-0470-EF0F-0D4C905B7DC5",
            ["B-numObservers"] = 0,
            ["TYPE"] = "G",
            ["LID"] = 255,
            ["B-U-Frames"] = "T%3a 205 B%3a 0",
            ["B-version"] = "RETAIL421378",
            ["QP"] = 0,
            ["MP"] = 24,
            ["B-U-type"] = "RANKED",
            ["B-U-playgroup"] = "YES",
            ["B-U-public"] = "YES",
            ["GID"] = 801000,
            ["PL"] = "PC",
            ["B-U-elo"] = 1000,
            ["B-maxObservers"] = 0,
            ["PW"] = 0,
            ["TID"] = request.DataDict["TID"],
            ["B-U-coralsea"] = "YES",
            ["AP"] = 0
        };

        var packet = new Packet("GDAT", 0x00000000, serverInfo);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);

        await SendGDET();
    }

    private async Task SendGDET()
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["LID"] = 255,
            ["UGID"] = "b7b78dc5-99a8-42cb-b0e7-81184929f0bb",
            ["GID"] = 801000,
            ["TID"] = _sessionCache["TID"]
        };

        for (var i = 0; i < 24; i++)
        {
            serverInfo.Add($"D-pdat{i}", "|0|0|0|0");
        }

        var packet = new Packet("GDET", 0x00000000, serverInfo);
        var data = await packet.ToPacket(0);

        _logger.LogTrace("Sending GDET to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }

    private async Task SendEGEG()
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["PL"] = "ps3",
            ["TICKET"] = "-479505973",
            ["PID"] = 1,
            ["HUID"] = "",
            ["EKEY"] = "",
            ["UGID"] = "",

            ["INT-IP"] = _serverIp,
            ["INT-PORT"] = _serverPort,

            ["I"] = _serverIp,
            ["P"] = _serverPort,

            ["LID"] = 255,
            ["GID"] = 801000
        };

        var packet = new Packet("EGEG", 0x00000000, serverInfo);
        var data = await packet.ToPacket(0);

        _logger.LogTrace("Sending EGEG to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }

    private async Task SendEGRQ()
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["R-INT-PORT"] = _sessionCache["R-INT-PORT"],
            ["R-INT-IP"] = _sessionCache["R-INT-IP"],
            ["PORT"] = _sessionCache["PORT"],
            ["NAME"] = "arcadia-ps3",
            ["PTYPE"] = "P",
            ["TICKET"] = "-479505973",
            ["PID"] = 1,
            ["PID"] = 1,
            ["UID"] = 1000000000000,
            ["IP"] = "192.168.0.164",
            ["LID"] = 255,
            ["GID"] = 801000
        };

        var packet = new Packet("EGRQ", 0x00000000, serverInfo);
        var data = await packet.ToPacket(0);

        _logger.LogTrace("Sending EGRQ to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }
}