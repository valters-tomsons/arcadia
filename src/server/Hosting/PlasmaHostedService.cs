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
using Arcadia.EA.Constants;
using Arcadia.Handlers;

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

    private readonly SemaphoreSlim _sslHandshakeSemaphore = new(1, 1);

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
            StartUdpListenerTask(port, processCt);
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
                catch (Exception e)
                {
                    _logger.LogError(e, "Error accepting client on port {port}", capturedPort);
                }
            }
        });
    }

    private async Task HandleTcpConnection(TcpClient tcpClient, int connectionPort)
    {
        var remoteEndpoint = tcpClient.Client.RemoteEndPoint?.ToString()! ?? throw new NullReferenceException("ClientEndpoint cannot be null!");
        var localEndpoint = tcpClient.Client.LocalEndPoint!.ToString()!;
        _logger.LogInformation("Opening connection from: {clientEndpoint} to {port}", remoteEndpoint, connectionPort);

        await using var scope = _scopeFactory.CreateAsyncScope();

        using var tcp = tcpClient;
        await using var networkStream = tcp.GetStream();

        using var cts = new CancellationTokenSource();

        if (PortExtensions.IsTheater(connectionPort))
        {
            var clientHandler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
            await clientHandler.HandleClientConnection(networkStream, remoteEndpoint, localEndpoint, cts.Token);
        }
        else if (_arcadiaSettings.MessengerPort == connectionPort)
        {
            var clientHandler = scope.ServiceProvider.GetRequiredService<MessengerHandler>();
            await clientHandler.HandleClientConnection(networkStream, remoteEndpoint, localEndpoint, cts.Token);
        }
        else
        {
            if (!PortExtensions.IsFeslPort(connectionPort))
            {
                _logger.LogError("Unknown game port: {port}, defaulting to Fesl...", connectionPort);
            }

            var clientHandler = scope.ServiceProvider.GetRequiredService<FeslHandler>();
            PlasmaSession? plasma = null;

            try
            {
                if (_debugSettings.ForcePlaintext)
                {
                    _logger.LogWarning("Connecting fesl client without TLS!");
                    plasma = await clientHandler.HandleClientConnection(networkStream, remoteEndpoint, localEndpoint, cts.Token);
                }
                else
                {
                    await _sslHandshakeSemaphore.WaitAsync(cts.Token);
                    TlsServerProtocol serverProtocol;

                    try
                    {
                        var crypto = scope.ServiceProvider.GetRequiredService<Rc4TlsCrypto>();
                        var connTls = new Ssl3TlsServer(crypto, _feslPubCert, _feslCertKey);
                        serverProtocol = new TlsServerProtocol(networkStream);
                        serverProtocol.Accept(connTls);
                    }
                    finally
                    {
                        _sslHandshakeSemaphore.Release();
                    }

                    plasma = await clientHandler.HandleClientConnection(serverProtocol.Stream, remoteEndpoint, localEndpoint, cts.Token);
                }
            }
            finally
            {
                if (plasma?.TheaterConnection is not null)
                {
                    try
                    {
                        await plasma.TheaterConnection.Terminate();
                        await plasma.TheaterConnection.DisposeAsync();
                    }
                    catch { }
                }

                if (plasma?.FeslConnection is not null)
                {
                    try
                    {
                        await plasma.FeslConnection.Terminate();
                        await plasma.FeslConnection.DisposeAsync();
                    }
                    catch { }
                }
            }
        }

        await cts.CancelAsync();
    }

    private void StartUdpListenerTask(int port, CancellationToken processCt)
    {
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
                        _logger.LogError(e, "Error receiving UDP datagram");
                    }
                }
            });
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}