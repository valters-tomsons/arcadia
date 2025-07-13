using System.Reflection;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Arcadia.Tls.Crypto;

/// <summary>
/// Original code from: https://github.com/zivillian/ism7mqtt
/// </summary>
public class Rc4TlsCrypto(IOptions<DebugSettings> settings) : BcTlsCrypto
{
    private readonly bool _writeSslKeyLog = settings.Value.WriteSslDebugKeys;

    public override TlsCipher CreateCipher(TlsCryptoParameters cryptoParams, int encryptionAlgorithm, int macAlgorithm)
    {
        if (_writeSslKeyLog)
        {
            var secret = ReflectMasterSecretFromBCTls(cryptoParams.SecurityParameters.MasterSecret) ?? throw new Exception("Failed to reflect master secret");
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

    private TlsRc4Cipher CreateCipher_RC4(TlsCryptoParameters cryptoParams, int cipherKeySize, int macAlgorithm)
    {
        return new TlsRc4Cipher(cryptoParams, cipherKeySize, CreateMac(cryptoParams, macAlgorithm),
            CreateMac(cryptoParams, macAlgorithm));
    }

    private static byte[]? ReflectMasterSecretFromBCTls(TlsSecret secret)
    {
        // We need to use reflection to access the master secret from BC
        // because using Extract() destroys the key for subsequent calls
        const BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        var field = typeof(BcTlsSecret).GetField("m_data", bindingFlags);
        return (byte[]?)field?.GetValue(secret);
    }
}