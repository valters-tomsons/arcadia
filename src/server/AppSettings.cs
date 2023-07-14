using Arcadia.Constants;

namespace Arcadia;

public record AppSettings
{
    public int Port { get; init; } = Beach.FeslPort;
    public string UpstreamHost { get; init; } = "beach-ps3.fesl.ea.com";
    public bool MirrorUpstreamCert { get; init; }
    public bool DumpPatchedCert { get; init; }
    public bool EnableProxyMode { get; init; }
}