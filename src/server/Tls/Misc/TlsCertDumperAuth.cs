using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;

namespace server.Tls.Misc;

public class TlsAuthCertDumper : TlsAuthentication
{
    public TlsCertificate[]? ServerCertificates { get; private set; }

    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        throw new NotImplementedException();
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        ServerCertificates = serverCertificate.Certificate.GetCertificateList();
    }
}