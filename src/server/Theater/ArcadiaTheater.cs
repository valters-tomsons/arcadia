using Arcadia.EA;
using Org.BouncyCastle.Tls;

namespace Arcadia.Theater;

public class ArcadiaTheater
{
    private readonly TlsServerProtocol _network;
    private readonly string _clientEndpoint;

    private uint _plasmaTicketId;

    public ArcadiaTheater(TlsServerProtocol network, string clientEndpoint)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;
    }

    public async Task HandleClientConnection()
    {
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
                Console.WriteLine($"[theater] Connection has been closed with {_clientEndpoint}");
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

            Console.WriteLine($"Type: {reqPacket.Type}");
            Console.WriteLine($"TXN: {reqTxn}");

            Console.WriteLine($"Unknown packet type: {reqPacket.Type} TXN: {reqTxn}");
            Interlocked.Increment(ref _plasmaTicketId);
        }
    }
}