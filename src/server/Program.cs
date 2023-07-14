using System.Net.Sockets;
using Org.BouncyCastle.Tls;
using server;
using server.Fesl;
using server.Tls;
using server.Tls.Crypto;

var config = Utils.BuildConfig();

var IssuerDN = string.Empty;
var SubjectDN = string.Empty;
if (config.MirrorUpstreamCert)
{
    Console.WriteLine($"Upstream cert mirroring enabled: {config.UpstreamHost}");
    (IssuerDN, SubjectDN) = TlsCertDump.DumpPubFeslCert(config.UpstreamHost);
}

var (feslCertKey, feslPubCert) = ProtoSslCertGenerator.GenerateVulnerableCert(IssuerDN, SubjectDN);
if(config.DumpPatchedCert)
{
    Console.WriteLine("Dumping patched certificate...");
    Utils.DumpCertificate(feslCertKey, feslPubCert, config.UpstreamHost);
}

var feslTcpListener = new TcpListener(System.Net.IPAddress.Any, config.Port);
var feslCrypto = new Rc4TlsCrypto(true);

feslTcpListener.Start();
Console.WriteLine($"Listening on tcp:{config.Port}");

while(true)
{
    using var tcpClient = await feslTcpListener.AcceptTcpClientAsync();
    Console.WriteLine($"Opening connection from: {tcpClient.Client.RemoteEndPoint}");

    // TODO: Run this in a separate thread.
    HandleClientConnection(tcpClient);
}

async void HandleClientConnection(TcpClient tcpClient)
{
    using var networkStream = tcpClient.GetStream();

    var connTls = new Ssl3TlsServer(feslCrypto, feslPubCert, feslCertKey);
    var connProtocol = new TlsServerProtocol(networkStream);

    try
    {
        connProtocol.Accept(connTls);
        Console.WriteLine("SSL Handshake successful!");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failed to accept connection: {e.Message}");

        connProtocol.Flush();
        connProtocol.Close();
        connTls.Cancel();

        return;
    }

    Console.WriteLine("Terminating connection in 2 seconds...");
    await Task.Delay(2000);
    Console.WriteLine("Terminating...");
}