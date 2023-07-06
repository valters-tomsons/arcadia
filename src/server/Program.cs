using System.Net.Sockets;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using server;

var secureRandom = new SecureRandom();

var (feslCertKey, feslCert) = FeslCertGenerator.GenerateVulnerableCert(secureRandom);

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

    var clientServer = new ServerZero(feslCert, feslCertKey);
    var clientServerProtocol = new TlsServerProtocol(networkStream, secureRandom);

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