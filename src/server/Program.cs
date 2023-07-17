using System.Net.Sockets;
using Org.BouncyCastle.Tls;
using Arcadia;
using Arcadia.Fesl;
using Arcadia.Tls;
using Arcadia.Tls.Crypto;
using Arcadia.Tls.Misc;

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

var arcadiaTcpListener = new TcpListener(System.Net.IPAddress.Any, config.Port);
var arcadiaTlsCrypto = new Rc4TlsCrypto(true);

arcadiaTcpListener.Start();
Console.WriteLine($"Listening on tcp:{config.Port}");

if (config.EnableProxyMode)
{
    Console.WriteLine("Proxy mode enabled!");
}

while(true)
{
    var tcpClient = await arcadiaTcpListener.AcceptTcpClientAsync();
    var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;
    var hostIp = ((System.Net.IPEndPoint)tcpClient.Client.LocalEndPoint!).Address.ToString();
    Console.WriteLine($"Opening connection from: {clientEndpoint}");

    // TODO: Run this in a separate thread.
    HandleClientConnection(tcpClient, clientEndpoint, hostIp);
}

async void HandleClientConnection(TcpClient tcpClient, string clientEndpoint, string serverIp)
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
    var serverHandler = new ArcadiaFesl(arcadiaServerProtocol, clientEndpoint, serverIp);
    serverHandler.HandleClientConnection();
}