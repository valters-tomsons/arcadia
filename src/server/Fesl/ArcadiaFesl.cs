using System.Globalization;
using System.Text;
using Arcadia.Fesl.Structures;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;

namespace Arcadia.Fesl;

public class ArcadiaFesl
{
    private readonly TlsServerProtocol _network;
    private readonly string _clientEndpoint;
    private readonly string _serverAddress;
    private uint _ticketCounter;

    private static readonly SecureRandom Random = new();

    public ArcadiaFesl(TlsServerProtocol network, string clientEndpoint, string serverAddress)
    {
        _network = network;
        _clientEndpoint = clientEndpoint;
        _serverAddress = serverAddress;
    }

    public void HandleClientConnection()
    {
        var readBuffer = new byte[1514];
        while (_network.IsConnected)
        {
            int read;

            try
            {
                read = _network.ReadApplicationData(readBuffer, 0, readBuffer.Length);
            }
            catch
            {
                Console.WriteLine($"Connection has been closed with {_clientEndpoint}");
                break;
            }

            if (read == 0)
            {
                continue;
            }

            var reqPacket = new FeslPacket(readBuffer[..read]);
            var reqTxn = (string)reqPacket.DataDict["TXN"];

            Console.WriteLine($"Type: {reqPacket.Type}");
            Console.WriteLine($"TXN: {reqTxn}");

            if (reqPacket.Type == "fsys" && reqTxn == "Hello")
            {
                var currentTime = DateTime.UtcNow.ToString("MMM-dd-yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

                var serverHelloData = new Dictionary<string, object>
                {
                    { "domainPartition.domain", "ps3" },
                    { "messengerIp", _serverAddress },
                    { "messengerPort", 0 },
                    { "domainPartition.subDomain", "BEACH" },
                    { "TXN", "Hello" },
                    { "activityTimeoutSecs", 0 },
                    { "curTime", currentTime},
                    {"theaterIp", _serverAddress },
                    {"theaterPort", 18236 }
                };

                var reqPlasmaId = Interlocked.Increment(ref _ticketCounter);

                var dataSb = Utils.DataDictToPacketString(serverHelloData);
                var helloSumBytes = GenerateChecksum(dataSb.ToString(), reqPacket.Id, reqPlasmaId);

                var helloResponse = new List<byte>();

                helloResponse.AddRange(Encoding.ASCII.GetBytes("fsys"));
                helloResponse.AddRange(helloSumBytes);
                helloResponse.AddRange(Encoding.ASCII.GetBytes(dataSb.ToString()));

                Console.WriteLine(Encoding.ASCII.GetString(helloResponse.ToArray()));

                _network.WriteApplicationData(helloResponse.ToArray(), 0, helloResponse.Count);

                var memCheckData = new Dictionary<string, object>
                {
                    { "TXN", "MemCheck" },
                    { "memcheck.[]", "0" },
                    { "type", "0" },
                    { "salt", GenerateSalt() },
                };

                var memCheckDataSb = Utils.DataDictToPacketString(memCheckData);
                var memSumBytes = GenerateChecksum(memCheckDataSb.ToString(), 0x80000000, reqPlasmaId);

                var memRequest = new List<byte>();
                memRequest.AddRange(Encoding.ASCII.GetBytes("fsys"));
                memRequest.AddRange(memSumBytes);
                memRequest.AddRange(Encoding.ASCII.GetBytes(memCheckDataSb.ToString()));

                Console.WriteLine(Encoding.ASCII.GetString(memRequest.ToArray()));
            }
        }
    }

    private static string GenerateSalt()
    {
        var seed = Random.NextInt64(900000000, 999999999);
        return seed.ToString();
    }

    private static byte[] GeneratePacketLength(string packetData)
    {
        var dataBytes = Encoding.ASCII.GetBytes(packetData);

        int length = dataBytes.Length + 12;
        byte[] bytes = BitConverter.GetBytes(length);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }

    private static byte[] GeneratePacketId(uint packetId)
    {
        byte[] bytes = BitConverter.GetBytes(packetId);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }

    public static byte[] GenerateChecksum(string data, uint packetId, uint plasmaPacketId)
    {
        byte[] packetIdBytes = GeneratePacketId(packetId + plasmaPacketId);
        byte[] packetLengthBytes = GeneratePacketLength(data);

        return packetIdBytes.Concat(packetLengthBytes).ToArray();
    }
}