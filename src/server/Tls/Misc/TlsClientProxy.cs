using System.Net.Sockets;
using Arcadia.EA;
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
        await StartProxying(proxyConfig);
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

    private async Task StartProxying(FeslSettings proxyConfig)
    {
        var clientToFeslTask = Task.Run(() =>
        {
            try
            {
                while (_arcadiaProtocol.IsConnected)
                {
                    ProxyApplicationData(_arcadiaProtocol, _upstreamProtocol!, proxyConfig);
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
                    ProxyApplicationData(_upstreamProtocol!, _arcadiaProtocol, proxyConfig);
                }
            }
            catch { }
            return Task.CompletedTask;
        });

        await Task.WhenAll(clientToFeslTask, feslToClientTask);
        Console.WriteLine("Proxy connection closed, exiting...");
    }

    private static async void ProxyApplicationData(TlsProtocol source, TlsProtocol destination, FeslSettings proxyConfig)
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

            if (!string.IsNullOrWhiteSpace(proxyConfig.ProxyOverrideClientTicket) || proxyConfig.LogPacketAnalysis)
            {
                var feslPacket = AnalyzeFeslPacket(readBuffer);

                if (proxyConfig.LogPacketAnalysis)
                {
                    var dataString = feslPacket?.DataDict.Select(x => $"{x.Key}={x.Value}").Aggregate((x, y) => $"{x}; {y}");
                    Console.WriteLine($"Proxying packet '{feslPacket?.Type}' with TXN={feslPacket?["TXN"]}; Data={dataString}");
                }

                if (feslPacket != null && feslPacket?.Type == "acct" && feslPacket?["TXN"] == "NuPS3Login")
                {
                    var packet = feslPacket.Value;
                    var clientTicket = packet["ticket"];

                    if (proxyConfig.LogPacketAnalysis && !string.IsNullOrWhiteSpace(clientTicket))
                    {
                        Console.WriteLine($"Client ticket={clientTicket}");
                    }

                    if (!string.IsNullOrWhiteSpace(clientTicket) && !string.IsNullOrWhiteSpace(proxyConfig.ProxyOverrideClientTicket))
                    {
                        Console.WriteLine($"Overriding client ticket!");
                        packet["ticket"] = proxyConfig.ProxyOverrideClientTicket;

                        readBuffer = await packet.Serialize(packet.Id);
                        read = readBuffer.Length;
                    }
                }
            }

            destination.WriteApplicationData(readBuffer, 0, read.Value);
        }
    }

    private static Packet? AnalyzeFeslPacket(byte[] buffer)
    {
        var packet = new Packet(buffer);
        if (!packet.DataDict.TryGetValue("TXN", out var txnObj) || txnObj == null)
        {
            return null;
        }

        var txn = txnObj as string;
        if (string.IsNullOrWhiteSpace(txn)) return null;

        return packet;
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