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
    
    private readonly FeslSettings _feslSettings;
    private readonly ProxySettings _proxySettings;

    private TlsServerProtocol? _arcadiaProtocol;
    private TlsClientProtocol? _upstreamProtocol;
    private BcTlsCrypto? _crypto;

    private string? _personaName;

    public FeslProxy(ILogger<FeslProxy> logger, IOptions<FeslSettings> feslSettings, IOptions<ProxySettings> proxySettings)
    {
        _logger = logger;
        _feslSettings = feslSettings.Value;
        _proxySettings = proxySettings.Value;
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
        _logger.LogInformation("Connecting to upstream {ServerAddress}:{ServerPort}", _feslSettings.ServerAddress, _feslSettings.ServerPort);

        var upstreamTcpClient = new TcpClient(_feslSettings.ServerAddress, _feslSettings.ServerPort);
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
            _logger.LogError("Failed to connect to upstream: {Message}", e.Message);
            throw new Exception($"Failed to connect to upstream {_feslSettings.ServerAddress}:{_feslSettings.ServerPort}");
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
                _logger.LogError(e, "Failed to proxy data from client: {Message}", e.Message);
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
                _logger.LogError(e, "Failed to proxy data from upstream: {Message}", e.Message);
            }
            return Task.CompletedTask;

        });

        await Task.WhenAny(clientToFeslTask, feslToClientTask);
        _logger.LogInformation("Proxy connection closed");
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

            var incomingPacket = AnalyzeFeslPacket(readBuffer.AsSpan(0, read.Value).ToArray());
            LogPacket("Proxying", incomingPacket);

            if (incomingPacket != null)
            {
                var (overridePacket, respond) = PotentiallyBuildOverridePacket(incomingPacket.Value);
                if (overridePacket != null)
                {
                    LogPacket("Packet after override", overridePacket.Value);
                    
                    var newBuffer = await overridePacket.Value.Serialize();

                    read = newBuffer.Length;
                    Array.Copy(newBuffer, readBuffer, read.Value);
                }

                if (respond)
                {
                    LogPacket("Responding with packet", overridePacket);
                    try
                    {
                        source.WriteApplicationData(readBuffer, 0, read.Value);
                        return; // We want to consume the response, so skip sending it to the client
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to write proxy data");
                        return;
                    }
                }
            }

            try
            {
                destination.WriteApplicationData(readBuffer, 0, read.Value);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to write proxy data");
                return;
            }
        }
    }

    private (Packet?, bool) PotentiallyBuildOverridePacket(Packet originalPacket)
    {
        var type = originalPacket.Type;
        var transmissionType = originalPacket.TransmissionType;
        var txn = originalPacket["TXN"];
        
        var enableTheaterOverride = !string.IsNullOrWhiteSpace(_proxySettings.ProxyOverrideTheaterIp) ||
                                    _proxySettings.ProxyOverrideTheaterPort != 0;
        if (enableTheaterOverride &&
            type == "fsys" &&
            transmissionType == FeslTransmissionType.SinglePacketResponse &&
            txn == "Hello")
        {
            _logger.LogInformation("Overriding server theater details...");

            var overridePacket = originalPacket.Clone();
            if (!string.IsNullOrWhiteSpace(_proxySettings.ProxyOverrideTheaterIp))
            {
                overridePacket["theaterIp"] = _proxySettings.ProxyOverrideTheaterIp;
            }

            if (_proxySettings.ProxyOverrideTheaterPort != 0)
            {
                overridePacket["theaterPort"] = _proxySettings.ProxyOverrideTheaterPort.ToString();
            }
            
            return (overridePacket, false);
        }

        var enablePlayNowGameOverride =
            _proxySettings.ProxyOverridePlayNowGameGid != 0 && _proxySettings.ProxyOverridePlayNowGameLid != 0;
        if (enablePlayNowGameOverride &&
            type == "pnow" &&
            transmissionType == FeslTransmissionType.SinglePacketResponse &&
            txn == "Status")
        {
            _logger.LogInformation("Overriding server play now response...");

            var overridePacketData = new Dictionary<string, object>
            {
                { "TXN", "Status" },
                { "id.id", originalPacket["id.id"] },
                { "id.partition", originalPacket["id.partition"] },
                { "sessionState", "COMPLETE" },
                { "props.{}", 2 },
                { "props.{resultType}", "JOIN" },
                { "props.{games}.[]", 1 },
                { "props.{games}.0.fit", 3349 },
                { "props.{games}.0.gid", _proxySettings.ProxyOverridePlayNowGameGid },
                { "props.{games}.0.lid", _proxySettings.ProxyOverridePlayNowGameLid }
            };

            var overridePacket = new Packet("pnow", FeslTransmissionType.SinglePacketResponse, originalPacket.Id, overridePacketData);
            return (overridePacket, false);
        }

        if (type == "acct" &&
            transmissionType == FeslTransmissionType.SinglePacketRequest &&
            txn == "NuPS3Login")
        {
            var clientTicket = originalPacket["ticket"];
            if (!string.IsNullOrWhiteSpace(clientTicket) && _proxySettings.DumpClientTicket)
            {
                _logger.LogCritical(clientTicket);
                throw new Exception("Ticket dumped, exiting...");
            }

            if (!string.IsNullOrWhiteSpace(clientTicket) &&
                !string.IsNullOrWhiteSpace(_proxySettings.ProxyOverrideClientTicket))
            {
                _logger.LogInformation("Overriding client ticket...");

                var overrideTicket = originalPacket.Clone();
                overrideTicket["ticket"] = _proxySettings.ProxyOverrideClientTicket;
                return (overrideTicket, false);
            }
        }

        var enableLoginOverride = !string.IsNullOrWhiteSpace(_proxySettings.ProxyOverrideAccountEmail) &&
                                  !string.IsNullOrWhiteSpace(_proxySettings.ProxyOverrideAccountPassword);
        if (enableLoginOverride &&
            type == "acct" &&
            transmissionType == FeslTransmissionType.SinglePacketRequest &&
            txn == "NuPS3Login")
        {
            var macAddr = !string.IsNullOrWhiteSpace(_proxySettings.ProxyOverrideClientMacAddr)
                ? _proxySettings.ProxyOverrideClientMacAddr
                : originalPacket["macAddr"];

            _logger.LogInformation("Overriding client login request...");

            var overridePacketData = new Dictionary<string, object>
            {
                { "TXN", "NuLogin" },
                { "returnEncryptedInfo", 0 },
                { "nuid", _proxySettings.ProxyOverrideAccountEmail },
                { "password", _proxySettings.ProxyOverrideAccountPassword },
                { "macAddr", macAddr },
            };

            var overridePacket = new Packet("acct", FeslTransmissionType.SinglePacketRequest, originalPacket.Id, overridePacketData);
            return (overridePacket, false);
        }

        if (enableLoginOverride &&
            type == "acct" &&
            transmissionType == FeslTransmissionType.SinglePacketResponse &&
            txn == "NuLogin")
        {
            // Make sure response contains an lkey, indicating successful login
            var lkey = originalPacket["lkey"];
            if (!string.IsNullOrWhiteSpace(lkey))
            {
                _logger.LogInformation("Consuming server login response...");

                var overridePacketData = new Dictionary<string, object>
                {
                    { "TXN", "NuGetPersonas" },
                    { "namespace", "" },
                };

                var overridePacket = new Packet("acct", FeslTransmissionType.SinglePacketRequest, originalPacket.Id, overridePacketData);
                return (overridePacket, true); // Directly respond to sender with override ticket
            }
        }

        if (enableLoginOverride &&
            type == "acct" &&
            transmissionType == FeslTransmissionType.SinglePacketResponse &&
            txn == "NuGetPersonas")
        {
            // Ensure response contains a persona we can use
            var personaName = originalPacket["personas.0"];
            if (!string.IsNullOrWhiteSpace(personaName))
            {
                _logger.LogInformation("Consuming server get personas response...");
                
                _personaName = personaName;

                var overridePacketData = new Dictionary<string, object>
                {
                    { "TXN", "NuLoginPersona" },
                    { "name", _personaName },
                };
                
                var overridePacket = new Packet("acct", FeslTransmissionType.SinglePacketRequest, originalPacket.Id, overridePacketData);
                return (overridePacket, true); // Directly respond to sender with override ticket
            }
        }

        if (enableLoginOverride &&
            type == "acct" &&
            transmissionType == FeslTransmissionType.SinglePacketResponse &&
            txn == "NuLoginPersona")
        {
            // Ensure we have both an lkey in the response and a persona name we can use
            var lkey = originalPacket["lkey"];
            if (!string.IsNullOrWhiteSpace(lkey) && !string.IsNullOrWhiteSpace(_personaName))
            {
                _logger.LogInformation("Overriding server profile details...");

                var overridePacketData = new Dictionary<string, object>
                {
                    { "TXN", "PS3Login" },
                    { "userId", originalPacket["userId"] },
                    { "personaName", _personaName },
                    { "lkey", lkey }
                };

                var overridePacket = new Packet("acct", FeslTransmissionType.SinglePacketResponse, originalPacket.Id, overridePacketData);
                return (overridePacket, false);
            }
        }
        
        return (null, false);
    }

    private void LogPacket(string msg, Packet? packet)
    {
        var dataStringMod = packet?.DataDict.Select(x => $"{x.Key}={x.Value}").Aggregate((x, y) => $"{x}; {y}");
        _logger.LogTrace("{msg} id={Id} len={Length} {Type}, data: {dataString}", msg, packet?.Id, packet?.Length, packet?.Type, dataStringMod);
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