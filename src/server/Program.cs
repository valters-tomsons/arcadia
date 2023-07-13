using System.Net.Sockets;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using server;

var secureRandom = new SecureRandom();
var certCrypto = new BcTlsCrypto(secureRandom);

var (feslCertKey, feslCert) = FeslCertGenerator.GenerateVulnerableCert(certCrypto);

var feslTcpPort = Constants.Beach_FeslPort;

var tcpListener = new TcpListener(System.Net.IPAddress.Any, feslTcpPort);
tcpListener.Start();

Console.WriteLine($"Listening on tcp:{feslTcpPort}");

var feslCrypto = new FeslTlsCrypto(true);

while(true)
{
    using var tcpClient = await tcpListener.AcceptTcpClientAsync();
    Console.WriteLine("Connection incoming!");
    HandleClient(tcpClient);
}

void HandleClient(TcpClient tcpClient)
{
    using var networkStream = tcpClient.GetStream();

    var clientServer = new FeslTcpServer(feslCrypto, feslCert, feslCertKey);
    var clientServerProtocol = new TlsServerProtocol(networkStream);

    try
    {
        clientServerProtocol.Accept(clientServer);
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failed to accept connection: {e.Message}");

        clientServerProtocol.Flush();
        clientServerProtocol.Close();
        clientServer.Cancel();

        return;
    }

    Console.WriteLine("SSL Handshake successful!");
    Console.WriteLine("Didn't expect to get this far!");
    Console.WriteLine("What do I do now?");
}