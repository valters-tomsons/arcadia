using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Arcadia.Handlers;
using Arcadia.EA.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Services;

public class TheaterHostedService : IHostedService
{
    private readonly ILogger<TheaterHostedService> _logger;

    private readonly ArcadiaSettings _arcadiaSettings;
    private readonly ProxySettings _proxySettings;
    
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly TcpListener _tcpListener;
    private readonly ConcurrentBag<Task> _activeConnections = new();
    private readonly UdpClient _udpListener;

    private CancellationTokenSource _cts = null!;

    private Task? _tcpServer;

    public TheaterHostedService(ILogger<TheaterHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings,
        IOptions<ProxySettings> proxySettings, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _arcadiaSettings = arcadiaSettings.Value;
        _proxySettings = proxySettings.Value;

        var endpoint = new IPEndPoint(IPAddress.Parse(_arcadiaSettings.ListenAddress), _arcadiaSettings.TheaterPort);
        _tcpListener = new TcpListener(endpoint);
        _udpListener = new UdpClient(endpoint);
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

        if (_proxySettings.EnableProxy)
        {
            await HandleAsProxy(scope.ServiceProvider, tcpClient, _cts.Token);
            return;
        }

        var networkStream = tcpClient.GetStream();
        var handler = scope.ServiceProvider.GetRequiredService<TheaterHandler>();
        await handler.HandleClientConnection(networkStream, clientEndpoint);
    }

    private async Task HandleAsProxy(IServiceProvider sp, TcpClient client, CancellationToken ct)
    {
        var proxy = sp.GetRequiredService<TheaterProxy>();
        await proxy.StartProxy(client);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _tcpListener.Stop();

        return Task.CompletedTask;
    }
}