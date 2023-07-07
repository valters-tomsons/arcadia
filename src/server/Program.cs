using System.Net.Sockets;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using server;

var secureRandom = new SecureRandom();
var crypto = new BcTlsCrypto(secureRandom);

var (feslCertKey, feslCert) = FeslCertGenerator.GenerateVulnerableCert(crypto);

const int tcpPort = 18800;
var tcpListener = new TcpListener(System.Net.IPAddress.Any, tcpPort);
tcpListener.Start();

Console.WriteLine($"Listening on tcp:{tcpPort}");

while(true)
{
    var tcpClient = await tcpListener.AcceptTcpClientAsync();
    Console.WriteLine("Connection incoming!");
    HandleClient(tcpClient);
}

void HandleClient(TcpClient tcpClient)
{
    using var networkStream = tcpClient.GetStream();

    var clientServer = new FeslTcpServer(crypto, feslCert, feslCertKey);
    var clientServerProtocol = new TlsServerProtocol(networkStream);

    try
    {
        clientServerProtocol.Accept(clientServer);
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failed to accept connection: {e.Message}");

        clientServer.Cancel();
        clientServerProtocol.Close();
        tcpClient.Close();

        return;
    }

    Console.WriteLine("Client handshake successful!");
}