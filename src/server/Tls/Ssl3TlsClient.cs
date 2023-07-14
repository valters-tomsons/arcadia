using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;

namespace server.Tls;

public class Ssl3TlsClient : DefaultTlsClient
{
    private readonly TlsAuthentication _tlsAuth;

    public Ssl3TlsClient(TlsCrypto crypto, TlsAuthentication auth) : base(crypto)
    {
        _tlsAuth = auth;
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
        return _tlsAuth;
    }
}

public class TlsAuthDumper : TlsAuthentication
{
    public TlsCertificate[]? ServerCertificates { get; private set; }

    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        throw new NotImplementedException();
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        ServerCertificates = serverCertificate.Certificate.GetCertificateList();
    }
}