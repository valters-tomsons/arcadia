using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;

namespace server.Tls;

public class Ssl3TlsClient : DefaultTlsClient
{
    public Ssl3TlsClient(TlsCrypto crypto) : base(crypto)
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
        return new TlsAuthDumper();
    }
}

public class TlsAuthDumper : TlsAuthentication
{
    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        throw new NotImplementedException();
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        var certificates = serverCertificate.Certificate.GetCertificateList();

        for (var i = 0; i < certificates.Length; i++)
        {
            var certificate = certificates[i];
            var certificateFilePath = $"server_certificate_{i}.crt";
            File.WriteAllBytes(certificateFilePath, certificate.GetEncoded());
            Console.WriteLine($"Wrote certificate to {certificateFilePath}");
        }
    }
}