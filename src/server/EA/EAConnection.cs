using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace Arcadia.EA;

public interface IEAConnection
{
    string ClientEndpoint { get; }
    Stream? NetworkStream { get; }
    string NetworkAddress { get; }
    string ServerAddress { get; }

    void InitializeInsecure(Stream network, string clientEndpoint, string serverEndpoint);
    void InitializeSecure(TlsServerProtocol network, string clientEndpoint, string serverEndpoint);

    IAsyncEnumerable<Packet> StartConnection(ILogger logger, CancellationToken ct = default);

    Task<bool> SendPacket(Packet packet);
}

public class EAConnection : IEAConnection
{
    private ILogger? _logger;

    public string ClientEndpoint { get; private set; } = string.Empty;
    public Stream? NetworkStream { get; private set; }

    public string NetworkAddress => ClientEndpoint.Split(':')[0];

    private string _serverAddress = string.Empty;
    public string ServerAddress => _serverAddress;

    public void InitializeInsecure(Stream network, string clientEndpoint, string serverEndpoint)
    {
        if (NetworkStream is not null)
        {
            throw new InvalidOperationException("Tried to initialize an already initialized connection!");
        }

        ClientEndpoint = clientEndpoint;
        NetworkStream = network;
        _serverAddress = serverEndpoint.Split(':')[0];
    }

    public void InitializeSecure(TlsServerProtocol network, string clientEndpoint, string serverEndpoint)
    {
        if (NetworkStream is not null)
        {
            throw new InvalidOperationException("Tried to initialize an already initialized connection!");
        }

        ClientEndpoint = clientEndpoint;
        NetworkStream = network.Stream;
        _serverAddress = serverEndpoint.Split(':')[0];
    }

    public async IAsyncEnumerable<Packet> StartConnection(ILogger logger, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger = logger;

        if (NetworkStream is null) throw new InvalidOperationException("Connection must be initialized before starting");

        var readBuffer = new byte[8096];
        while (NetworkStream.CanRead == true || !ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await NetworkStream!.ReadAsync(readBuffer.AsMemory(), ct);
            }
            catch(Exception e)
            {
                logger.LogDebug(e, "Failed to read client stream, endpoint: {endpoint}", ClientEndpoint);
                break;
            }

            if (read == 0)
            {
                continue;
            }

            var incomingData = readBuffer[..read];
            var dataProcessed = 0;

            while (dataProcessed < read)
            {
                var buffer = incomingData[dataProcessed..];
                if (buffer.Length <= 12)
                {
                    _logger.LogCritical("Unexpected incoming message length");
                    throw new NotImplementedException();
                }

                var packet = new Packet(buffer);

                if (packet.Length == 0)
                {
                    _logger.LogCritical("Unexpected packet length");
                    throw new NotImplementedException();
                }

                logger.LogTrace("'{type}' incoming:{data}", packet.Type, Encoding.ASCII.GetString(packet.Data ?? []));
                dataProcessed += (int)packet.Length;
                yield return packet;
            }
        }

        logger.LogInformation("Connection has been closed: {endpoint}", ClientEndpoint);
    }

    public async Task<bool> SendPacket(Packet packet)
    {
        if (NetworkStream is null || !NetworkStream.CanWrite)
        {
            return false;
        }

        var packetBuffer = await packet.Serialize();
        return await SendBinary(packetBuffer);
    }

    private async Task<bool> SendBinary(byte[] buffer)
    {
        if (NetworkStream is null || !NetworkStream.CanWrite)
        {
            _logger?.LogDebug("Tried writing to disconnected endpoint: {endpoint}!", ClientEndpoint);
            return false;
        }

        try
        {
            NetworkStream.Write(buffer);
            await NetworkStream.FlushAsync();
            _logger?.LogTrace("data sent:{data}", Encoding.ASCII.GetString(buffer));
            return true;
        }
        catch (Exception e)
        {
            _logger?.LogDebug(e, "Failed writing to endpoint: {endpoint}!", ClientEndpoint);
            return false;
        }
    }
}