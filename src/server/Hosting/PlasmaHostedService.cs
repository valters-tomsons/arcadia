using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.Handlers;
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

namespace Arcadia.Hosting;

public class PlasmaHostedService : IHostedService
{
    private readonly ILogger<PlasmaHostedService> _logger;
    private readonly ArcadiaSettings _arcadiaSettings;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly AsymmetricKeyParameter _feslCertKey;
    private readonly Certificate _feslPubCert;

    private readonly List<TcpListener> _tcpListeners = [];

    private readonly static int[] _listeningPorts = [
            (int)FeslGamePort.RomePS3,
            (int)TheaterGamePort.RomePS3,
            (int)FeslServerPort.RomePC,
            (int)TheaterServerPort.RomePC
    ];

    public PlasmaHostedService(ILogger<PlasmaHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings, ProtoSSL certGenerator, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _arcadiaSettings = arcadiaSettings.Value;
        (_feslCertKey, _feslPubCert) = certGenerator.GetFeslEaCert();
    }

    public Task StartAsync(CancellationToken processCt)
    {
        foreach (var port in _listeningPorts)
        {
            _logger.LogInformation("Listening on {address}:{port}", _arcadiaSettings.ListenAddress, port);
            var listener = new TcpListener(IPAddress.Parse(_arcadiaSettings.ListenAddress), port);
            _tcpListeners.Add(listener);

            Task.Run(async () =>
            {
                listener.Start();
                while (!processCt.IsCancellationRequested)
                {
                    try
                    {
                        var tcpClient = await listener.AcceptTcpClientAsync(processCt);
                        _ = HandleTcpConnection(tcpClient, port);
                    }
                    catch(TlsNoCloseNotifyException)
                    {
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error accepting client on port {port}", port);
                    }
                }
            }, processCt);
        }

        return Task.CompletedTask;
    }

    private async Task HandleTcpConnection(TcpClient tcpClient, int connectionPort)
    {
        var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString()! ?? throw new NullReferenceException("ClientEndpoint cannot be null!");
        _logger.LogInformation("Opening connection from: {clientEndpoint} to {port}", clientEndpoint, connectionPort);

        using var scope = _scopeFactory.CreateAsyncScope();
        var networkStream = tcpClient.GetStream();

        try
        {
            var isFeslGame = Enum.IsDefined(typeof(FeslGamePort), connectionPort);
            var isFeslServer = Enum.IsDefined(typeof(FeslServerPort), connectionPort);
            var isTheater = Enum.IsDefined(typeof(TheaterGamePort), connectionPort) || Enum.IsDefined(typeof(TheaterServerPort), connectionPort);

            if (isFeslGame || isFeslServer)
            {
                var crypto = scope.ServiceProvider.GetRequiredService<Rc4TlsCrypto>();
                var connTls = new Ssl3TlsServer(crypto, _feslPubCert, _feslCertKey);
                var serverProtocol = new TlsServerProtocol(networkStream);
                serverProtocol.Accept(connTls);

                if (isFeslGame)
                {
                    var clientHandler = scope.ServiceProvider.GetRequiredService<FeslClientHandler>();
                    await clientHandler.HandleClientConnection(serverProtocol, clientEndpoint, (FeslGamePort)connectionPort);
                }
                else
                {
                    var serverHandler = scope.ServiceProvider.GetRequiredService<FeslServerHandler>();
                    await serverHandler.HandleClientConnection(serverProtocol, clientEndpoint);
                }
            }
            else if (isTheater)
            {
                var clientHandler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
                await clientHandler.HandleClientConnection(networkStream, clientEndpoint);
            }
            else
            {
                throw new NotSupportedException($"No handler defined for port {connectionPort}");
            }
        }
        finally
        {
            _logger.LogInformation("Closing connection from: {clientEndpoint}", clientEndpoint);

            networkStream.Close();
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