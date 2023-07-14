using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace server.Tls.Crypto;

public class Rc4TlsCrypto : BcTlsCrypto
{
    private readonly bool _writeSslKeyLog;

    public Rc4TlsCrypto(bool writeSslKeyLog = false)
    {
        _writeSslKeyLog = writeSslKeyLog;
    }

    public override TlsCipher CreateCipher(TlsCryptoParameters cryptoParams, int encryptionAlgorithm, int macAlgorithm)
    {
        if (_writeSslKeyLog)
        {
            var secret = BCUtils.ReflectMasterSecret(cryptoParams.SecurityParameters.MasterSecret) ?? throw new Exception("Failed to reflect master secret");
            var clientRandom = Convert.ToHexString(cryptoParams.SecurityParameters.ClientRandom);
            var masterSecret = Convert.ToHexString(secret);

            using StreamWriter sw = File.AppendText("sslkeylog.log");
            sw.WriteLine("CLIENT_RANDOM " + clientRandom + " " + masterSecret);
        }

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