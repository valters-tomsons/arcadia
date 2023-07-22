using System.Net.Sockets;
using Org.BouncyCastle.Tls;
using Arcadia;
using Arcadia.Fesl;
using Arcadia.Tls;
using Arcadia.Tls.Crypto;
using Arcadia.Tls.Misc;
using System.Collections.Concurrent;
using Arcadia.Constants;
using Arcadia.Theater;

var config = Utils.BuildConfig();

var IssuerDN = string.Empty;
var SubjectDN = string.Empty;
if (config.MirrorUpstreamCert)
{
    Console.WriteLine($"Upstream cert mirroring enabled: {config.UpstreamHost}");
    (IssuerDN, SubjectDN) = TlsCertDumper.DumpPubFeslCert(config.UpstreamHost, config.Port);
}

var (feslCertKey, feslPubCert) = ProtoSslCertGenerator.GenerateVulnerableCert(IssuerDN, SubjectDN);
if(config.DumpPatchedCert)
{
    Console.WriteLine("Dumping patched certificate...");
    Utils.DumpCertificate(feslCertKey, feslPubCert, config.UpstreamHost);
}

var arcadiaFeslListener = new TcpListener(System.Net.IPAddress.Any, config.Port);
arcadiaFeslListener.Start();

var arcadiaTheaterListener = new TcpListener(System.Net.IPAddress.Any, Beach.TheaterPort);
arcadiaTheaterListener.Start();

var arcadiaTlsCrypto = new Rc4TlsCrypto(true);

if (config.EnableProxyMode)
{
    Console.WriteLine("Proxy mode enabled!");
}

var activeConnections = new ConcurrentBag<Task>();

var feslServer = Task.Run(async () => await StartFesl(arcadiaFeslListener));
var theaterServer = Task.Run(async () => await StartTheater(arcadiaTheaterListener));

await Task.WhenAll(feslServer, theaterServer);

//

async Task StartFesl(TcpListener listener)
{
    Console.WriteLine($"Listening on tcp:{listener.LocalEndpoint}");

    while (true)
    {
        var tcpClient = await listener.AcceptTcpClientAsync();
        var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;

        Console.WriteLine($"Opening connection from: {clientEndpoint}");
        var connection = Task.Run(async () => await HandleFeslConnection(tcpClient, clientEndpoint));
        activeConnections!.Add(connection);
    }
}

async Task StartTheater(TcpListener listener)
{
    Console.WriteLine($"Listening on tcp:{listener.LocalEndpoint}");

    while (true)
    {
        var tcpClient = await listener.AcceptTcpClientAsync();
        var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;

        Console.WriteLine($"Opening connection from: {clientEndpoint}");
        var connection = Task.Run(async () => await HandleTheaterConnection(tcpClient, clientEndpoint));
        activeConnections!.Add(connection);
    }
}

async Task HandleFeslConnection(TcpClient tcpClient, string clientEndpoint)
{
    var networkStream = tcpClient.GetStream();

    var connTls = new Ssl3TlsServer(arcadiaTlsCrypto, feslPubCert, feslCertKey);
    var arcadiaServerProtocol = new TlsServerProtocol(networkStream);

    try
    {
        arcadiaServerProtocol.Accept(connTls);
        Console.WriteLine("SSL Handshake with game client successful!");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failed to accept connection: {e.Message}");

        arcadiaServerProtocol.Flush();
        arcadiaServerProtocol.Close();
        connTls.Cancel();

        return;
    }

    if (config.EnableProxyMode)
    {
        var proxy = new TlsClientProxy(arcadiaServerProtocol, arcadiaTlsCrypto);
        await proxy.StartProxy(config);

        return;
    }

    Console.WriteLine("Starting arcadia-emu FESL session");
    var serverHandler = new ArcadiaFesl(arcadiaServerProtocol, clientEndpoint);
    await serverHandler.HandleClientConnection();
}

async Task HandleTheaterConnection(TcpClient tcpClient, string clientEndpoint)
{
    var networkStream = tcpClient.GetStream();
    var arcadiaServerProtocol = new TlsServerProtocol(networkStream);

    Console.WriteLine("Starting arcadia-emu Theater session");
    var serverHandler = new ArcadiaTheater(arcadiaServerProtocol, clientEndpoint);
    await serverHandler.HandleClientConnection();
}