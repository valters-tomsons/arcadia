using System.Collections.Concurrent;
using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.EA.Constants;
using Arcadia.Handlers;
using Arcadia.Tls;
using Arcadia.Tls.Crypto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;

namespace Arcadia.Services;

public class FeslHostedService : IHostedService
{
    private readonly ILogger<FeslHostedService> _logger;
    
    private readonly ArcadiaSettings _arcadiaSettings;
    private readonly FeslSettings _feslSettings;

    private readonly CertGenerator _certGenerator;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ConcurrentBag<TcpListener> _listeners = new();
    private readonly ConcurrentBag<Task> _activeConnections = new();

    private CancellationTokenSource _cts = null!;
    private AsymmetricKeyParameter _feslCertKey = null!;
    private Certificate _feslPubCert = null!;

    private ConcurrentBag<Task?> _servers = new();

    public FeslHostedService(ILogger<FeslHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings,
        IOptions<FeslSettings> feslSettings, CertGenerator certGenerator, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        
        _arcadiaSettings = arcadiaSettings.Value;
        _feslSettings = feslSettings.Value;

        _certGenerator = certGenerator;
        _scopeFactory = scopeFactory;

    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        PrepareCertificate();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        CreateFeslPortListener(_feslSettings.ServerPort);
        CreateFeslPortListener(Rome.FeslServerPortPc);

        return Task.CompletedTask;
    }

    private void PrepareCertificate()
    {
        var IssuerDN = "CN=OTG3 Certificate Authority, C=US, ST=California, L=Redwood City, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, emailAddress=dirtysock-contact@ea.com";
        var SubjectDN = "C=US, ST=California, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, CN=fesl.ea.com, emailAddress=fesl@ea.com";

        (_feslCertKey, _feslPubCert) = _certGenerator.GenerateVulnerableCert(IssuerDN, SubjectDN);
    }

    private void CreateFeslPortListener(int listenerPort)
    {
        var serverFesl = Task.Run(async () =>
        {
            var listener = new TcpListener(System.Net.IPAddress.Parse(_arcadiaSettings.ListenAddress), listenerPort);
            listener.Start();
            _logger.LogInformation("Fesl listening on {address}:{port}", _arcadiaSettings.ListenAddress, listenerPort);
            _listeners.Add(listener);

            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(_cts.Token);
                var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;

                _logger.LogInformation("Opening connection from: {clientEndpoint}", clientEndpoint);

                var connection = Task.Run(async () => await HandleClient(tcpClient, clientEndpoint), _cts.Token);
                _activeConnections.Add(connection);
            }
        }, _cts.Token);
        _servers.Add(serverFesl);
    }

    private async Task HandleClient(TcpClient tcpClient, string clientEndpoint)
    {
        var connectionId = Guid.NewGuid().ToString();
        using var logScope = _logger.BeginScope(connectionId);
        _logger.LogDebug("Creating new connectionId: {connId}", connectionId);

        using var scope = _scopeFactory.CreateAsyncScope();
        var crypto = scope.ServiceProvider.GetRequiredService<Rc4TlsCrypto>();

        var networkStream = tcpClient.GetStream();

        var connTls = new Ssl3TlsServer(crypto, _feslPubCert, _feslCertKey);
        var serverProtocol = new TlsServerProtocol(networkStream);

        try
        {
            serverProtocol.Accept(connTls);
        }
        catch (Exception e)
        {
            _logger.LogError("Failed to accept connection: {message}", e.Message);

            serverProtocol.Flush();
            serverProtocol.Close();
            connTls.Cancel();

            return;
        }

        var handler = scope.ServiceProvider.GetRequiredService<FeslHandler>();
        await handler.HandleClientConnection(serverProtocol, clientEndpoint);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listeners.ToList().ForEach(x => x.Stop());
        return Task.CompletedTask;
    }
}