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
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto.Parameters;

ConsoleColor DefaultConsoleColor = Console.ForegroundColor;

ReadOnlyMemory<byte> Sha1CipherSignature = new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x05 };
const string Sha1CipherAlgorithm = "SHA1WITHRSA";

const string IssuerDN = "CN=OTG3 Certificate Authority, C=US, ST=California, L=Redwood City, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, emailAddress=dirtysock-contact@ea.com";
const string SubjectDN = "C=US, ST=California, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, CN=fesl.ea.com, emailAddress=fesl@ea.com";

var (PrivateKey, Certificate) = GenerateFeslCert(IssuerDN, SubjectDN);
var cn = new X509Name(SubjectDN).GetValueList(X509Name.CN)[0];

DumpCertificate(PrivateKey, Certificate, cn);

X509Certificate PatchCertificateSignaturePattern(X509Certificate cCertificate)
{
    var cert = DotNetUtilities.ToX509Certificate(cCertificate);
    var certDer = cert.GetRawCertData();

    // There must be two signatures in the DER encoded certificate
    var signature1Offset = FindBytePattern(certDer, Sha1CipherSignature.Span);
    var signature2Offset = FindBytePattern(certDer, Sha1CipherSignature.Span, signature1Offset + Sha1CipherSignature.Length);

    if (signature1Offset == -1 || signature2Offset == -1)
    {
        throw new Exception("Failed to find valid signature for patching!");
    }

    // Patch the second signature to TLS_NULL_WITH_NULL_NULL
    certDer[signature2Offset + 8] = 0x01;

    return new X509Certificate(certDer);
}

AsymmetricCipherKeyPair ConstructRsaKeyPair()
{
    BigInteger p = new("111453461317074268353761995724716395361446805418267262156522133799013175060193");
    BigInteger q = new("114726748596358283670400355329452217110158824439119423212154846632671665145039");
    BigInteger e = new("3");

    BigInteger n = p.Multiply(q);
    BigInteger phi = p.Subtract(BigInteger.One).Multiply(q.Subtract(BigInteger.One));
    BigInteger d = e.ModInverse(phi);
    
    BigInteger dP = d.Remainder(p.Subtract(BigInteger.One));
    BigInteger dQ = d.Remainder(q.Subtract(BigInteger.One));
    BigInteger qInv = q.ModInverse(p);

    var publicKeyParams = new RsaKeyParameters(false, n, e);
    var privateKeyParams = new RsaPrivateCrtKeyParameters(n, e, d, p, q, dP, dQ, qInv);
    
    return new AsymmetricCipherKeyPair(publicKeyParams, privateKeyParams);
}

(AsymmetricKeyParameter PrivateKey, Certificate Certificate) GenerateFeslCert(string issuer, string subject)
{
    var crypto = new BcTlsCrypto(new SecureRandom());
    var rsaKeyPairGen = new RsaKeyPairGenerator();
    rsaKeyPairGen.Init(new KeyGenerationParameters(crypto.SecureRandom, 1024));

    var caKeyPair = rsaKeyPairGen.GenerateKeyPair();
    var caCertificate = GenerateX509Certificate(issuer, caKeyPair, caKeyPair.Private);

    var cKeyPair = ConstructRsaKeyPair();
    var cCertificate = GenerateX509Certificate(subject, cKeyPair, caKeyPair.Private, caCertificate);

    cCertificate = PatchCertificateSignaturePattern(cCertificate);

    var store = new Pkcs12StoreBuilder().Build();
    var certEntry = new X509CertificateEntry(cCertificate);

    var certDomain = subject.Split("CN=")[1].Split(",")[0];

    LogLine($"Certificate generated & patched for CN={certDomain}", ConsoleColor.Green);

    store.SetCertificateEntry(certDomain, certEntry);
    store.SetKeyEntry(certDomain, new AsymmetricKeyEntry(cKeyPair.Private), [certEntry]);

    var chain = new TlsCertificate[] { new BcTlsCertificate(crypto, certEntry.Certificate.GetEncoded()) };
    var finalCertificate = new Certificate(chain);

    return (cKeyPair.Private, finalCertificate);
}

void DumpCertificate(AsymmetricKeyParameter privateKey, Certificate certificate, string serviceName)
{
    var certName = serviceName.Replace(".", string.Empty);
    var x509 = new X509Certificate(certificate.GetCertificateAt(0).GetEncoded());

    LogLine($"Certificate files generated:", ConsoleColor.DarkGreen);

    // Export Private Key
    {
        var file = $"{certName}-priv.pem";
        if (File.Exists(file)) LogLine($"Overwriting existing file: {file}", ConsoleColor.Red);

        using var textWriter = new StreamWriter(file);
        using var pemWriter = new PemWriter(textWriter);
        pemWriter.WriteObject(privateKey);
        pemWriter.Writer.Flush();

        LogLine(file);
    }

    // Export Certificate
    {
        var file = $"{certName}-cert.pem";
        if (File.Exists(file)) LogLine($"Overwriting existing file: {file}", ConsoleColor.Red);

        using var textWriter = new StreamWriter(file);
        using var pemWriter = new PemWriter(textWriter);
        pemWriter.WriteObject(x509);
        pemWriter.Writer.Flush();

        LogLine(file);
    }

    // Export PFX
    {
        var file = $"{certName}.pfx";
        if (File.Exists(file)) LogLine($"Overwriting existing file: {file}", ConsoleColor.Red);

        var store = new Pkcs12StoreBuilder().Build();
        var certEntry = new X509CertificateEntry(x509);

        store.SetCertificateEntry(x509.SubjectDN.ToString(), certEntry);
        store.SetKeyEntry(x509.SubjectDN.ToString(),
            new AsymmetricKeyEntry(privateKey),
            [certEntry]
        );

        using var fileStream = File.Create(file);
        store.Save(fileStream, [], new SecureRandom());

        LogLine(file);
    }
}

static X509Certificate GenerateX509Certificate(string subjectName, AsymmetricCipherKeyPair subjectKeyPair, AsymmetricKeyParameter issuerPrivKey, X509Certificate? issuerCert = null)
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
    var signatureFactory = new Asn1SignatureFactory(Sha1CipherAlgorithm, issuerPrivKey);
    return certGen.Generate(signatureFactory);
}

static int FindBytePattern(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> searchPattern, int offset = 0)
{
    if (searchPattern.IsEmpty || buffer.Length < searchPattern.Length || offset > buffer.Length - searchPattern.Length)
    {
        return -1;
    }

    int endIndex = buffer.Length - searchPattern.Length + 1;
    for (int i = offset; i < endIndex; i++)
    {
        if (buffer[i] == searchPattern[0] && buffer.Slice(i, searchPattern.Length).SequenceEqual(searchPattern))
        {
            return i;
        }
    }

    return -1;
}

void LogLine(string? message = null, ConsoleColor? color = null)
{
    message ??= string.Empty;

    if (color is null)
    {
        Console.WriteLine(message);
        return;
    }

    Console.ForegroundColor = (ConsoleColor)color;
    Console.WriteLine(message);
    Console.ForegroundColor =  DefaultConsoleColor;
}