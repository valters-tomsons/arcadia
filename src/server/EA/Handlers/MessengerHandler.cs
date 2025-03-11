using System.Collections.Immutable;
using System.Net.Sockets;
using Arcadia.EA.Constants;
using Arcadia.Storage;
using Microsoft.Extensions.Logging;

namespace Arcadia.EA.Handlers;

public class MessengerHandler
{
    private readonly ILogger<MessengerHandler> _logger;
    private readonly IEAConnection _conn;
    private readonly SharedCache _storage;

    private readonly ImmutableDictionary<string, Func<Packet, Task>> _handlers;

    public MessengerHandler(IEAConnection conn, ILogger<MessengerHandler> logger, SharedCache storage)
    {
        _logger = logger;
        _conn = conn;
        _storage = storage;

        _handlers = new Dictionary<string, Func<Packet, Task>>
        {
            ["AUTH"] = HandleAUTH,
            ["RGET"] = HandleRGET,
            ["PSET"] = HandlePSET
        }.ToImmutableDictionary();
    }

    public async Task HandleClientConnection(NetworkStream network, string clientEndpoint, string serverEndpoint, CancellationToken ct)
    {
        _conn.Initialize(network, clientEndpoint, serverEndpoint, ct);
        await foreach (var packet in _conn.StartConnection(_logger))
        {
            await HandlePacket(packet);
        }
    }

    public async Task HandlePacket(Packet packet)
    {
        var packetType = packet.Type;
        _handlers.TryGetValue(packetType, out var handler);

        if (handler is null)
        {
            _logger.LogWarning("Unknown packet type: {type}", packetType);
            return;
        }

        await handler(packet);
    }

    private async Task HandleAUTH(Packet request)
    {
        var lkey = request["LKEY"];
        var plasma = _storage.FindSessionByLkey(lkey);

        if (string.IsNullOrWhiteSpace(lkey) || plasma is null)
        {
            throw new Exception("No plasma session found with requested LKEY");
        }

        var response = new Dictionary<string, string>
        {
            ["TTID"] = "0",
            ["TITL"] = "A Game",
            ["ID"] = "1",
            ["USER"] = $"{request["USER"]}@messaging.ea.com/eagames/{plasma.PartitionId}"
        };

        var packet = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandleRGET(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            ["ID"] = request["ID"],
            ["SIZE"] = "0",
        };

        var packet = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }

    private async Task HandlePSET(Packet request)
    {
        var response = new Dictionary<string, string>
        {
            ["ID"] = request["ID"]
        };

        var packet = new Packet(request.Type, TheaterTransmissionType.OkResponse, 0, response);
        await _conn.SendPacket(packet);
    }
}