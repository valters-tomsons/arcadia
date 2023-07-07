using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace server;

public static class FeslCertGenerator
{
    public static (AsymmetricKeyParameter, Certificate) GenerateVulnerableCert(BcTlsCrypto crypto)
    {
        var rsaKeyPairGen = new RsaKeyPairGenerator();
        rsaKeyPairGen.Init(new KeyGenerationParameters(crypto.SecureRandom, 1024));

        var caKeyPair = rsaKeyPairGen.GenerateKeyPair();
        var caCertificate = GenerateCertificate("CN=OTG3 Certificate Authority,OU=Online Technology Group,O=Electronic Arts Inc.,L=Redwood City,ST=California,C=US", caKeyPair, caKeyPair.Private);

        var cKeyPair = rsaKeyPairGen.GenerateKeyPair();
        var cCertificate = GenerateCertificate("CN=bfbc-ps3.fesl.ea.com,OU=Global Online Studio,O=Electronic Arts Inc.,ST=California,C=US", cKeyPair, caKeyPair.Private, caCertificate);

        var patched_cCertificate = PatchCertificateSignaturePattern(cCertificate);

        var store = new Pkcs12StoreBuilder().Build();
        var certEntry = new X509CertificateEntry(patched_cCertificate);
        store.SetCertificateEntry("bfbc-ps3.fesl.ea.com", certEntry);
        store.SetKeyEntry("bfbc-ps3.fesl.ea.com", new AsymmetricKeyEntry(cKeyPair.Private), new[] { certEntry });

        var chain = new TlsCertificate[] { new BcTlsCertificate(crypto, certEntry.Certificate.GetEncoded()) };
        var finalCertificate = new Certificate(chain);

        return (cKeyPair.Private, finalCertificate);
    }

    private static X509Certificate GenerateCertificate(string subjectName, AsymmetricCipherKeyPair subjectKeyPair, AsymmetricKeyParameter issuerPrivKey, X509Certificate? issuerCert = null)
    {
        var issuerDn = issuerCert == null ? new X509Name(subjectName) : issuerCert.SubjectDN;

        var certGen = new X509V3CertificateGenerator();
        var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), new SecureRandom());
        certGen.SetSerialNumber(serialNumber);
        certGen.SetIssuerDN(issuerDn);
        certGen.SetNotBefore(DateTime.UtcNow.Date);
        certGen.SetNotAfter(DateTime.UtcNow.Date.AddYears(10));
        certGen.SetSubjectDN(new X509Name(subjectName));
        certGen.SetPublicKey(subjectKeyPair.Public);
        var signatureFactory = new Asn1SignatureFactory("SHA1WITHRSA", issuerPrivKey);
        return certGen.Generate(signatureFactory);
    }

    private static X509Certificate PatchCertificateSignaturePattern(X509Certificate cCertificate)
    {
        var derCert = DotNetUtilities.ToX509Certificate(cCertificate);
        var derDump = derCert.GetRawCertData();

        var signaturePattern = new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x05 };
        var signature1Offset = ByteSearch.FindPattern(derDump, signaturePattern);
        var signature2Offset = ByteSearch.FindPattern(derDump, signaturePattern, signature1Offset + 1);

        if (signature1Offset == -1 || signature2Offset == -1)
        {
            throw new Exception("Failed to find valid signature for patching!");
        }

        var byteOffset = signature2Offset + 8;
        derDump[byteOffset] = 0x01;

        using var derStream = new MemoryStream(derDump);
        var parser = new X509CertificateParser();
        return parser.ReadCertificate(derStream);
    }
}