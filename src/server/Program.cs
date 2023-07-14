using System.Net.Sockets;
using Org.BouncyCastle.Tls;
using server;
using server.Fesl;
using server.Tls;
using server.Tls.Crypto;

var (feslCertKey, feslPubCert) = ProtoSslCertGenerator.GenerateVulnerableCert();
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

    var connCrypto = new Rc4TlsCrypto(true);
    var connTls = new Ssl3TlsServer(connCrypto, feslPubCert, feslCertKey);
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