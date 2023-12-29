using Arcadia.EA;
using Org.BouncyCastle.Tls;

namespace Arcadia.Interfaces;

public interface IEAConnection
{
    string ClientEndpoint { get; }
    Stream? NetworkStream { get; }

    void InitializeInsecure(Stream network, string endpoint);
    void InitializeSecure(TlsServerProtocol network, string endpoint);

    IAsyncEnumerable<Packet> StartConnection(CancellationToken ct = default);

    Task<bool> SendPacket(Packet packet);
    Task<bool> SendBinary(byte[] buffer);
}