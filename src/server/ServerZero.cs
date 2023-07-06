using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Pkcs;

namespace server;

public class ServerZero : DefaultTlsServer
{
    protected override ProtocolVersion MinimumVersion
    {
        get { return ProtocolVersion.SSLv3; }
    }

    protected override ProtocolVersion MaximumVersion
    {
        get { return ProtocolVersion.SSLv3; }
    }

     protected override int[] GetCipherSuites()
    {
        // return new int[] { CipherSuite.TLS_RSA_WITH_RC4_128_SHA, CipherSuite.TLS_NULL_WITH_NULL_NULL };
        return new int[] { CipherSuite.TLS_RSA_WITH_RC4_128_SHA };
    }

    protected override TlsSignerCredentials GetRsaSignerCredentials()
    {
        try
        {
            var certificate = new X509Certificate2("fesl_vuln.pfx", "123456");
            var certExport = certificate.Export(X509ContentType.Pkcs12);
            var pckStream = new MemoryStream(certExport);

            var store = new Pkcs12Store(pckStream, "123456".ToCharArray());
            string alias = string.Empty;

            foreach (string n in store.Aliases)
            {
                if (store.IsKeyEntry(n) && store.GetKey(n).Key.IsPrivate)
                {
                    alias = n;
                    break;
                }
            }

            AsymmetricKeyEntry keyEntry = store.GetKey(alias);
            if (keyEntry.Key is RsaPrivateCrtKeyParameters rsa)
            {
                X509CertificateEntry[] chain = store.GetCertificateChain(alias);
                var certs = new Org.BouncyCastle.X509.X509Certificate[chain.Length];
                for (int i = 0; i < chain.Length; i++)
                {
                    certs[i] = chain[i].Certificate;
                }

                X509CertificateStructure[] certStructure = new X509CertificateStructure[certs.Length];
                for (int i = 0; i < certs.Length; i++)
                {
                    certStructure[i] = X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(certs[i].GetEncoded()));
                }

                var _certificate = new Certificate(certStructure);
                return new DefaultTlsSignerCredentials(mContext, _certificate, keyEntry.Key);
            }

            throw new Exception("No RSA private key found in the certificate.");
        }
        catch (Exception e)
        {
            throw new TlsFatalAlert(AlertDescription.internal_error, e);
        }
    }
}