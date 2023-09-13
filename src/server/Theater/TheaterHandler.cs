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

    public TheaterHandler(ILogger<TheaterHandler> logger)
    {
        _logger = logger;

        _handlers = new Dictionary<string, Func<Packet, Task>>
        {
            ["CONN"] = HandleCONN,
            ["USER"] = HandleUSER,
            ["CGAM"] = HandleCGAM,
            ["ECNL"] = HandleECNL,
            ["EGAM"] = HandleEGAM
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
            ["NAME"] = "faith",
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

        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["MAX-PLAYERS"] = request.DataDict["MAX-PLAYERS"],
            ["EKEY"] = "AIBSgPFqRDg0TfdXW1zUGa4%3d",
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

        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
        };

        var packet = new Packet("EGAM", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);

        await Task.Delay(200);
        await SendEGRQ();
    }

    private async Task SendEGRQ()
    {
        var serverInfo = new Dictionary<string, object>
        {
            ["R-INT-PORT"] = _sessionCache["R-INT-PORT"],
            ["R-INT-IP"] = _sessionCache["R-INT-IP"],
            ["PORT"] = _sessionCache["PORT"],
            ["NAME"] = "faith",
            ["PTYPE"] = "P",
            ["TICKET"] = "-479505973",
            ["PID"] = 1,
            ["PID"] = 1,
            ["UID"] = 1000000000000,
            ["IP"] = "192.168.0.164",
            ["LID"] = 257,
            ["GID"] = 801000
        };

        var packet = new Packet("EGRQ", 0x00000000, serverInfo);
        var data = await packet.ToPacket(0);

        _logger.LogTrace("Sending EGRQ to client at {endpoint}", _clientEndpoint);
        await _network.WriteAsync(data);
    }
}