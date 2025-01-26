using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Arcadia.EA.Constants;
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
    void Terminate();

    IAsyncEnumerable<Packet> StartConnection(ILogger logger, CancellationToken ct = default);

    Task<bool> SendPacket(Packet packet);
}

public class EAConnection : IEAConnection
{
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    private const int ReadBufferSize = 8192;
    private const int MultiPacketBufferSize = 24000;

    private ILogger? _logger;

    public string ClientEndpoint { get; private set; } = string.Empty;
    public Stream? NetworkStream { get; private set; }

    public string NetworkAddress => ClientEndpoint.Split(':')[0];
    public string ServerAddress => _serverAddress;

    private string _serverAddress = string.Empty;
    private bool _terminated = false;

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

        var bufferArray = _bufferPool.Rent(ReadBufferSize);
        var readBuffer = bufferArray.AsMemory();

        MemoryStream? multiPacketBuffer = null;
        uint? currentMultiPacketId = null;
        uint currentMultiPacketSize = 0;
        int currentMultiPacketReceivedSize = 0;

        try
        {
            while (NetworkStream.CanRead == true && !ct.IsCancellationRequested && !_terminated)
            {
                int read;

                try
                {
                    read = await NetworkStream!.ReadAsync(readBuffer, ct);
                }
                catch (ObjectDisposedException) { break; }
                catch (TlsNoCloseNotifyException) { break; }
                catch (Exception e)
                {
                    logger.LogDebug(e, "Failed to read client stream, endpoint: {endpoint}", ClientEndpoint);
                    break;
                }

                if (read == 0)
                {
                    continue;
                }

                if (read > readBuffer.Length)
                {
                    _logger.LogCritical("Client sent a packet exceeding read buffer size!");
                    break;
                }

                var dataProcessed = 0;
                while (dataProcessed < read)
                {
                    var endRange = dataProcessed + read;
                    var buffer = readBuffer[dataProcessed..endRange];
                    if (buffer.Length <= 12)
                    {
                        _logger.LogCritical("Unexpected incoming message length");
                        throw new NotImplementedException();
                    }

                    var packet = new Packet(buffer.ToArray());
                    dataProcessed += (int)packet.Length;

                    if (packet.Length == 0)
                    {
                        _logger.LogCritical("Unexpected packet length");
                        throw new NotImplementedException();
                    }

                    if (packet.TransmissionType == FeslTransmissionType.MultiPacketResponse || packet.TransmissionType == FeslTransmissionType.MultiPacketRequest)
                    {
                        multiPacketBuffer ??= new(MultiPacketBufferSize);

                        var encodedPart = packet["data"].Replace("%3d", "=");
                        var partPayload = Convert.FromBase64String(encodedPart);
                        var size = uint.Parse(packet["size"]);

                        if (currentMultiPacketId != packet.Id)
                        {
                            currentMultiPacketId = packet.Id;
                            currentMultiPacketSize = size;
                            currentMultiPacketReceivedSize = 0;

                            if (size > MultiPacketBufferSize) throw new Exception($"Requested multi-packet buffer-size too large: {size}");
                        }

                        if (currentMultiPacketSize != size) throw new Exception($"Requested packet-size changed between requests! Initial size: {currentMultiPacketSize}, newSize: {size}");

                        await multiPacketBuffer.WriteAsync(partPayload, ct);
                        currentMultiPacketReceivedSize += encodedPart.Length;

                        if (currentMultiPacketReceivedSize == currentMultiPacketSize)
                        {
                            var bufferData = multiPacketBuffer.ToArray();
                            currentMultiPacketId = null;

                            var combinedData = Utils.ParseFeslPacketToDict(bufferData);
                            var combinedPacket = new Packet(packet.Type, packet.TransmissionType, packet.Id, combinedData, size);

                            logger.LogTrace("'{type}' incoming multi-packet, combined:{data}", combinedPacket.Type, Encoding.ASCII.GetString(bufferData));

                            yield return combinedPacket;
                        }
                        else if (currentMultiPacketReceivedSize > currentMultiPacketSize) throw new Exception($"Requested packet-size changed between requests! Initial size: {currentMultiPacketSize}, tried to write: {currentMultiPacketSize}");

                        continue;
                    }
                    else
                    {
                        currentMultiPacketId = null;
                        logger.LogTrace("'{type}' incoming:{data}", packet.Type, Encoding.ASCII.GetString(packet.Data ?? []));
                    }

                    yield return packet;
                }
            }
        }
        finally
        {
            if (multiPacketBuffer is not null)
            {
                await multiPacketBuffer.DisposeAsync();
            }

            _bufferPool.Return(bufferArray);
        }

        logger.LogInformation("Connection has been closed: {endpoint}", ClientEndpoint);
    }

    public void Terminate()
    {
        _terminated = true;
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
            _terminated = true;
            return false;
        }
    }
}