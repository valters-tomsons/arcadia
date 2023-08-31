using Arcadia.EA.Constants;

namespace Arcadia;

public record ArcadiaSettings
{
    public int FeslPort { get; init; } = Beach.FeslPort;

    public string TheaterAddress { get; init; } = "127.0.0.1";
    public int TheaterPort { get; init; } = Beach.TheaterPort;

    public bool DumpPatchedCert { get; init; }
}

public record FeslProxySettings
{
    public string ServerAddress { get; init; } = "beach-ps3.fesl.ea.com";
    public bool MirrorCertificateStrings { get; init; }
    public bool EnableProxy { get; init; }
}