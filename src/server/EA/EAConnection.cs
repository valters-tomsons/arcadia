using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace Arcadia.EA;

public interface IEAConnection
{
    string ClientEndpoint { get; }
    Stream? NetworkStream { get; }

    void InitializeInsecure(Stream network, string endpoint);
    void InitializeSecure(TlsServerProtocol network, string endpoint);

    IAsyncEnumerable<Packet> StartConnection(ILogger logger, CancellationToken ct = default);

    Task<bool> SendPacket(Packet packet);
}

public class EAConnection : IEAConnection
{
    private ILogger? _logger;

    public string ClientEndpoint { get; private set; } = string.Empty;
    public Stream? NetworkStream { get; private set; }

    public void InitializeInsecure(Stream network, string endpoint)
    {
        if (ClientEndpoint is null && NetworkStream is null)
        {
            throw new InvalidOperationException("Tried to initialize an already initialized connection!");
        }

        ClientEndpoint = endpoint;
        NetworkStream = network;
    }

    public void InitializeSecure(TlsServerProtocol network, string endpoint)
    {
        if (ClientEndpoint is null && NetworkStream is null)
        {
            throw new InvalidOperationException("Tried to initialize an already initialized connection!");
        }

        ClientEndpoint = endpoint;
        NetworkStream = network.Stream;
    }

    public async IAsyncEnumerable<Packet> StartConnection(ILogger logger, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger = logger;

        int read;
        byte[] readBuffer = new byte[8096];

        while (NetworkStream?.CanRead == true || !ct.IsCancellationRequested)
        {
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

            logger.LogTrace("Incoming packet data:{data}", Encoding.ASCII.GetString(incomingData));

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

                logger.LogTrace("'{type}' processed:{data}", packet.Type, Encoding.ASCII.GetString(packet.Data ?? []));
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
            await NetworkStream.WriteAsync(buffer);
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