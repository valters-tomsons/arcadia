using Arcadia.EA.Constants;

namespace Arcadia;

public record ArcadiaSettings
{
    public string ListenAddress { get; init; } = System.Net.IPAddress.Loopback.ToString();
    public string TheaterAddress { get; init; } = "theater.ps3.arcadia";
    public int TheaterPort { get; init; } = Beach.TheaterPort;
    public string GameServerAddress { get; init; } = "gameserver1.ps3.arcadia";
    public int GameServerPort { get; init; } = 1003;
}

public record FeslSettings
{
    // *must* match domain of the original server, otherwise the client will reject the certificate
    // (this is the domain of the original server, not the proxy)
    public string ServerAddress { get; init; } = "beach-ps3.fesl.ea.com";

    // *must* match port of the original server, otherwise the client won't be able to connect
    public int ServerPort { get; init; } = Beach.FeslPort;
}

public record DnsSettings
{
    public bool EnableDns { get; init; }
    public string FeslAddress { get; init; } = string.Empty;
    public int DnsPort { get; init; } = 53;
}

public record DebugSettings
{
    public bool WriteSslDebugKeys { get; init; }
    public bool EnableFileLogging { get; init; }
}