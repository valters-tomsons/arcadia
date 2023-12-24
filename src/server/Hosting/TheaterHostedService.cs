using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Arcadia.EA;
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

    private readonly TcpListener _tcpListener;
    private readonly ConcurrentBag<Task> _activeConnections = new();

    private CancellationTokenSource _cts = null!;

#pragma warning disable IDE0052 // Remove unread private members
    private Task? _tcpServer;
#pragma warning restore IDE0052 // Remove unread private members

    public TheaterHostedService(ILogger<TheaterHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _arcadiaSettings = arcadiaSettings.Value;

        var endpoint = new IPEndPoint(IPAddress.Parse(_arcadiaSettings.ListenAddress), _arcadiaSettings.TheaterPort);
        _tcpListener = new TcpListener(endpoint);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _tcpListener.Start();
        _tcpServer = Task.Run(async () =>
        {
            _logger.LogInformation("Theater listening on {address}:{port}", _arcadiaSettings.ListenAddress, _arcadiaSettings.TheaterPort);

            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await _tcpListener.AcceptTcpClientAsync(_cts.Token);
                var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;

                _logger.LogInformation("Opening TCP connection from: {clientEndpoint}", clientEndpoint);

                var connection = Task.Run(async () => await HandleClient(tcpClient, clientEndpoint), _cts.Token);
                _activeConnections.Add(connection);
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    private async Task HandleClient(TcpClient tcpClient, string clientEndpoint)
    {
        using var scope = _scopeFactory.CreateAsyncScope();

        var scopeData = scope.ServiceProvider.GetRequiredService<ConnectionLogScope>();
        scopeData.ClientEndpoint = clientEndpoint;

        using var logScope = _logger.BeginScope(scopeData);
        _logger.LogDebug("Creating new connectionId: {connId}", scopeData.ConnectionId);

        var networkStream = tcpClient.GetStream();
        var handler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
        await handler.HandleClientConnection(networkStream, clientEndpoint);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _tcpListener.Stop();

        return Task.CompletedTask;
    }
}