using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace server;

public class FeslTcpServer : DefaultTlsServer
{
    private readonly Certificate _serverCertificate;
    private readonly AsymmetricKeyParameter _serverPrivateKey;
    private readonly BcTlsCrypto _crypto;

    public FeslTcpServer(BcTlsCrypto crypto, Certificate serverCertificate, AsymmetricKeyParameter serverPrivateKey) : base(crypto)
    {
        _crypto = crypto;
        _serverCertificate = serverCertificate;
        _serverPrivateKey = serverPrivateKey;
    }

    public override ProtocolVersion GetServerVersion()
    {
        return ProtocolVersion.SSLv3;
    }

    protected override ProtocolVersion[] GetSupportedVersions()
    {
        return new ProtocolVersion[] { ProtocolVersion.SSLv3 };
    }

    public override int[] GetCipherSuites()
    {
        return new int[] { CipherSuite.TLS_RSA_WITH_RC4_128_SHA, CipherSuite.TLS_RSA_WITH_RC4_128_MD5};
    }

    protected override int[] GetSupportedCipherSuites()
    {
        return new int[] { CipherSuite.TLS_RSA_WITH_RC4_128_SHA, CipherSuite.TLS_RSA_WITH_RC4_128_MD5};
    }

    public override void NotifySecureRenegotiation(bool secureRenegotiation)
    {
        if (!secureRenegotiation)
        {
            secureRenegotiation = true;
        }

        base.NotifySecureRenegotiation(secureRenegotiation);
    }

    protected override TlsCredentialedDecryptor GetRsaEncryptionCredentials()
    {
        return new BcDefaultTlsCredentialedDecryptor(_crypto, _serverCertificate, _serverPrivateKey);
    }
}