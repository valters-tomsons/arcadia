using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Arcadia.Tls;

public class Ssl3TlsClient : DefaultTlsClient
{
    private readonly TlsAuthentication _tlsAuth;

    public Ssl3TlsClient(BcTlsCrypto crypto, TlsAuthentication auth) : base(crypto)
    {
        _tlsAuth = auth;
    }

    private static readonly int[] _cipherSuites =
    {
        CipherSuite.TLS_RSA_WITH_RC4_128_SHA,
        CipherSuite.TLS_RSA_WITH_RC4_128_MD5
    };

    private static readonly ProtocolVersion[] _supportedVersions =
    {
        ProtocolVersion.SSLv3
    };

    public override ProtocolVersion[] GetProtocolVersions()
    {
        return _supportedVersions;
    }

    protected override ProtocolVersion[] GetSupportedVersions()
    {
        return _supportedVersions;
    }

    public override int[] GetCipherSuites()
    {
        return _cipherSuites;
    }

    protected override int[] GetSupportedCipherSuites()
    {
        return _cipherSuites;
    }

    public override TlsAuthentication GetAuthentication()
    {
        return _tlsAuth;
    }

    public override void NotifySecureRenegotiation(bool secureRenegotiation)
    {
        if (!secureRenegotiation)
        {
            secureRenegotiation = true;
        }

        base.NotifySecureRenegotiation(secureRenegotiation);
    }
}