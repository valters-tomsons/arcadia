using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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

namespace Arcadia.EA;

/// <summary>
/// Based on the following article: https://github.com/Aim4kill/Bug_OldProtoSSL
/// </summary>
public class ProtoSSL
{
    private readonly ILogger<ProtoSSL> _logger;

    private readonly ConcurrentDictionary<string, (AsymmetricKeyParameter, Certificate)> _certCache = new();
    private const string Sha1CipherAlgorithm = "SHA1WITHRSA";
    private static readonly ReadOnlyMemory<byte> Sha1CipherSignature = new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x05 };

    public ProtoSSL(ILogger<ProtoSSL> logger)
    {
        _logger = logger;
    }

    public (AsymmetricKeyParameter, Certificate) GetFeslEaCert()
    {
        const string IssuerDN = "CN=OTG3 Certificate Authority, C=US, ST=California, L=Redwood City, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, emailAddress=dirtysock-contact@ea.com";
        const string SubjectDN = "C=US, ST=California, O=\"Electronic Arts, Inc.\", OU=Online Technology Group, CN=fesl.ea.com, emailAddress=fesl@ea.com";
        const string cacheKey = "fesl.ea.com";

        if (_certCache.TryGetValue(cacheKey, out var cacheHit))
        {
            // !TODO: New connections will break after running 10 years without a restart 
            return cacheHit;
        }

        var creds = GenerateVulnerableCert(IssuerDN, SubjectDN);
        _certCache.TryAdd(cacheKey, creds);
        return creds;
    }

    /// <summary>
    /// Generates a certificate for vulnerable ProtoSSL versions.
    /// </summary>
    private (AsymmetricKeyParameter, Certificate) GenerateVulnerableCert(string issuer, string subject)
    {
        var crypto = new BcTlsCrypto(new SecureRandom());
        var rsaKeyPairGen = new RsaKeyPairGenerator();
        rsaKeyPairGen.Init(new KeyGenerationParameters(crypto.SecureRandom, 1024));

        var caKeyPair = rsaKeyPairGen.GenerateKeyPair();
        var caCertificate = GenerateCertificate(issuer, caKeyPair, caKeyPair.Private);

        var cKeyPair = rsaKeyPairGen.GenerateKeyPair();
        var cCertificate = GenerateCertificate(subject, cKeyPair, caKeyPair.Private, caCertificate);
        var patched_cCertificate = PatchCertificateSignaturePattern(cCertificate);

        var store = new Pkcs12StoreBuilder().Build();
        var certEntry = new X509CertificateEntry(patched_cCertificate);

        var certDomain = subject.Split("CN=")[1].Split(",")[0];

        _logger.LogDebug("Certificate generated for: {domain}", certDomain);

        store.SetCertificateEntry(certDomain, certEntry);
        store.SetKeyEntry(certDomain, new AsymmetricKeyEntry(cKeyPair.Private), new[] { certEntry });

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
        var signatureFactory = new Asn1SignatureFactory(Sha1CipherAlgorithm, issuerPrivKey);
        return certGen.Generate(signatureFactory);
    }

    private static X509Certificate PatchCertificateSignaturePattern(X509Certificate cCertificate)
    {
        var cert = DotNetUtilities.ToX509Certificate(cCertificate);
        var certDer = cert.GetRawCertData();

        // There must be two signatures in the DER encoded certificate
        var signature1Offset = Utils.FindBytePattern(certDer, Sha1CipherSignature.Span);
        var signature2Offset = Utils.FindBytePattern(certDer, Sha1CipherSignature.Span, signature1Offset + Sha1CipherSignature.Length);

        if (signature1Offset == -1 || signature2Offset == -1)
        {
            throw new Exception("Failed to find valid signature for patching!");
        }

        // Patch the second signature to TLS_NULL_WITH_NULL_NULL
        certDer[signature2Offset + 8] = 0x01;

        using var derStream = new MemoryStream(certDer);
        var parser = new X509CertificateParser();
        return parser.ReadCertificate(derStream);
    }
}