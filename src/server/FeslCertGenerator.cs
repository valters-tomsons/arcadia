using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace server;

public static class FeslCertGenerator
{
    public static (AsymmetricKeyParameter, Certificate) GenerateVulnerableCert(SecureRandom secureRandom)
    {
        var rsaKeyPairGen = new RsaKeyPairGenerator();
        rsaKeyPairGen.Init(new KeyGenerationParameters(secureRandom, 1024));
        var caKeyPair = rsaKeyPairGen.GenerateKeyPair();

        var caCertificate = GenerateCertificate("CN=OTG3 Certificate Authority,OU=Online Technology Group,O=Electronic Arts/ Inc.,L=Redwood City,ST=California,C=US", caKeyPair, caKeyPair.Private);
        WritePem("certs/ca.key.pem", caKeyPair.Private);
        WritePem("certs/ca.crt", caCertificate);

        var cKeyPair = rsaKeyPairGen.GenerateKeyPair();
        var cCertificate = GenerateCertificate("CN=bfbc-ps3.fesl.ea.com,OU=Global Online Studio,O=Electronic Arts/ Inc.,ST=California,C=US", cKeyPair, caKeyPair.Private, caCertificate);
        WritePem("certs/c.key.pem", cKeyPair.Private);

        var pCertificate = PatchPemForOldProtoSSL(cCertificate);
        WritePem("certs/c.crt", pCertificate);

        var store = new Pkcs12Store();
        var certEntry = new X509CertificateEntry(pCertificate);
        store.SetCertificateEntry("bfbc-ps3.fesl.ea.com", certEntry);

        store.SetKeyEntry("bfbc-ps3.fesl.ea.com", new AsymmetricKeyEntry(cKeyPair.Private), new[] { certEntry });

        const string certFileName = "fesl_vuln.pfx";

        using (var fileStream = new FileStream(certFileName, FileMode.Create, FileAccess.ReadWrite))
        {
            store.Save(fileStream, "123456".ToCharArray(), new SecureRandom());
        }

        return (cKeyPair.Private, ParseCertificate(certFileName));
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

    private static void WritePem(string filename, object obj)
    {
        using var writer = new StreamWriter(filename);
        var pemWriter = new PemWriter(writer);
        pemWriter.WriteObject(obj);
        pemWriter.Writer.Flush();
    }

    private static X509Certificate PatchPemForOldProtoSSL(X509Certificate cCertificate)
    {
        var derCert = DotNetUtilities.ToX509Certificate(cCertificate);
        var derDump = derCert.GetRawCertData();

        var shaSignaturePattern = new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x05 };
        var signature1Offset = ByteSearch.FindPattern(derDump, shaSignaturePattern);
        var signature2Offset = ByteSearch.FindPattern(derDump, shaSignaturePattern, signature1Offset + 1);

        if (signature2Offset == -1)
        {
            throw new Exception("Failed to find valid SHA signature for patching!");
        }

        var byteOffset = signature2Offset + 8;
        derDump[byteOffset] = 0x01;

        using var derStream = new MemoryStream(derDump);
        var parser = new X509CertificateParser();
        return parser.ReadCertificate(derStream);
    }

    private static Certificate ParseCertificate(string pfxPath)
    {
        const string pfxPassword = "123456";

        using var pfxFileStream = new FileStream(pfxPath, FileMode.Open, FileAccess.Read);
        var pkcs12Store = new Pkcs12Store(pfxFileStream, pfxPassword.ToCharArray());

        string alias = pkcs12Store.Aliases.Cast<string>().First() ?? throw new Exception("Failed to find certificate alias!");

        X509CertificateEntry certificateEntry = pkcs12Store.GetCertificate(alias);
        X509CertificateStructure[] chain = new[] { certificateEntry.Certificate.CertificateStructure };
        return new Certificate(chain);
    }
}