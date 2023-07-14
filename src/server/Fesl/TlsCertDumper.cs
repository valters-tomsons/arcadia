using System.Net.Sockets;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;
using server.Tls;
using server.Tls.Misc;

namespace server.Fesl;

public static class TlsCertDumper
{
    public static (string IssuerDN, string SubjectDN) DumpPubFeslCert(string serverHost, int port)
    {
        var crypto = new BcTlsCrypto(new SecureRandom());

        using var tcpClient = new TcpClient(serverHost, port);
        using var networkStream = tcpClient.GetStream();
        var tlsClientProtocol = new TlsClientProtocol(networkStream);

        var certDumper = new TlsAuthCertDumper();
        var tlsClient = new Ssl3TlsClient(crypto, certDumper);

        Console.WriteLine($"Connecting to {serverHost}:{port}...");

        try
        {
            tlsClientProtocol.Connect(tlsClient);
            Console.WriteLine("SSL Handshake with backend server successful!");
        }
        catch
        {
            // Intentionally swallow exceptions, due to cipher suite errors.
            // TODO: Use RC4
        }

        if (certDumper.ServerCertificates == null)
        {
            Console.WriteLine("Warning! Failed to retrieve certificate from upstream server!");
            return (string.Empty, string.Empty);
        }

        // Gymnastics to extract exact DN strings from the upstream certificate.
        var cCertificate = new X509Certificate(certDumper.ServerCertificates[0].GetEncoded());
        var x509cert = DotNetUtilities.ToX509Certificate(cCertificate);
        var certDer = x509cert.GetRawCertData();

        var bc = new X509CertificateParser().ReadCertificate(certDer);
        var tbsCertificate = TbsCertificateStructure.GetInstance(Asn1Object.FromByteArray(bc.GetTbsCertificate()));

        var issuer = tbsCertificate.Issuer.ToString();
        var subject = tbsCertificate.Subject.ToString();

        Console.WriteLine("Copying from upstream certificate:");
        Console.WriteLine($"Issuer: {issuer}");
        Console.WriteLine($"Subject: {subject}");

        return (issuer, subject);
    }
}