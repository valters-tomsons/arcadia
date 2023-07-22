using Arcadia.EA;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace Arcadia.Theater;

public class TheaterHandler
{
    private TlsServerProtocol _network = null!;
    private string _clientEndpoint = null!;

    private readonly ILogger<TheaterHandler> _logger;

    private uint _plasmaTicketId;

    public TheaterHandler(ILogger<TheaterHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleClientConnection(TlsServerProtocol network, string clientEndpoint)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;

        while (_network.IsConnected)
        {
            int read;
            byte[]? readBuffer;

            try
            {
                (read, readBuffer) = await Utils.ReadApplicationDataAsync(_network);
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

            var reqPacket = new Packet(readBuffer[..read]);
            var reqTxn = (string)reqPacket.DataDict["TXN"];

            if (reqPacket.Id != 0x80000000)
            {
                Interlocked.Increment(ref _plasmaTicketId);
            }

            _logger.LogInformation("Type: {type}", reqPacket.Type);
            _logger.LogInformation("TXN: {txn}", reqTxn);

            _logger.LogWarning("Unknown packet type: {type} TXN: {txn}", reqPacket.Type, reqTxn);

            Interlocked.Increment(ref _plasmaTicketId);
        }
    }
}