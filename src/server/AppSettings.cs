namespace Arcadia;

public record ArcadiaSettings
{
    public string ListenAddress { get; init; } = System.Net.IPAddress.Loopback.ToString();
    public string GameServerAddress { get; init; } = "gameserver1.ps3.arcadia";
    public int GameServerPort { get; init; } = 1003;

    // TODO: Get rid of this?
    public string TheaterAddress { get; init; } = "theater.ps3.arcadia";
}

public record DnsSettings
{
    public bool EnableDns { get; init; }
    public string ArcadiaAddress { get; init; } = string.Empty;
    public string TheaterAddress { get; init; } = "theater.ps3.arcadia";
    public int DnsPort { get; init; } = 53;
}

public record DebugSettings
{
    public bool WriteSslDebugKeys { get; init; }
    public bool EnableFileLogging { get; init; }
}