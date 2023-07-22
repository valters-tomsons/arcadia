using System.Net.Sockets;
using System.Text;
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

            _logger.LogInformation("Received {read} bytes", read);
            _logger.LogInformation("Data: {data}", Encoding.ASCII.GetString(readBuffer[..read]));
        }
    }
}