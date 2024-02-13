using System.Collections.Concurrent;
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

public class FeslHostedService : IHostedService
{
    private readonly ILogger<FeslHostedService> _logger;
    private readonly ArcadiaSettings _arcadiaSettings;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly AsymmetricKeyParameter _feslCertKey;
    private readonly Certificate _feslPubCert;
    private readonly ConcurrentBag<TcpListener> _listeners = new();
    private readonly ConcurrentBag<Task> _activeConnections = new();
    private readonly ConcurrentBag<Task?> _servers = new();
    private CancellationTokenSource _cts = null!;

    public FeslHostedService(ILogger<FeslHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings, ProtoSSL certGenerator, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _arcadiaSettings = arcadiaSettings.Value;
        (_feslCertKey, _feslPubCert) = certGenerator.GetFeslEaCert();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var listeningPorts = new int[] {
            (int)FeslGamePort.BeachPS3,
            (int)FeslGamePort.BadCompanyPS3,
            (int)FeslGamePort.RomePS3,
            (int)FeslGamePort.RomePC,
            (int)FeslServerPort.RomePC
        };

        foreach (var port in listeningPorts)
        {
            CreateFeslPortListener(port);
        }

        return Task.CompletedTask;
    }

    private void CreateFeslPortListener(int listenerPort)
    {
        var serverFesl = Task.Run(async () =>
        {
            var listener = new TcpListener(IPAddress.Parse(_arcadiaSettings.ListenAddress), listenerPort);
            listener.Start();
            _logger.LogInformation("Fesl listening on {address}:{port}", _arcadiaSettings.ListenAddress, listenerPort);
            _listeners.Add(listener);

            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(_cts.Token);
                var connection = Task.Run(async () => await HandleConnection(tcpClient, listenerPort), _cts.Token);
                _activeConnections.Add(connection);
            }
        }, _cts.Token);

        _servers.Add(serverFesl);
    }

    private async Task HandleConnection(TcpClient tcpClient, int connectionPort)
    {
        var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString()! ?? throw new NullReferenceException("ClientEndpoint cannot be null!");
        _logger.LogInformation("Opening connection from: {clientEndpoint} to {port}", clientEndpoint, connectionPort);

        using var scope = _scopeFactory.CreateAsyncScope();
        var networkStream = tcpClient.GetStream();

        var crypto = scope.ServiceProvider.GetRequiredService<Rc4TlsCrypto>();
        var connTls = new Ssl3TlsServer(crypto, _feslPubCert, _feslCertKey);
        var serverProtocol = new TlsServerProtocol(networkStream);
        serverProtocol.Accept(connTls);

        switch (connectionPort)
        {
            case (int)FeslGamePort.BeachPS3:
            case (int)FeslGamePort.RomePS3:
            case (int)FeslGamePort.RomePC:
            case (int)FeslGamePort.BadCompanyPS3:
                var clientHandler = scope.ServiceProvider.GetRequiredService<FeslClientHandler>();
                await clientHandler.HandleClientConnection(serverProtocol, clientEndpoint, (FeslGamePort)connectionPort);
                break;

            case (int)FeslServerPort.RomePC:
                var serverHandler = scope.ServiceProvider.GetRequiredService<FeslServerHandler>();
                await serverHandler.HandleClientConnection(serverProtocol, clientEndpoint);
                break;
        }

        tcpClient.Close();
        await networkStream.DisposeAsync();
        tcpClient.Dispose();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listeners.ToList().ForEach(x => x.Stop());
        return Task.CompletedTask;
    }
}