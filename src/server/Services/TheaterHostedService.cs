using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using Arcadia.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Services;

public class TheaterHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ArcadiaSettings> _settings;
    private readonly ILogger<TheaterHostedService> _logger;

    private readonly ConcurrentBag<Task> _activeConnections = new();
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udpListener;

    private CancellationTokenSource _cts = null!;

    private Task? _tcpServer;
    private Task? _udpServer;

    public TheaterHostedService(IOptions<ArcadiaSettings> settings, ILogger<TheaterHostedService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _settings = settings;

        _tcpListener = new TcpListener(System.Net.IPAddress.Any, _settings.Value.TheaterPort);
        _udpListener = new UdpClient(_settings.Value.TheaterPort);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _tcpListener.Start();
        _tcpServer = Task.Run(async () =>
        {
            _logger.LogInformation("Theater listening on port tcp:{port}", _settings.Value.TheaterPort);

            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await _tcpListener.AcceptTcpClientAsync(_cts.Token);
                var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;

                _logger.LogInformation("Opening TCP connection from: {clientEndpoint}", clientEndpoint);

                var connection = Task.Run(async () => await HandleConnection(tcpClient, clientEndpoint), _cts.Token);
                _activeConnections.Add(connection);
            }
        }, _cts.Token);

        _udpServer = Task.Run(async () =>
        {
            _logger.LogInformation("Theater listening on port udp:{port}", _settings.Value.TheaterPort);

            while (!_cts.Token.IsCancellationRequested)
            {
                var udpClient = await _udpListener.ReceiveAsync(_cts.Token);
                _logger.LogInformation("UDP: {data}", Encoding.ASCII.GetString(udpClient.Buffer));
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    private async Task HandleConnection(TcpClient tcpClient, string clientEndpoint)
    {
        using var scope = _scopeFactory.CreateAsyncScope();
        var networkStream = tcpClient.GetStream();

        var handler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
        await handler.HandleClientConnection(networkStream, clientEndpoint);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _tcpListener.Stop();

        await Task.Delay(200, cancellationToken);

        if (_tcpServer is not null || _tcpServer!.IsCompleted)
        {
            _logger.LogCritical("Waiting for connections to close...");
            await _tcpServer.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}