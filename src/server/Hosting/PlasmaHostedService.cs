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
using Arcadia.Storage;
using Arcadia.EA.Handlers;
using Arcadia.EA.Constants;

namespace Arcadia.Hosting;

public class PlasmaHostedService : IHostedService
{
    private readonly ILogger<PlasmaHostedService> _logger;
    private readonly ArcadiaSettings _arcadiaSettings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SharedCache _sharedCache;
    private readonly AsymmetricKeyParameter _feslCertKey;
    private readonly Certificate _feslPubCert;

    private readonly List<TcpListener> _tcpListeners = [];
    private readonly List<UdpClient> _udpListeners = [];

    public PlasmaHostedService(ILogger<PlasmaHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings, ProtoSSL certGenerator, IServiceScopeFactory scopeFactory, SharedCache sharedCache)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _sharedCache = sharedCache;
        _arcadiaSettings = arcadiaSettings.Value;
        (_feslCertKey, _feslPubCert) = certGenerator.GetFeslEaCert();
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

        ThreadPool.QueueUserWorkItem(async callback =>
        {
            listener.Start();
            while (!processCt.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync(processCt);
                    _ = HandleTcpConnection(tcpClient, port);
                }
                catch (TlsNoCloseNotifyException)
                {
                    _logger.LogWarning("Client terminated the connection without warning on port {port}", port);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error accepting client on port {port}", port);
                }
            }
        });
    }

    private async Task HandleTcpConnection(TcpClient tcpClient, int connectionPort)
    {
        var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString()! ?? throw new NullReferenceException("ClientEndpoint cannot be null!");
        _logger.LogInformation("Opening connection from: {clientEndpoint} to {port}", clientEndpoint, connectionPort);

        using var scope = _scopeFactory.CreateAsyncScope();

        var networkStream = tcpClient.GetStream();
        PlasmaSession? plasma = null;

        try
        {
            if (PortExtensions.IsFeslPort(connectionPort))
            {
                var crypto = scope.ServiceProvider.GetRequiredService<Rc4TlsCrypto>();
                var connTls = new Ssl3TlsServer(crypto, _feslPubCert, _feslCertKey);
                var serverProtocol = new TlsServerProtocol(networkStream);
                serverProtocol.Accept(connTls);

                var clientHandler = scope.ServiceProvider.GetRequiredService<FeslHandler>();
                plasma = await clientHandler.HandleClientConnection(serverProtocol, clientEndpoint, tcpClient.Client.LocalEndPoint!.ToString()!);
            }
            else if (PortExtensions.IsTheater(connectionPort))
            {
                var clientHandler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
                plasma = await clientHandler.HandleClientConnection(networkStream, clientEndpoint, tcpClient.Client.LocalEndPoint!.ToString()!);
            }
            else if (_arcadiaSettings.MessengerPort == connectionPort)
            {
                var clientHandler = scope.ServiceProvider.GetRequiredService<MessengerHandler>();
                await clientHandler.HandleClientConnection(networkStream, clientEndpoint, tcpClient.Client.LocalEndPoint!.ToString()!);
            }
            else
            {
                throw new NotSupportedException($"No handler defined for port {connectionPort}");
            }
        }
        finally
        {
            _logger.LogInformation("Closing connection: {clientEndpoint}", clientEndpoint);

            if (plasma != null)
            {
                _logger.LogInformation("Terminating plasma session: {uid}, {name} | fesl={fesl}, thea={thea}", plasma.UID, plasma.NAME, plasma.FeslConnection is not null, plasma.TheaterConnection is not null);

                plasma.FeslConnection?.NetworkStream?.Close();
                plasma.TheaterConnection?.NetworkStream?.Close();
                _sharedCache.RemoveConnection(plasma);
            }

            await networkStream.DisposeAsync();
            tcpClient.Close();
            tcpClient.Dispose();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}