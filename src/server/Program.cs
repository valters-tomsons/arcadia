using System.Net.Sockets;
using System.Text;
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

while(true)
{
    using var tcpClient = await arcadiaTcpListener.AcceptTcpClientAsync();
    var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;
    Console.WriteLine($"Opening connection from: {clientEndpoint}");

    // TODO: Run this in a separate thread.
    HandleClientConnection(tcpClient, clientEndpoint);
}

void HandleClientConnection(TcpClient tcpClient, string clientEndpoint)
{
    using var networkStream = tcpClient.GetStream();

    var connTls = new Ssl3TlsServer(arcadiaTlsCrypto, feslPubCert, feslCertKey);
    var connProtocol = new TlsServerProtocol(networkStream);

    try
    {
        connProtocol.Accept(connTls);
        Console.WriteLine("SSL Handshake with game client successful!");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failed to accept connection: {e.Message}");

        connProtocol.Flush();
        connProtocol.Close();
        connTls.Cancel();

        return;
    }

    var readBuffer = new byte[4096];
    while (connProtocol.IsConnected)
    {
        var read = 0;

        try
        {
            read = connProtocol.ReadApplicationData(readBuffer, 0, readBuffer.Length);
        }
        catch
        {
            Console.WriteLine($"Connection has been closed with {clientEndpoint}");
            break;
        }

        if (read == 0)
        {
            continue;
        }

        Console.WriteLine($"Received {read} bytes from client.");
        Console.WriteLine(Encoding.ASCII.GetString(readBuffer, 0, read));

        var packet = new FeslPacket(readBuffer[..read]);
        Console.WriteLine($"Type: {packet.Type}");
    }
}