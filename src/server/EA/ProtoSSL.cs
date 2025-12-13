using System.Reflection;
using Arcadia.Tls.Crypto;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;

namespace Arcadia.EA;

public class ProtoSSL
{
    private readonly Certificate _cert;
    private readonly AsymmetricKeyParameter _keyParam;

    public ProtoSSL(Rc4TlsCrypto crypto)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var resource = assembly.GetManifestResourceStream("Arcadia.fesl-pub.der");

        var cert = new X509CertificateParser().ReadCertificate(resource);
        var tlsCert = new BcTlsCertificate(crypto, cert.GetEncoded());

        _cert = new Certificate([tlsCert]);
        _keyParam = ConstructRsaPrivateKey();
    }

    public (AsymmetricKeyParameter, Certificate) GetFeslEaCert()
    {
        return (_keyParam, _cert);
    }

    private static AsymmetricKeyParameter ConstructRsaPrivateKey()
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

        return new RsaPrivateCrtKeyParameters(n, e, d, p, q, dP, dQ, qInv);
    }
}