using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace server;

public class FeslTcpServer : DefaultTlsServer
{
    private static readonly int[] _cipherSuites =
    {
        CipherSuite.TLS_RSA_WITH_RC4_128_SHA
    };

    private static readonly ProtocolVersion[] _supportedVersions =
    {
        ProtocolVersion.SSLv3
    };

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
        return _supportedVersions[0];
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
public class FeslTlsCrypto : BcTlsCrypto
{
    public override TlsCipher CreateCipher(TlsCryptoParameters cryptoParams, int encryptionAlgorithm, int macAlgorithm)
    {
        return encryptionAlgorithm switch
        {
            EncryptionAlgorithm.RC4_128 => CreateCipher_RC4(cryptoParams, 16, macAlgorithm),
            EncryptionAlgorithm.RC4_40 => CreateCipher_RC4(cryptoParams, 5, macAlgorithm),
            _ => base.CreateCipher(cryptoParams, encryptionAlgorithm, macAlgorithm),
        };
    }

    public override bool HasEncryptionAlgorithm(int encryptionAlgorithm)
    {
        return encryptionAlgorithm switch
        {
            EncryptionAlgorithm.RC4_128 or EncryptionAlgorithm.RC4_40 => true,
            _ => base.HasEncryptionAlgorithm(encryptionAlgorithm),
        };
    }

    private TlsCipher CreateCipher_RC4(TlsCryptoParameters cryptoParams, int cipherKeySize, int macAlgorithm)
    {
        return new TlsRc4Cipher(cryptoParams, cipherKeySize, CreateMac(cryptoParams, macAlgorithm),
            CreateMac(cryptoParams, macAlgorithm));
    }
}