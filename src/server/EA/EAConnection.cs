using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Arcadia.EA.Constants;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace Arcadia.EA;

public interface IEAConnection : IAsyncDisposable
{
    string ClientEndpoint { get; }
    Stream? NetworkStream { get; }
    string NetworkAddress { get; }
    string ServerAddress { get; }

    void Initialize(Stream network, string clientEndpoint, string serverEndpoint);
    void Terminate();

    IAsyncEnumerable<Packet> StartConnection(ILogger logger, CancellationToken ct = default);
    Task<bool> SendPacket(Packet packet, CancellationToken ct = default);
}

public sealed class EAConnection : IEAConnection
{
    public string ClientEndpoint { get; private set; } = string.Empty;
    public string NetworkAddress => ClientEndpoint.Split(':')[0];
    public string ServerAddress => _serverAddress;
    public Stream? NetworkStream { get; private set; }

    private const int ReadBufferSize = 8192;
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    private ILogger? _logger;
    private readonly byte[] _readBufferArray = _bufferPool.Rent(ReadBufferSize);
    private readonly MemoryStream _multiPacketBuffer = new(ReadBufferSize);

    private string _serverAddress = string.Empty;
    private bool _terminated = false;

    public void Initialize(Stream network, string clientEndpoint, string serverEndpoint)
    {
        if (NetworkStream is not null)
        {
            throw new InvalidOperationException("Tried to initialize an already initialized connection!");
        }

        ClientEndpoint = clientEndpoint;
        NetworkStream = network;
        _serverAddress = serverEndpoint.Split(':')[0];
    }

    public async IAsyncEnumerable<Packet> StartConnection(ILogger parentLogger, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger = parentLogger;
        if (NetworkStream is null) throw new InvalidOperationException("Connection must be initialized before starting");

        var readBuffer = _readBufferArray.AsMemory();

        uint? currentMultiPacketId = null;
        uint requestedMultiPacketSize = 0;

        while (NetworkStream.CanRead == true && !ct.IsCancellationRequested && !_terminated)
        {
            int read;

            try
            {
                read = await NetworkStream.ReadAsync(readBuffer, ct);
            }
            catch (ObjectDisposedException) { break; }
            catch (TlsNoCloseNotifyException) { break; }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Failed to read client stream, endpoint: {endpoint}", ClientEndpoint);
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
                    var encodedPart = packet["data"].Replace("%3d", "=");
                    var partPayload = Convert.FromBase64String(encodedPart);
                    var size = uint.Parse(packet["size"]);

                    if (currentMultiPacketId != packet.Id)
                    {
                        currentMultiPacketId = packet.Id;
                        requestedMultiPacketSize = size;
                    }

                    if (requestedMultiPacketSize != size) throw new Exception($"Requested packet-size changed between requests! Initial size: {requestedMultiPacketSize}, newSize: {size}");

                    await _multiPacketBuffer.WriteAsync(partPayload, ct);

                    if (_multiPacketBuffer.Length == requestedMultiPacketSize)
                    {
                        var bufferData = _multiPacketBuffer.ToArray();
                        currentMultiPacketId = null;

                        _multiPacketBuffer.SetLength(0);
                        _multiPacketBuffer.Position = 0;

                        var combinedData = Utils.ParseFeslPacketToDict(bufferData);
                        var combinedPacket = new Packet(packet.Type, packet.TransmissionType, packet.Id, combinedData, size);

                        _logger.LogTrace("'{type}' incoming multi-packet, combined:{data}", combinedPacket.Type, Encoding.ASCII.GetString(bufferData));

                        yield return combinedPacket;
                    }
                    else if (_multiPacketBuffer.Length > requestedMultiPacketSize) throw new Exception($"Requested packet-size changed between requests! Initial size: {requestedMultiPacketSize}, tried to write: {requestedMultiPacketSize}");

                    continue;
                }
                else
                {
                    currentMultiPacketId = null;
                    _logger.LogTrace("'{type}' incoming:{data}", packet.Type, Encoding.ASCII.GetString(packet.Data ?? []));
                }

                yield return packet;
            }
        }

        _logger.LogInformation("Connection has been closed: {endpoint}", ClientEndpoint);
    }

    public void Terminate()
    {
        _terminated = true;
    }

    public async Task<bool> SendPacket(Packet packet, CancellationToken ct = default)
    {
        if (NetworkStream is null || !NetworkStream.CanWrite)
        {
            return false;
        }

        var packetBuffer = await packet.Serialize();
        return await SendBinary(packetBuffer, ct);
    }

    private async Task<bool> SendBinary(byte[] buffer, CancellationToken ct)
    {
        if (NetworkStream is null || !NetworkStream.CanWrite)
        {
            _logger?.LogDebug("Tried writing to disconnected endpoint: {endpoint}!", ClientEndpoint);
            return false;
        }

        try
        {
            NetworkStream.Write(buffer);
            await NetworkStream.FlushAsync(ct);
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

    public async ValueTask DisposeAsync()
    {
        _bufferPool.Return(_readBufferArray, clearArray: true);
        await _multiPacketBuffer.DisposeAsync();
    }
}