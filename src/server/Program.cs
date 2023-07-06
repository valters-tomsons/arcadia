using System.Net.Sockets;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using server;

const int tcpPort = 18800;

var secureRandom = new SecureRandom();

var serverZero = new TcpListener(System.Net.IPAddress.Any, tcpPort);
serverZero.Start();

Console.WriteLine($"Listening on tcp:{tcpPort}");

while(true)
{
    var tcpClient = await serverZero.AcceptTcpClientAsync();
    Console.WriteLine("A client connected!");
    Task.Run(() => HandleClient(tcpClient));
}

void HandleClient(TcpClient tcpClient)
{
    using var networkStream = tcpClient.GetStream();

    var clientServerProtocol = new TlsServerProtocol(networkStream, secureRandom);
    var clientServer = new ServerZero();

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

    Console.WriteLine("Client accepted!");
}