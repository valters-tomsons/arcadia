using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace Arcadia.EA;

public class EAConnection
{
    public string ClientEndpoint { get; private set; } = string.Empty;
    public Stream? NetworkStream { get; private set; }

    private readonly ILogger<EAConnection> _logger;

    public EAConnection(ILogger<EAConnection> logger)
    {
        _logger = logger;
    }

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

    public async IAsyncEnumerable<Packet> StartConnection([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (NetworkStream?.CanRead == true || !ct.IsCancellationRequested)
        {
            int read;
            byte[] readBuffer = new byte[8096];

            try
            {
                read = await NetworkStream!.ReadAsync(readBuffer.AsMemory(), ct);
            }
            catch(Exception e)
            {
                _logger.LogDebug(e, "Failed to read client stream, endpoint: {endpoint}", ClientEndpoint);
                break;
            }

            if (read == 0)
            {
                continue;
            }

            var packet = new Packet(readBuffer[..read]);
            yield return packet;

            var type = packet.Type;

            _logger.LogDebug("Incoming Type: {type}", type);
            _logger.LogTrace("data:{data}", Encoding.ASCII.GetString(readBuffer[..read]));
        }

        _logger.LogInformation("Connection has been closed: {endpoint}", ClientEndpoint);
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

    public async Task<bool> SendBinary(byte[] buffer)
    {
        if (NetworkStream is null || !NetworkStream.CanWrite)
        {
            _logger.LogDebug("Tried writing to disconnected endpoint: {endpoint}!", ClientEndpoint);
            return false;
        }

        try
        {
            await NetworkStream.WriteAsync(buffer);
            _logger.LogTrace("data sent:{data}", Encoding.ASCII.GetString(buffer));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed writing to endpoint: {endpoint}!", ClientEndpoint);
            return false;
        }
    }
}