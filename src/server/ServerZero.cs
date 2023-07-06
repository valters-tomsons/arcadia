using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Pkcs;

namespace server;

public class ServerZero : DefaultTlsServer
{
    private readonly Certificate _serverCertificate;
    private readonly AsymmetricKeyParameter _serverPrivateKey;
    private readonly SignatureAndHashAlgorithm _algorithm;

    public ServerZero(Certificate serverCertificate, AsymmetricKeyParameter serverPrivateKey)
    {
        _algorithm = new SignatureAndHashAlgorithm(HashAlgorithm.sha1, SignatureAlgorithm.rsa);

        _serverCertificate = serverCertificate;
        _serverPrivateKey = serverPrivateKey;
    }

    protected override ProtocolVersion MinimumVersion => ProtocolVersion.SSLv3;
    protected override ProtocolVersion MaximumVersion => ProtocolVersion.SSLv3;

    protected override int[] GetCipherSuites()
    {
        return new int[] { CipherSuite.TLS_RSA_WITH_RC4_128_SHA, CipherSuite.TLS_RSA_WITH_RC4_128_MD5 };
    }

    protected override TlsSignerCredentials GetRsaSignerCredentials()
    {
        return new DefaultTlsSignerCredentials(mContext, _serverCertificate, _serverPrivateKey, _algorithm);
    }
}