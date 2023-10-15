using System.Net.Sockets;
using Arcadia.EA.Constants;
using Arcadia.Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Arcadia.EA.Proxy;

public class FeslProxy
{
    private readonly ILogger<FeslProxy> _logger;
    private readonly FeslSettings _settings;

    private TlsServerProtocol? _arcadiaProtocol;
    private TlsClientProtocol? _upstreamProtocol;
    private BcTlsCrypto? _crypto;

    public FeslProxy(ILogger<FeslProxy> logger, IOptions<FeslSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task StartProxy(TlsServerProtocol arcadiaProtocol, BcTlsCrypto crypto)
    {
        _arcadiaProtocol = arcadiaProtocol;
        _crypto = crypto;

        InitializeUpstreamClient();
        await StartProxying();
    }

    private void InitializeUpstreamClient()
    {
        _logger.LogInformation($"Connecting to upstream {_settings.ServerAddress}:{_settings.ServerPort}");

        var upstreamTcpClient = new TcpClient(_settings.ServerAddress, _settings.ServerPort);
        var upstreamTcpStream = upstreamTcpClient.GetStream();
        _upstreamProtocol = new TlsClientProtocol(upstreamTcpStream);

        var proxyTlsAuth = new ProxyTlsAuthentication(_logger);
        var upstreamClient = new Ssl3TlsClient(_crypto!, proxyTlsAuth);

        try
        {
            _upstreamProtocol.Connect(upstreamClient);
            _logger.LogDebug("SSL Handshake with upstream successful!");
        }
        catch(Exception e)
        {
            _logger.LogError(e.Message);
            throw new Exception($"Failed to connect to upstream {_settings.ServerAddress}:{_settings.ServerPort}");
        }
    }

    private async Task StartProxying()
    {
        var clientToFeslTask = Task.Run(() =>
        {
            try
            {
                while (_arcadiaProtocol!.IsConnected)
                {
                    ProxyApplicationData(_arcadiaProtocol, _upstreamProtocol!);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to proxy data from client: {e.Message}");
            }

            return Task.CompletedTask;
        });

        var feslToClientTask = Task.Run(() =>
        {
            try
            {
                while (_arcadiaProtocol!.IsConnected)
                {
                    ProxyApplicationData(_upstreamProtocol!, _arcadiaProtocol);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to proxy data from upstream: {e.Message}");
            }
            return Task.CompletedTask;

        });

        await Task.WhenAny(clientToFeslTask, feslToClientTask);
        _logger.LogInformation("Proxy connection closed, exiting...");
    }

    private async void ProxyApplicationData(TlsProtocol source, TlsProtocol destination)
    {
        var readBuffer = new byte[8096];
        int? read = 0;

        while (source.IsConnected)
        {
            try
            {
                read = source.ReadApplicationData(readBuffer, 0, readBuffer.Length);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to read proxy data");
                return;
            }

            if (!(read > 0))
            {
                continue;
            }

            var feslPacket = AnalyzeFeslPacket(readBuffer.AsSpan(0, read.Value).ToArray());
            var dataString = feslPacket?.DataDict.Select(x => $"{x.Key}={x.Value}").Aggregate((x, y) => $"{x}; {y}");
            _logger.LogTrace($"Proxying id={feslPacket?.Id} len={feslPacket?.Length} {feslPacket?.Type}, data: {dataString}");

            if (feslPacket != null && feslPacket?.Type == "fsys" && feslPacket?["TXN"] == "Hello")
            {
                var packet = feslPacket.Value;
                var theaterIp = packet["theaterIp"];
                var theaterPort = packet["theaterPort"];

                if (!string.IsNullOrWhiteSpace(theaterIp) && !string.IsNullOrWhiteSpace(theaterPort) && (!string.IsNullOrWhiteSpace(_settings.ProxyOverrideTheaterIp) || _settings.ProxyOverrideTheaterPort != 0))
                {
                    _logger.LogInformation("Overriding server theater details...");

                    if (!string.IsNullOrWhiteSpace(_settings.ProxyOverrideTheaterIp))
                    {
                        packet["theaterIp"] = _settings.ProxyOverrideTheaterIp;                        
                    }

                    if (_settings.ProxyOverrideTheaterPort != 0)
                    {
                        packet["theaterPort"] = _settings.ProxyOverrideTheaterPort.ToString();                        
                    }
                    
                    var newBuffer = await packet.Serialize();

                    read = newBuffer.Length;
                    Array.Copy(newBuffer, readBuffer, read.Value);
                    
                    var dataStringMod = feslPacket?.DataDict.Select(x => $"{x.Key}={x.Value}").Aggregate((x, y) => $"{x}; {y}");
                    _logger.LogTrace($"FESL packet with override id={feslPacket?.Id} len={feslPacket?.Length} {feslPacket?.Type}, data: {dataStringMod}");
                }
            }
            
            if (feslPacket != null && feslPacket?.Type == "pnow" && feslPacket?["TXN"] == "Status")
            {
                var packet = feslPacket.Value;
                var sessionState = packet["sessionState"];

                if (!string.IsNullOrWhiteSpace(sessionState) && _settings.ProxyOverridePlayNowGameGid != 0 && _settings.ProxyOverridePlayNowGameLid != 0)
                {
                    _logger.LogInformation("Overriding server play now response...");
                    
                    var pnowData = new Dictionary<string, object>
                    {
                        { "TXN", "Status" },
                        { "id.id", packet["id.id"] },
                        { "id.partition", packet["id.partition"] },
                        { "sessionState", "COMPLETE" },
                        { "props.{}", 2 },
                        { "props.{resultType}", "JOIN" },
                        { "props.{games}.[]", 1 },
                        { "props.{games}.0.fit", 3349 },
                        { "props.{games}.0.gid", _settings.ProxyOverridePlayNowGameGid },
                        { "props.{games}.0.lid", _settings.ProxyOverridePlayNowGameLid }
                    };

                    var pnowPacket = new Packet("pnow", FeslTransmissionType.SinglePacketResponse, packet.Id, pnowData);
                    
                    var newBuffer = await pnowPacket.Serialize();

                    read = newBuffer.Length;
                    Array.Copy(newBuffer, readBuffer, read.Value);
                    
                    var dataStringMod = pnowPacket.DataDict.Select(x => $"{x.Key}={x.Value}").Aggregate((x, y) => $"{x}; {y}");
                    _logger.LogTrace($"FESL packet with override id={pnowPacket.Id} len={pnowPacket.Length} {pnowPacket.Type}, data: {dataStringMod}");
                }
            }

            if (feslPacket != null && feslPacket?.Type == "acct" && feslPacket?["TXN"] == "NuPS3Login")
            {
                var packet = feslPacket.Value;
                var clientTicket = packet["ticket"];

                if (!string.IsNullOrWhiteSpace(clientTicket) && _settings.DumpClientTicket)
                {
                    _logger.LogCritical(clientTicket);
                    throw new Exception("Ticket dumped, exiting...");
                }

                if (!string.IsNullOrWhiteSpace(clientTicket) && !string.IsNullOrWhiteSpace(_settings.ProxyOverrideClientTicket))
                {
                    try
                    {
                        _logger.LogInformation("Overriding client ticket...");

                        packet["ticket"] = _settings.ProxyOverrideClientTicket;
                        packet["macAddr"] = _settings.ProxyOverrideClientMacAddr;

                        var dataStringMod = packet.DataDict.Select(x => $"{x.Key}={x.Value}").Aggregate((x, y) => $"{x}; {y}");
                        _logger.LogDebug($"Rewritten id={packet.Id} len={packet.Length} {packet.Type}, data: {dataStringMod}");

                        var newBuffer = await packet.Serialize();

                        read = newBuffer.Length;
                        Array.Copy(newBuffer, readBuffer, read.Value);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, $"Failed to override ticket: {e.Message}");
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(clientTicket))
                {
                    _logger.LogInformation("Received login!");
                }
            }

            destination.WriteApplicationData(readBuffer, 0, read.Value);
        }
    }

    private static Packet? AnalyzeFeslPacket(byte[] buffer)
    {
        var packet = new Packet(buffer);
        if (!packet.DataDict.TryGetValue("TXN", out var txnObj) || txnObj == null)
        {
            return null;
        }

        var txn = txnObj as string;
        if (string.IsNullOrWhiteSpace(txn)) return null;

        return packet;
    }
}

public class ProxyTlsAuthentication : TlsAuthentication
{
    private readonly ILogger _logger;

    public ProxyTlsAuthentication(ILogger logger)
    {
        _logger = logger;
    }

    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        throw new NotImplementedException();
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        _logger.LogDebug("Ignoring server certificate...");
    }
}