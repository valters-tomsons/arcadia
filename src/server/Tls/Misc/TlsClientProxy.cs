using System.Net.Sockets;
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

    public async Task StartProxy(ArcadiaSettings config, FeslSettings proxyConfig)
    {
        InitializeUpstreamClient(config, proxyConfig);
        await StartProxying();
    }

    private void InitializeUpstreamClient(ArcadiaSettings config, FeslSettings proxyConfig)
    {
        Console.WriteLine($"Connecting to upstream {proxyConfig.ServerAddress}:{proxyConfig.ServerPort}");

        var upstreamTcpClient = new TcpClient(proxyConfig.ServerAddress, proxyConfig.ServerPort);
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
            throw new Exception($"Failed to connect to upstream {proxyConfig.ServerAddress}:{proxyConfig.ServerPort}");
        }
    }

    private async Task StartProxying()
    {
        var clientToFeslTask = Task.Run(() =>
        {
            try
            {
                while (_arcadiaProtocol.IsConnected)
                {
                    ProxyApplicationData(_arcadiaProtocol, _upstreamProtocol!);
                }
            }
            catch { }
            return Task.CompletedTask;
        });

        var feslToClientTask = Task.Run(() =>
        {
            try
            {
                while (_arcadiaProtocol.IsConnected)
                {
                    ProxyApplicationData(_upstreamProtocol!, _arcadiaProtocol);
                }
            }
            catch { }
            return Task.CompletedTask;
        });

        await Task.WhenAll(clientToFeslTask, feslToClientTask);
        Console.WriteLine("Proxy connection closed, exiting...");
    }

    private static void ProxyApplicationData(TlsProtocol source, TlsProtocol destination)
    {
        var readBuffer = new byte[1514];
        int? read = 0;

        while (read == 0)
        {
            read = source.ReadApplicationData(readBuffer, 0, readBuffer.Length);
            if (read < 1)
            {
                continue;
            }

            Console.WriteLine($"Received {read} bytes from server");

            destination.WriteApplicationData(readBuffer, 0, read.Value);
        }
    }
}

public class ProxyTlsAuthentication : TlsAuthentication
{
    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        throw new NotImplementedException();
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        Console.WriteLine("Ignoring server certificate...");
    }
}