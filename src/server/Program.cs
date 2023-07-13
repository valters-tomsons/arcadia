using System.Net.Sockets;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using server;

var certGenCrypto = new BcTlsCrypto(new SecureRandom());
var (feslCertKey, feslPubCert) = FeslCertGenerator.GenerateVulnerableCert(certGenCrypto);

var feslTcpListener = new TcpListener(System.Net.IPAddress.Any, Constants.Beach_FeslPort);

feslTcpListener.Start();
Console.WriteLine($"Listening on tcp:{Constants.Beach_FeslPort}");

while(true)
{
    using var tcpClient = await feslTcpListener.AcceptTcpClientAsync();
    Console.WriteLine($"Opening connection from: {tcpClient.Client.RemoteEndPoint}");
    HandleClientConnection(tcpClient);
}

async void HandleClientConnection(TcpClient tcpClient)
{
    using var networkStream = tcpClient.GetStream();

    var connCrypto = new FeslTlsCrypto(true);
    var connTcp = new FeslTcpServer(connCrypto, feslPubCert, feslCertKey);
    var connProtocol = new TlsServerProtocol(networkStream);

    try
    {
        connProtocol.Accept(connTcp);
        Console.WriteLine("SSL Handshake successful!");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failed to accept connection: {e.Message}");

        connProtocol.Flush();
        connProtocol.Close();
        connTcp.Cancel();

        return;
    }

    Console.WriteLine("Terminating connection in 2 seconds...");
    await Task.Delay(2000);
    Console.WriteLine("Terminating...");
}