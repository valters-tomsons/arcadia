using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Arcadia.EA.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.EA;

public enum NatType { Unknown, Open, Moderate, Strict }
public record NatRoute(IPEndPoint ForHost, IPEndPoint ForClient, bool Relayed);

public class NatService(ILogger<NatService> logger, IOptions<ArcadiaSettings> settings)
{
    private class Client
    {
        public IPEndPoint Endpoint { get; set; } = new(IPAddress.None, 0);
        public bool? Open { get; set; }
        public string? ProbeTid { get; set; }
        public int ProbeSends { get; set; }
    }

    private const int LastEchoSend = 4;
    private const int RelayPorts = 1000;

    private static readonly TimeSpan OpenAfter = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FailedJoinMemory = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RelayIdleTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger<NatService> _logger = logger;
    private readonly int _port = settings.Value.NatPort;
    private readonly ConcurrentDictionary<ulong, Client> _clients = new();
    private readonly ConcurrentDictionary<(ulong Uid, long Gid), DateTime> _pendingJoins = new();
    private readonly ConcurrentDictionary<(ulong Uid, long Gid), NatRoute> _routes = new();
    private readonly ConcurrentDictionary<(ulong Uid, long Gid), Relay> _relays = new();

    private UdpClient? _probeSocket;
    private int _nextRelayPort;
    public bool Enabled => _port > 0;

    private IPEndPoint? GamePort(ulong uid) => _clients.TryGetValue(uid, out var c) ? c.Endpoint : null;

    public NatType GetNatType(ulong uid, int internalPort)
    {
        if (!_clients.TryGetValue(uid, out var c) || c.Open is null) return NatType.Unknown;
        if (c.Open == true) return NatType.Open;

        return c.Endpoint.Port == internalPort ? NatType.Moderate : NatType.Strict;
    }

    public async Task HandleEcho(UdpClient listener, UdpReceiveResult result, Packet request, CancellationToken ct)
    {
        var tid = request["TID"];

        var response = new Packet("ECHO", TheaterTransmissionType.OkResponse, 0, new()
        {
            ["TXN"] = "ECHO",
            ["IP"] = result.RemoteEndPoint.Address.ToString(),
            ["PORT"] = result.RemoteEndPoint.Port.ToString(),
            ["ERR"] = "0",
            ["TYPE"] = "1",
            ["TID"] = tid
        });

        if (!ulong.TryParse(request["UID"], out var uid) || uid == 0)
        {
            await listener.SendAsync(await response.Serialize(), result.RemoteEndPoint, ct);
            return;
        }

        var client = _clients.GetOrAdd(uid, _ => new());

        bool probe;
        bool? open;
        lock (client)
        {
            if (!client.Endpoint.Address.Equals(result.RemoteEndPoint.Address)) client.Open = null;

            client.Endpoint = result.RemoteEndPoint;

            if (client.ProbeTid is null && client.Open is null)
            {
                client.ProbeTid = tid;
                client.ProbeSends = 0;
            }

            if (client.ProbeTid == tid && ++client.ProbeSends >= LastEchoSend)
            {
                client.ProbeTid = null;
                client.Open = false;
                _logger.LogInformation("NAT: user {Uid} at {Endpoint} is not open", uid, result.RemoteEndPoint);
            }

            probe = client.ProbeTid == tid;
            open = client.Open;
        }

        if (!probe)
        {
            // BeachMod extension, ignored by the game
            if (open is not null) response["NATOPEN"] = open.Value ? "1" : "0";

            await listener.SendAsync(await response.Serialize(), result.RemoteEndPoint, ct);
            return;
        }

        _probeSocket ??= new UdpClient(new IPEndPoint(IPAddress.Any, _port));
        await _probeSocket.SendAsync(await response.Serialize(), result.RemoteEndPoint);

        _ = Task.Delay(OpenAfter, ct).ContinueWith(_ =>
        {
            lock (client)
            {
                if (client.ProbeTid != tid || client.Open is not null) return;
                client.ProbeTid = null;
                client.Open = true;
            }

            _logger.LogInformation("NAT: user {Uid} at {Endpoint} is open", uid, client.Endpoint);
        }, ct);
    }

    // Relay: strict host, moderate host with a strict client, or a repeated join that didn't reach PENT
    public NatRoute? Route(GameServerListing game, PlasmaSession client, int clientInternalPort)
    {
        var key = (client.User.UserId, game.GID);
        var hostPort = GamePort(game.UID);
        var clientPort = GamePort(client.User.UserId);

        if (hostPort is null || clientPort is null || game.TheaterConnection is null || client.TheaterConnection is null)
        {
            _routes.TryRemove(key, out _);
            return null;
        }

        if (!int.TryParse(game.Data.GetValueOrDefault("INT-PORT"), out var hostInternalPort))
        {
            _logger.LogWarning("Host internal port is not a number");
        }

        var hostType = GetNatType(game.UID, hostInternalPort);
        var clientType = GetNatType(client.User.UserId, clientInternalPort);
        var failedBefore = _pendingJoins.TryGetValue(key, out var since) && DateTime.UtcNow - since < FailedJoinMemory;

        var relay = !hostPort.Address.Equals(clientPort.Address) && (failedBefore || hostType == NatType.Strict || (hostType == NatType.Moderate && clientType == NatType.Strict));

        _logger.LogInformation(
            "NAT: {Client} ({ClientType}, {ClientPort}) joins {Host} ({HostType}, {HostPort}){Failed}: {Route}",
            client.User.Username, clientType, clientPort, game.NAME, hostType, hostPort,
            failedBefore ? " again" : "", relay ? "relay" : "direct"
        );

        _pendingJoins[key] = DateTime.UtcNow;
        if (_relays.TryRemove(key, out var old)) old.Dispose();

        var opened = relay ? Relay.Open(this, key, (client.User.UserId, clientPort.Address), (game.UID, hostPort.Address)) : null;
        if (relay && opened is null) _logger.LogError("NAT: no free relay port");
        if (opened is null) return _routes[key] = new(clientPort, hostPort, false);

        _relays[key] = opened;

        return _routes[key] = new(
            new(IPAddress.Parse(game.TheaterConnection.LocalAddress), opened.HostSidePort),
            new(IPAddress.Parse(client.TheaterConnection.LocalAddress), opened.ClientSidePort),
            true
        );
    }

