using Arcadia.EA.Constants;

namespace Arcadia;

public record ArcadiaSettings
{
    public string TheaterAddress { get; init; } = "theater.ps3.arcadia";
    public int TheaterPort { get; init; } = Beach.TheaterPort;

    public bool DumpPatchedCert { get; init; }
}

public record FeslSettings
{
    // *must* match domain of the original server, otherwise the client will reject the certificate
    // (this is the domain of the original server, not the proxy)
    public string ServerAddress { get; init; } = "beach-ps3.fesl.ea.com";

    // *must* match port of the original server, otherwise the client won't be able to connect
    public int ServerPort { get; init; } = Beach.FeslPort;

    public bool MirrorCertificateStrings { get; init; }
    public bool EnableProxy { get; init; }
}