using System.Net.Sockets;
using System.Text;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Arcadia.Tls.Misc;

public class TlsClientProxy
{
    private readonly TlsServerProtocol _arcadiaProtocol;
    private TlsClientProtocol? _upstreamProtocol;
    private readonly BcTlsCrypto _crypto;

    public TlsClientProxy(TlsServerProtocol arcadiaProtocol, BcTlsCrypto crypto)
    {
        _arcadiaProtocol = arcadiaProtocol;
        _crypto = crypto;
    }

    public async Task StartProxy(AppSettings config)
    {
        InitializeUpstreamClient(config);
        await StartProxying();
        Console.WriteLine("Proxy closed, closing connections");
    }

    private void InitializeUpstreamClient(AppSettings config)
    {
        Console.WriteLine($"Connecting to upstream {config.UpstreamHost}:{config.Port}");

        var upstreamTcpClient = new TcpClient(config.UpstreamHost, config.Port);
        var upstreamTcpStream = upstreamTcpClient.GetStream();
        _upstreamProtocol = new TlsClientProtocol(upstreamTcpStream);

        var proxyTlsAuth = new ProxyTlsAuthentication();
        var upstreamClient = new Ssl3TlsClient(_crypto, proxyTlsAuth);

        try
        {
            _upstreamProtocol.Connect(upstreamClient);
            Console.WriteLine("SSL Handshake with upstream successful!");
        }
        catch(Exception e)
        {
            Console.WriteLine(e.Message);
            throw new Exception($"Failed to connect to upstream {config.UpstreamHost}:{config.Port}");
        }
    }

    private async Task StartProxying()
    {
        var clientToFeslTask = Task.Run(() =>
        {
            while (_arcadiaProtocol.IsConnected)
            {
                ForwardClientToFesl();
            }
        });

        var feslToClientTask = Task.Run(() =>
        {
            while(_upstreamProtocol?.IsConnected == true)
            {
                ForwardFeslToClient();
            }
        });

        await Task.WhenAll(clientToFeslTask, feslToClientTask);
        Console.WriteLine("Proxy connection closed, exiting...");
    }

    private void ForwardClientToFesl()
    {
        var readBuffer = new byte[1028];
        int read = 0;

        while (read == 0)
        {
            read = _arcadiaProtocol.ReadApplicationData(readBuffer, 0, readBuffer.Length);

            if (read < 5)
            {
                continue;
            }

            Console.WriteLine($"Received {read} bytes from client:");
            Console.WriteLine(Encoding.ASCII.GetString(readBuffer, 0, read));

            _upstreamProtocol?.WriteApplicationData(readBuffer, 0, read);
            _upstreamProtocol?.Flush();

            Console.WriteLine("Forwarded to FESL");
        }
    }

    private void ForwardFeslToClient()
    {
        var readBuffer = new byte[1028];
        int read = 0;

        while (read == 0)
        {
            read = _upstreamProtocol.ReadApplicationData(readBuffer, 0, readBuffer.Length);

            if (read < 5)
            {
                continue;
            }

            Console.WriteLine($"Received {read} bytes from server:");
            Console.WriteLine(Encoding.ASCII.GetString(readBuffer, 0, read));

            _arcadiaProtocol.WriteApplicationData(readBuffer, 0, read);
            _arcadiaProtocol.Flush();

            Console.WriteLine("Forwarded to client");
        }
    }
}