    public NatRoute? RouteFor(ulong uid, long gid) => _routes.GetValueOrDefault((uid, gid));
    public void Joined(ulong uid, long gid) => _pendingJoins.TryRemove((uid, gid), out _);

    private void Seen(ulong uid, IPEndPoint endpoint)
    {
        if (!_clients.TryGetValue(uid, out var c)) return;
        lock (c)
        {
            if (!c.Endpoint.Address.Equals(endpoint.Address) || c.Endpoint.Equals(endpoint)) return;

            _logger.LogInformation("NAT: user {Uid} game port moved {Old} -> {New}", uid, c.Endpoint, endpoint);
            c.Endpoint = endpoint;
        }
    }

    private int NextRelayPort() => _port + 1 + (int)((uint)Interlocked.Increment(ref _nextRelayPort) % RelayPorts);

    private sealed class Relay : IDisposable
    {
        private readonly NatService _owner;
        private readonly (ulong, long) _key;
        private readonly UdpClient _clientSide;
        private readonly UdpClient _hostSide;
        private readonly (ulong Uid, IPAddress Address) _clientPeer;
        private readonly (ulong Uid, IPAddress Address) _hostPeer;
        private readonly CancellationTokenSource _cts = new();
        private IPEndPoint? _client;
        private IPEndPoint? _host;
        private DateTime _lastPacket = DateTime.UtcNow;

        public int ClientSidePort { get; }
        public int HostSidePort { get; }

        private Relay(NatService owner, (ulong, long) key, UdpClient clientSide, UdpClient hostSide, (ulong, IPAddress) client, (ulong, IPAddress) host)
        {
            _owner = owner;
            _key = key;
            _clientSide = clientSide;
            _hostSide = hostSide;
            _clientPeer = client;
            _hostPeer = host;
            ClientSidePort = ((IPEndPoint)clientSide.Client.LocalEndPoint!).Port;
            HostSidePort = ((IPEndPoint)hostSide.Client.LocalEndPoint!).Port;
        }

        public static Relay? Open(NatService owner, (ulong, long) key, (ulong, IPAddress) client, (ulong, IPAddress) host)
        {
            UdpClient? first = null;
            for (var i = 0; i < RelayPorts; i++)
            {
                try
                {
                    var socket = new UdpClient(new IPEndPoint(IPAddress.Any, owner.NextRelayPort()));
                    if (first is null)
                    {
                        first = socket;
                        continue;
                    }

                    var relay = new Relay(owner, key, first, socket, client, host);
                    _ = relay.Forward(relay._clientSide, relay._hostSide, true);
                    _ = relay.Forward(relay._hostSide, relay._clientSide, false);
                    _ = relay.CloseWhenIdle();
                    owner._logger.LogInformation("NAT: relay {ClientPort} <-> {HostPort}", relay.ClientSidePort, relay.HostSidePort);
                    return relay;
                }
                catch (SocketException) { }
            }

            first?.Dispose();
            return null;
        }

        private async Task Forward(UdpClient from, UdpClient to, bool fromClient)
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var packet = await from.ReceiveAsync(_cts.Token);
                    var (Uid, Address) = fromClient ? _clientPeer : _hostPeer;
                    if (!packet.RemoteEndPoint.Address.Equals(Address)) continue;

                    if (!packet.RemoteEndPoint.Equals(fromClient ? _client : _host))
                    {
                        if (fromClient) _client = packet.RemoteEndPoint;
                        else _host = packet.RemoteEndPoint;
                        _owner.Seen(Uid, packet.RemoteEndPoint);
                    }

                    _lastPacket = DateTime.UtcNow;

                    var target = fromClient ? _host : _client;
                    if (target is not null) await to.SendAsync(packet.Buffer, target, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _owner._logger.LogWarning(e, "NAT: relay {Port} stopped", fromClient ? ClientSidePort : HostSidePort);
            }
        }

        private async Task CloseWhenIdle()
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                if (DateTime.UtcNow - _lastPacket <= RelayIdleTimeout) continue;

                _owner._logger.LogInformation("NAT: relay {ClientPort} <-> {HostPort} closed", ClientSidePort, HostSidePort);
                _owner._relays.TryRemove(new(_key, this));

                Dispose();
            }
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested) return;
            _cts.Cancel();
            _clientSide.Dispose();
            _hostSide.Dispose();
        }
    }
}
