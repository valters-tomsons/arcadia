using Org.BouncyCastle.Tls;

namespace Arcadia.Tls.Misc;

public class ProxyTlsAuthentication : TlsAuthentication
{
    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        throw new NotImplementedException();
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        Console.WriteLine("Ignoring server certificate...");
    }
}