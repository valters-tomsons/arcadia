using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace server;

public static class FeslCertGenerator
{
    public static void GenerateVulnerableCert()
    {
        var rsaKeyPairGen = new RsaKeyPairGenerator();
        rsaKeyPairGen.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
        var caKeyPair = rsaKeyPairGen.GenerateKeyPair();

        var caCertificate = GenerateCertificate("CN=OTG3 Certificate Authority,OU=Online Technology Group,O=Electronic Arts/ Inc.,L=Redwood City,ST=California,C=US", caKeyPair, caKeyPair.Private);
        WritePem("certs/ca.key.pem", caKeyPair.Private);
        WritePem("certs/ca.crt", caCertificate);

        var cKeyPair = rsaKeyPairGen.GenerateKeyPair();
        var cCertificate = GenerateCertificate("CN=bfbc-ps3.fesl.ea.com,OU=Global Online Studio,O=Electronic Arts/ Inc.,ST=California,C=US", cKeyPair, caKeyPair.Private, caCertificate);
        WritePem("certs/c.key.pem", cKeyPair.Private);
        WritePem("certs/c.crt", cCertificate);

        var store = new Pkcs12Store();
        var certEntry = new X509CertificateEntry(cCertificate);
        store.SetCertificateEntry("bfbc-ps3.fesl.ea.com", certEntry);
        store.SetKeyEntry("bfbc-ps3.fesl.ea.com", new AsymmetricKeyEntry(cKeyPair.Private), new[] { certEntry });

        Directory.CreateDirectory("certs");
        using var fileStream = new FileStream("certs/c.pfx", FileMode.Create, FileAccess.ReadWrite);
        store.Save(fileStream, "123456".ToCharArray(), new SecureRandom());
    }

    private static X509Certificate GenerateCertificate(string subjectName, AsymmetricCipherKeyPair subjectKeyPair, AsymmetricKeyParameter issuerPrivKey, X509Certificate? issuerCert = null)
    {
        var issuerDn = issuerCert == null ? new X509Name(subjectName) : issuerCert.SubjectDN;

        var certGen = new X509V3CertificateGenerator();
        var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), new SecureRandom());
        certGen.SetSerialNumber(serialNumber);
        certGen.SetIssuerDN(issuerDn);
        certGen.SetNotBefore(DateTime.UtcNow.Date);
        certGen.SetNotAfter(DateTime.UtcNow.Date.AddYears(10));
        certGen.SetSubjectDN(new X509Name(subjectName));
        certGen.SetPublicKey(subjectKeyPair.Public);
        var signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", issuerPrivKey);
        return certGen.Generate(signatureFactory);
    }

    private static void WritePem(string filename, object obj)
    {
        using var writer = new StreamWriter(filename);
        var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(writer);
        pemWriter.WriteObject(obj);
        pemWriter.Writer.Flush();
    }
}