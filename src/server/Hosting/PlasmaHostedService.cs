using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.Tls;
using Arcadia.Tls.Crypto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Arcadia.EA.Ports;
using Arcadia.EA.Handlers;
using Arcadia.EA.Constants;

namespace Arcadia.Hosting;

public class PlasmaHostedService : IHostedService
{
    private readonly ILogger<PlasmaHostedService> _logger;
    private readonly ArcadiaSettings _arcadiaSettings;
    private readonly DebugSettings _debugSettings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AsymmetricKeyParameter _feslCertKey;
    private readonly Certificate _feslPubCert;

    private readonly List<TcpListener> _tcpListeners = [];
    private readonly List<UdpClient> _udpListeners = [];

    public PlasmaHostedService(ILogger<PlasmaHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings, ProtoSSL certGenerator, IServiceScopeFactory scopeFactory, IOptions<DebugSettings> debugSettings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _arcadiaSettings = arcadiaSettings.Value;
        (_feslCertKey, _feslPubCert) = certGenerator.GetFeslEaCert();
        _debugSettings = debugSettings.Value;
    }

    public Task StartAsync(CancellationToken processCt)
    {
        if (_arcadiaSettings.ListenAddress.Length == 0)
        {
            throw new ApplicationException("Arcadia must listen on Plasma ports!");
        }

        if (_arcadiaSettings.MessengerPort > 0)
        {
            _logger.LogInformation("Messenger starting on port: {port}", _arcadiaSettings.MessengerPort);
            StartTcpListenerTask(_arcadiaSettings.MessengerPort, processCt);
        }

        if (_debugSettings.ForcePlaintext)
        {
            _logger.LogWarning("!!!!!! HERE BE DRAGONS !!!!!!!!");
            _logger.LogWarning("ForcePlaintext=true;");
            _logger.LogWarning("Plasma connections will not use SSL. Regular clients will not connect.");
        }

        foreach (var port in _arcadiaSettings.ListenPorts.Distinct())
        {
            _logger.LogInformation("Initializing listener on {address}:{port}", _arcadiaSettings.ListenAddress, port);

            StartTcpListenerTask(port, processCt);

            var isTheater = PortExtensions.IsTheater(port);
            if (isTheater)
            {
                var udpListener = new UdpClient(port);
                _udpListeners.Add(udpListener);

                ThreadPool.QueueUserWorkItem(async callback =>
                {
                    while (!processCt.IsCancellationRequested)
                    {
                        try
                        {
                            var udpResult = await udpListener.ReceiveAsync();
                            var request = new Packet(udpResult.Buffer);
                            if (request.Type == "ECHO")
                            {
                                Dictionary<string, string> data = new()
                                {
                                    { "TXN", "ECHO" },
                                    { "IP", udpResult.RemoteEndPoint.Address.ToString() },
                                    { "PORT", udpResult.RemoteEndPoint.Port.ToString() },
                                    { "ERR", "0" },
                                    { "TYPE", "1" },
                                    { "TID", request["TID"] }
                                };

                                var response = await new Packet(request.Type, TheaterTransmissionType.OkResponse, 0, data).Serialize();
                                await udpListener.SendAsync(response, udpResult.RemoteEndPoint, processCt);
                            }
                            else
                            {
                                _logger.LogError("Unknown UDP request type: {type}", request.Type);
                            }
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Error receiving UDP datagram on port {port}", port);
                        }
                    }
                });
            }
        }

        return Task.CompletedTask;
    }

    private void StartTcpListenerTask(int port, CancellationToken processCt)
    {
        var listener = new TcpListener(IPAddress.Parse(_arcadiaSettings.ListenAddress), port);
        _tcpListeners.Add(listener);

        // Ensure the port value is captured early to avoid issues with closures
        var capturedPort = port;

        ThreadPool.QueueUserWorkItem(async callback =>
        {
            listener.Start();
            while (!processCt.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync(processCt);
                    _ = HandleTcpConnection(tcpClient, capturedPort);
                }
                catch (TlsNoCloseNotifyException)
                {
                    _logger.LogWarning("Client terminated the connection without warning on port {port}", capturedPort);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error accepting client on port {port}", capturedPort);
                }
            }
        });
    }

    private async Task HandleTcpConnection(TcpClient tcpClient, int connectionPort)
    {
        var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString()! ?? throw new NullReferenceException("ClientEndpoint cannot be null!");
        _logger.LogInformation("Opening connection from: {clientEndpoint} to {port}", clientEndpoint, connectionPort);

        await using var scope = _scopeFactory.CreateAsyncScope();

        using var tcp = tcpClient;
        await using var networkStream = tcp.GetStream();

        if (PortExtensions.IsTheater(connectionPort))
        {
            var clientHandler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
            await clientHandler.HandleClientConnection(networkStream, clientEndpoint, tcpClient.Client.LocalEndPoint!.ToString()!);
        }
        else if (_arcadiaSettings.MessengerPort == connectionPort)
        {
            var clientHandler = scope.ServiceProvider.GetRequiredService<MessengerHandler>();
            await clientHandler.HandleClientConnection(networkStream, clientEndpoint, tcpClient.Client.LocalEndPoint!.ToString()!);
        }
        else
        {
            if (!PortExtensions.IsFeslPort(connectionPort))
            {
                _logger.LogError("Unknown game port: {port}, defaulting to Fesl...", connectionPort);
            }

            var clientHandler = scope.ServiceProvider.GetRequiredService<FeslHandler>();

            if (_debugSettings.ForcePlaintext)
            {
                _logger.LogWarning("Connecting fesl client without TLS!");
                await clientHandler.HandleClientConnection(networkStream, clientEndpoint, tcpClient.Client.LocalEndPoint!.ToString()!);
            }
            else
            {
                var crypto = scope.ServiceProvider.GetRequiredService<Rc4TlsCrypto>();
                var connTls = new Ssl3TlsServer(crypto, _feslPubCert, _feslCertKey);
                var serverProtocol = new TlsServerProtocol(networkStream);

                try
                {
                    serverProtocol.Accept(connTls);
                }
                catch (TlsFatalAlert e)
                {
                    _logger.LogError(e, "SSL handshake failed!");
                    throw;
                }

                await clientHandler.HandleClientConnection(serverProtocol.Stream, clientEndpoint, tcpClient.Client.LocalEndPoint!.ToString()!);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}