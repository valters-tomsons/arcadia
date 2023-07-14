using System.Net.Sockets;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using server.Tls;

namespace server;

public static class TcpCertDump
{
    public static void FeslDumpPubCert(TlsCrypto crypto)
    {
        const string serverHost = "beach-ps3.fesl.ea.com";
        const int serverPort = Constants.Beach_FeslPort;

        using var tcpClient = new TcpClient(serverHost, serverPort);
        using var networkStream = tcpClient.GetStream();
        var tlsClientProtocol = new TlsClientProtocol(networkStream);

        tlsClientProtocol.Connect(new Ssl3TlsClient(crypto));
    }
}