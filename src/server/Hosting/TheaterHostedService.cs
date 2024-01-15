using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Arcadia.EA.Ports;
using Arcadia.Handlers;
using Arcadia.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class TheaterHostedService(ILogger<TheaterHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings, IServiceScopeFactory scopeFactory) : IHostedService
{
    private readonly ILogger<TheaterHostedService> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ArcadiaSettings _arcadiaSettings = arcadiaSettings.Value;

    private readonly ConcurrentBag<TcpListener> _listeners = [];
    private readonly ConcurrentBag<Task> _activeConnections = [];
    private readonly ConcurrentBag<Task?> _servers = [];
    private CancellationTokenSource _cts = null!;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var listeningPorts = new int[] {
            (int)TheaterGamePort.BeachPS3,
            (int)TheaterGamePort.BadCompanyPS3,
            (int)TheaterGamePort.RomePS3,
            (int)TheaterServerPort.RomePC
        };

        foreach (var port in listeningPorts)
        {
            CreateTheaterPortListener(port);
        }

        return Task.CompletedTask;
    }

    private void CreateTheaterPortListener(int listenerPort)
    {
        var server = Task.Run(async () =>
        {
            var listener = new TcpListener(IPAddress.Parse(_arcadiaSettings.ListenAddress), listenerPort);
            listener.Start();
            _logger.LogInformation("Theater listening on {address}:{port}", _arcadiaSettings.ListenAddress, listenerPort);
            _listeners.Add(listener);

            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(_cts.Token);
                var connection = Task.Run(async () => await HandleConnection(tcpClient, listenerPort), _cts.Token);
                _activeConnections.Add(connection);
            }
        });

        _servers.Add(server);
    }

    private async Task HandleConnection(TcpClient tcpClient, int connectionPort)
    {
        var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString()! ?? throw new NullReferenceException("ClientEndpoint cannot be null!");
        _logger.LogInformation("Opening connection from: {clientEndpoint} to {port}", clientEndpoint, connectionPort);

        using var scope = _scopeFactory.CreateAsyncScope();

        var scopeData = scope.ServiceProvider.GetRequiredService<ConnectionLogScope>();
        scopeData.ClientEndpoint = clientEndpoint;

        using var logScope = _logger.BeginScope(scopeData);
        _logger.LogDebug("Creating new connectionId: {connId}", scopeData.ConnectionId);

        var networkStream = tcpClient.GetStream();

        switch (connectionPort)
        {
            case (int)TheaterGamePort.BeachPS3:
            case (int)TheaterGamePort.RomePS3:
            case (int)TheaterGamePort.BadCompanyPS3:
                var clientHandler = scope.ServiceProvider.GetRequiredService<TheaterClientHandler>();
                await clientHandler.HandleClientConnection(networkStream, clientEndpoint);
                break;

            case (int)TheaterServerPort.RomePC:
                var serverHandler = scope.ServiceProvider.GetRequiredService<TheaterServerHandler>();
                await serverHandler.HandleClientConnection(networkStream, clientEndpoint);
                break;
        }

        tcpClient.Close();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listeners.ToList().ForEach(x => x.Stop());
        return Task.CompletedTask;
    }
}