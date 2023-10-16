using System.Collections.Concurrent;
using System.Net.Sockets;
using Arcadia.EA;
using Arcadia.Handlers;
using Arcadia.Tls;
using Arcadia.Tls.Crypto;
using Arcadia.EA.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Arcadia.Tls.Misc;

namespace Arcadia.Services;

public class FeslHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<FeslSettings> _feslSettings;
    private readonly IOptions<ProxySettings> _proxySettings;
    private readonly ILogger<FeslHostedService> _logger;
    private readonly CertGenerator _certGenerator;

    private readonly ConcurrentBag<Task> _activeConnections = new();
    private readonly TcpListener _listener;

    private CancellationTokenSource _cts = null!;
    private AsymmetricKeyParameter _feslCertKey = null!;
    private Certificate _feslPubCert = null!;

    private Task? _server;

    public FeslHostedService(IOptions<FeslSettings> proxySettings, ILogger<FeslHostedService> logger, IOptions<ProxySettings> proxy, CertGenerator certGenerator, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _feslSettings = proxySettings;
        _certGenerator = certGenerator;
        _proxySettings = proxy;

        _listener = new TcpListener(System.Net.IPAddress.Any, _feslSettings.Value.ServerPort);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        PrepareCertificate();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener.Start();

        if (_proxySettings.Value.EnableProxy)
        {
            _logger.LogWarning("Proxying enabled!");
        }

        _server = Task.Run(async () =>
        {
            _logger.LogInformation("Fesl listening on port tcp:{port}", _feslSettings.Value.ServerPort);

            while (!_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token);
                var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;

                _logger.LogInformation("Opening connection from: {clientEndpoint}", clientEndpoint);

                var connection = Task.Run(async () => await HandleClient(tcpClient, clientEndpoint), _cts.Token);
                _activeConnections.Add(connection);
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    private void PrepareCertificate()
    {
        var IssuerDN = "CN=OTG3 Certificate Authority, C=US, ST=California, L=Redwood City, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, emailAddress=dirtysock-contact@ea.com";
        var SubjectDN = "C=US, ST=California, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, CN=fesl.ea.com, emailAddress=fesl@ea.com";

        if (_proxySettings.Value.MirrorCertificateStrings && _proxySettings.Value.EnableProxy)
        {
            var feslSettings = _feslSettings.Value;
            (IssuerDN, SubjectDN) = TlsCertDumper.DumpPubFeslCert(feslSettings.ServerAddress, feslSettings.ServerPort);
            _logger.LogWarning("Certificate strings copied from upstream: {address}", feslSettings.ServerAddress);
        }

        (_feslCertKey, _feslPubCert) = _certGenerator.GenerateVulnerableCert(IssuerDN, SubjectDN);
    }

    private async Task HandleClient(TcpClient tcpClient, string clientEndpoint)
    {
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

        if (_proxySettings.Value.EnableProxy)
        {
            await HandleAsProxy(scope.ServiceProvider, serverProtocol, crypto);
            return;
        }

        var handler = scope.ServiceProvider.GetRequiredService<FeslHandler>();
        await handler.HandleClientConnection(serverProtocol, clientEndpoint);
    }

    private async Task HandleAsProxy(IServiceProvider sp, TlsServerProtocol protocol, Rc4TlsCrypto crypto)
    {
        var proxy = sp.GetRequiredService<FeslProxy>();
        await proxy.StartProxy(protocol, crypto);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listener.Stop();

        return Task.CompletedTask;
    }
}