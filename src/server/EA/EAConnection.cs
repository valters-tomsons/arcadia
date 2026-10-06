using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Arcadia.EA.Constants;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace Arcadia.EA;

public interface IEAConnection : IAsyncDisposable
{
    string RemoteEndpoint { get; }
    Stream? NetworkStream { get; }
    string RemoteAddress { get; }
    string LocalAddress { get; }

    void Initialize(Stream network, string remoteEndpoint, string localEndpoint, CancellationToken ct);
    Task Terminate();

    IAsyncEnumerable<Packet> ReceiveAsync(ILogger logger);
    Task<bool> SendPacket(Packet packet);
}

public sealed class EAConnection : IEAConnection
{
    public string RemoteEndpoint { get; private set; } = string.Empty;
    public string RemoteAddress => RemoteEndpoint.Split(':')[0];
    public string LocalAddress => _serverAddress;
    public Stream? NetworkStream { get; private set; }

    private const int ReadBufferSize = 8192;
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    private ILogger? _logger;
    private readonly byte[] _readBufferArray = _bufferPool.Rent(ReadBufferSize);
    private readonly MemoryStream _multiPacketBuffer = new(ReadBufferSize);

    private string _serverAddress = null!;
    private CancellationTokenSource _cts = null!;

    public void Initialize(Stream network, string remoteEndpoint, string localEndpoint, CancellationToken ct)
    {
        if (NetworkStream is not null)
        {
            throw new InvalidOperationException("Tried to initialize an already initialized connection!");
        }

        RemoteEndpoint = remoteEndpoint;
        NetworkStream = network;

        _serverAddress = localEndpoint.Split(':')[0];
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    public async IAsyncEnumerable<Packet> ReceiveAsync(ILogger? parentLogger)
    {
        _logger = parentLogger;
        if (NetworkStream is null) throw new InvalidOperationException("Connection must be initialized before starting");

        var readBuffer = _readBufferArray.AsMemory();

        uint? currentMultiPacketId = null;
        uint requestedMultiPacketSize = 0;
        long bufferedMultiPacketSize = 0;

        // Bytes of an incomplete packet carried over from the previous read (TCP doesn't preserve packet boundaries).
        var buffered = 0;

        while (NetworkStream.CanRead == true && !_cts.IsCancellationRequested)
        {
            int read;

            try
            {
                read = await NetworkStream.ReadAtLeastAsync(readBuffer[buffered..], 1, throwOnEndOfStream: true, _cts.Token);
            }
            catch (ObjectDisposedException) { break; }
            catch (TaskCanceledException) { break; }
            catch (TlsNoCloseNotifyException) { break; }
            catch (EndOfStreamException) { break; }
            catch (Exception e)
            {
                _logger?.LogDebug(e, "Failed to read client stream, endpoint: {endpoint}", RemoteEndpoint);
                break;
            }

            if (read == 0)
            {
                continue;
            }

            buffered += read;

            var dataProcessed = 0;
            while (buffered - dataProcessed >= Packet.HEADER_SIZE && !_cts.IsCancellationRequested)
            {
                var packetLength = (int)BinaryPrimitives.ReadUInt32BigEndian(readBuffer.Span[(dataProcessed + 8)..]);
                if (packetLength <= Packet.HEADER_SIZE || packetLength > readBuffer.Length)
                {
                    _logger?.LogCritical("Unexpected packet length {length}, closing connection", packetLength);
                    yield break;
                }

                if (buffered - dataProcessed < packetLength)
                {
                    break; // rest of the packet hasn't arrived yet
                }

                var packet = new Packet(readBuffer[dataProcessed..(dataProcessed + packetLength)].ToArray());
                dataProcessed += packetLength;

                if (packet.TransmissionType == FeslTransmissionType.MultiPacketResponse || packet.TransmissionType == FeslTransmissionType.MultiPacketRequest)
                {
                    var encodedPart = packet["data"].Replace("%3d", "=");

                    var partPayload = Convert.FromBase64String(encodedPart);
                    var size = uint.Parse(packet["size"]);

                    _logger?.LogTrace("Multi-packet part received - ID: {id}, Declared Size: {size}, Part Size: {partSize}", packet.Id, size, encodedPart.Length);

                    if (currentMultiPacketId != packet.Id)
                    {
                        currentMultiPacketId = packet.Id;
                        requestedMultiPacketSize = size;

                        _multiPacketBuffer.SetLength(0);
                        _multiPacketBuffer.Position = 0;
                        bufferedMultiPacketSize = 0;
                    }

                    if (requestedMultiPacketSize != size) throw new Exception($"Requested packet-size changed between requests! Initial size: {requestedMultiPacketSize}, newSize: {size}");

                    await _multiPacketBuffer.WriteAsync(partPayload, _cts.Token);
                    bufferedMultiPacketSize += encodedPart.Length;

                    _logger?.LogTrace("Multi-packet part buffered - Length: {bufferLength}, Requested Size: {requestedSize}",
                        bufferedMultiPacketSize, requestedMultiPacketSize);

                    if (bufferedMultiPacketSize == requestedMultiPacketSize)
                    {
                        currentMultiPacketId = null;

                        var bufferData = _multiPacketBuffer.ToArray();
                        var combinedData = Utils.ParseFeslPacketToDict(bufferData);
                        var combinedPacket = new Packet(packet.Type, packet.TransmissionType, packet.Id, combinedData, size);

                        _logger?.LogTrace("'{type}' incoming multi-packet, combined:{data}", combinedPacket.Type, Encoding.ASCII.GetString(bufferData));
                        yield return combinedPacket;
                    }
                    else if (bufferedMultiPacketSize > requestedMultiPacketSize) throw new Exception($"Buffer overflow! Buffer contains {_multiPacketBuffer.Length} bytes but expected only {requestedMultiPacketSize}");

                    continue;
                }
                else
                {
                    currentMultiPacketId = null;
                    _logger?.LogTrace("'{type}' incoming:{data}", packet.Type, Encoding.ASCII.GetString(packet.Data ?? []));
                }

                yield return packet;
            }

            readBuffer[dataProcessed..buffered].CopyTo(readBuffer);
            buffered -= dataProcessed;
        }

        _logger?.LogTrace("Connection has been closed: {endpoint}", RemoteEndpoint);
    }

    public Task Terminate()
    {
        return _cts.CancelAsync();
    }

    public async Task<bool> SendPacket(Packet packet)
    {
        if (NetworkStream is null || !NetworkStream.CanWrite)
        {
            return false;
        }

        var packetBuffer = await packet.Serialize();
        
        if (packetBuffer.Length > 8064)
        {
            return await SendMultiPacket(packet, packetBuffer);
        }

        return await SendBinary(packetBuffer);
    }

    private async Task<bool> SendMultiPacket(Packet originalPacket, byte[] fullPacketBuffer)
    {
        var dataPortion = fullPacketBuffer[Packet.HEADER_SIZE..];
        
        var rawChunks = new List<byte[]>();
        for (int i = 0; i < dataPortion.Length; i += 6000)
        {
            var chunkLength = Math.Min(6000, dataPortion.Length - i);
            var chunk = new byte[chunkLength];
            Array.Copy(dataPortion, i, chunk, 0, chunkLength);
            rawChunks.Add(chunk);
        }
        
        var base64Chunks = new List<string>();
        uint totalBase64Size = 0;
        foreach (var rawChunk in rawChunks)
        {
            var base64Chunk = Convert.ToBase64String(rawChunk).Replace("=", "%3d");
            base64Chunks.Add(base64Chunk);
            totalBase64Size += (uint)base64Chunk.Length;
        }

        _logger?.LogTrace("Sending packet as multi-part - ID: {id}, Total Base64 Size: {size}, Chunks: {chunks}, Data:\n{Data}",
            originalPacket.Id, totalBase64Size, base64Chunks.Count, Encoding.ASCII.GetString(fullPacketBuffer));

        for (int i = 0; i < base64Chunks.Count; i++)
        {
            var base64Chunk = base64Chunks[i];
            var chunkData = new Dictionary<string, string>
            {
                ["data"] = base64Chunk,
                ["size"] = totalBase64Size.ToString(),

                // BC1 (Fesl SDK 3.5) sizes its reassembly buffer from decodedSize
                ["decodedSize"] = dataPortion.Length.ToString()
            };
            
            var chunkPacket = new Packet(
                originalPacket.Type,
                FeslTransmissionType.MultiPacketResponse,
                originalPacket.Id,
                chunkData
            );
            
            var chunkBuffer = await chunkPacket.Serialize();
            
            _logger?.LogTrace("Sending multi-packet part - ID: {id}, Base64 Chunk Size: {chunkSize}, Part: {part}/{total}, Packet Size: {packetSize}",
                originalPacket.Id, base64Chunk.Length, i + 1, base64Chunks.Count, chunkBuffer.Length);
            
            if (!await SendBinary(chunkBuffer))
            {
                return false;
            }
        }
        
        return true;
    }

    private async Task<bool> SendBinary(byte[] buffer)
    {
        if (NetworkStream is null || !NetworkStream.CanWrite)
        {
            _logger?.LogDebug("Tried writing to disconnected endpoint: {endpoint}!", RemoteEndpoint);
            return false;
        }

        try
        {
            NetworkStream.Write(buffer);
            await NetworkStream.FlushAsync(_cts.Token);
            _logger?.LogTrace("data sent:{data}", Encoding.ASCII.GetString(buffer));
            return true;
        }
        catch (Exception e)
        {
            _logger?.LogDebug(e, "Failed writing to endpoint: {endpoint}!", RemoteEndpoint);
            _cts.Cancel();
            return false;
        }
    }
    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _bufferPool.Return(_readBufferArray, clearArray: true);
        await _multiPacketBuffer.DisposeAsync();
        _cts.Dispose();
    }
}