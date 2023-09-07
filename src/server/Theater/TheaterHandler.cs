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
            ["TID"] = tid,
            ["PROT"] = prot,
            ["TIME"] = 0,
            ["activityTimeoutSecs"] = 240,
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
            ["TID"] = request.DataDict["TID"],
            ["NAME"] = "faith"
        };

        var packet = new Packet("USER", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);
    }

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
            ["SECRET"] = "4l94N6Y0A3Il3+kb55pVfK6xRjc+Z6sGNuztPeNGwN5CMwC7ZlE/lwel07yciyZ5y3bav7whbzHugPm11NfuBg%3d%3d",
            ["LID"] = 1,
            ["J"] = request.DataDict["JOIN"],
            ["GID"] = 1
        };

        var packet = new Packet("CGAM", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);
    }

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

    private async Task HandleEGAM(Packet request)
    {
        var response = new Dictionary<string, object>
        {
            ["TID"] = request.DataDict["TID"],
            ["LID"] = request.DataDict["LID"],
            ["GID"] = request.DataDict["GID"],
        };

        var packet = new Packet("EGAM", 0x00000000, response);
        var data = await packet.ToPacket(0);

        await _network.WriteAsync(data);
    }
}