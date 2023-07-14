using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Tls;
using server;
using server.Fesl;
using server.Tls;
using server.Tls.Crypto;

var config = BuildConfig();

var (feslCertKey, feslPubCert) = ProtoSslCertGenerator.GenerateVulnerableCert();
var feslTcpListener = new TcpListener(System.Net.IPAddress.Any, config.Port);

feslTcpListener.Start();
Console.WriteLine($"Listening on tcp:{config.Port}");

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

static AppSettings BuildConfig()
{
    var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

    var config = builder.Build();
    return config.GetSection(nameof(AppSettings)).Get<AppSettings>() ?? new AppSettings();
}