using System.Net.Sockets;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;

namespace server;

public static class TcpCertDump
{
    public static void DumpFESL(TlsCrypto crypto)
    {
        const string serverHost = "beach-ps3.fesl.ea.com";
        const int serverPort = Constants.Beach_FeslPort;

        using var tcpClient = new TcpClient(serverHost, serverPort);
        using var networkStream = tcpClient.GetStream();
        var tlsClientProtocol = new TlsClientProtocol(networkStream);

        tlsClientProtocol.Connect(new DumperTlsClient(crypto));
    }
}

class DumperTlsClient : DefaultTlsClient
{
    public DumperTlsClient(TlsCrypto crypto) : base(crypto)
    {
    }

    public override ProtocolVersion[] GetProtocolVersions()
    {
        return new[] { ProtocolVersion.SSLv3 };
    }

    protected override ProtocolVersion[] GetSupportedVersions()
    {
        return new[] { ProtocolVersion.SSLv3 };
    }

    public override int[] GetCipherSuites()
    {
        return new[] { CipherSuite.TLS_RSA_WITH_RC4_128_SHA, CipherSuite.TLS_RSA_WITH_RC4_128_SHA };
    }

    protected override int[] GetSupportedCipherSuites()
    {
        return new[] { CipherSuite.TLS_RSA_WITH_RC4_128_SHA, CipherSuite.TLS_RSA_WITH_RC4_128_SHA };
    }

    public override TlsAuthentication GetAuthentication()
    {
        return new ServerCertificateDump();
    }
}

public class ServerCertificateDump : TlsAuthentication
{
    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        throw new NotImplementedException();
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        var certificates = serverCertificate.Certificate.GetCertificateList();

        for(var i = 0; i < certificates.Length; i++)
        {
            var certificate = certificates[i];
            var certificateFilePath = $"server_certificate_{i}.crt";
            File.WriteAllBytes(certificateFilePath, certificate.GetEncoded());
            Console.WriteLine($"Wrote certificate to {certificateFilePath}");
        }
    }
}