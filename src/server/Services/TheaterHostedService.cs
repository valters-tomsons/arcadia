using System.Collections.Concurrent;
using System.Net.Sockets;
using Arcadia.Theater;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Tls;

namespace Arcadia.Services;

public class TheaterHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ArcadiaSettings> _settings;
    private readonly ILogger<FeslHostedService> _logger;

    private readonly ConcurrentBag<Task> _activeConnections = new();
    private readonly TcpListener _listener;

    private CancellationTokenSource _cts = null!;

    private Task? _server;

    public TheaterHostedService(IOptions<ArcadiaSettings> settings, ILogger<FeslHostedService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _settings = settings;

        _listener = new TcpListener(System.Net.IPAddress.Any, _settings.Value.TheaterPort);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener.Start();
        _logger.LogInformation("Theater listening on port tcp:{port}", _settings.Value.TheaterPort);

        _server = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token);
                var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;

                _logger.LogInformation("Opening connection from: {clientEndpoint}", clientEndpoint);

                var connection = Task.Run(async () => await HandleConnection(tcpClient, clientEndpoint), _cts.Token);
                _activeConnections.Add(connection);
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    private async Task HandleConnection(TcpClient tcpClient, string clientEndpoint)
    {
        using var scope = _scopeFactory.CreateAsyncScope();
        var networkStream = tcpClient.GetStream();

        var serverProtocol = new TlsServerProtocol(networkStream);

        var handler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
        await handler.HandleClientConnection(serverProtocol, clientEndpoint);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listener.Stop();

        await Task.Delay(200, cancellationToken);

        if (_server is not null || _server!.IsCompleted)
        {
            _logger.LogCritical("Waiting for connections to close...");
            await _server.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}