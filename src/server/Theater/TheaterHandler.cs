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

    public TheaterHandler(ILogger<TheaterHandler> logger)
    {
        _logger = logger;
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

            if (type == "CONN")
            {
                await HandleCONN(packet);
            }
            else if (type == "USER")
            {
                await HandleUSER(packet);
            }
            else
            {
                _logger.LogWarning("Unknown packet type: {type}", type);
            }
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
}