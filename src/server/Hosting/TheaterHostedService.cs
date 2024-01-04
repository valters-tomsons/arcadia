using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Arcadia.EA.Constants;
using Arcadia.Handlers;
using Arcadia.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class TheaterHostedService : IHostedService
{
    private readonly ILogger<TheaterHostedService> _logger;

    private readonly ArcadiaSettings _arcadiaSettings;
    
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ConcurrentBag<TcpListener> _listeners = new();
    private readonly ConcurrentBag<Task> _activeConnections = new();

    private CancellationTokenSource _cts = null!;

    private ConcurrentBag<Task?> _servers = new();

    public TheaterHostedService(ILogger<TheaterHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _arcadiaSettings = arcadiaSettings.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var listeningPorts = new int[] {
            Beach.TheaterPort,
            BadCompany.TheaterPort,
            Rome.TheaterClientPortPs3,
            Rome.TheaterServerPortPc
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
        _logger.LogInformation("Opening connection from: {clientEndpoint}", clientEndpoint);

        using var scope = _scopeFactory.CreateAsyncScope();

        var scopeData = scope.ServiceProvider.GetRequiredService<ConnectionLogScope>();
        scopeData.ClientEndpoint = clientEndpoint;

        using var logScope = _logger.BeginScope(scopeData);
        _logger.LogDebug("Creating new connectionId: {connId}", scopeData.ConnectionId);

        var networkStream = tcpClient.GetStream();

        switch (connectionPort)
        {
            case Beach.TheaterPort:
            case BadCompany.TheaterPort:
            case Rome.TheaterClientPortPs3:
                var clientHandler = scope.ServiceProvider.GetRequiredService<TheaterClientHandler>();
                await clientHandler.HandleClientConnection(networkStream, clientEndpoint);
                break;
            case Rome.TheaterServerPortPc:
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