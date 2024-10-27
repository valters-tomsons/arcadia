using Arcadia.EA.Ports;

namespace Arcadia;

public record ArcadiaSettings
{
    public string ListenAddress { get; init; } = System.Net.IPAddress.Loopback.ToString();
    public string TheaterAddress { get; init; } = "theater.ps3.arcadia";
    public int[] ListenPorts { get; init; } = [
        (int)TheaterGamePort.RomePS3, 
        (int)FeslGamePort.RomePS3, 
        (int)FeslGamePort.BeachPS3
    ];

    public int MessengerPort { get; init; } = 0;
}

public record FileServerSettings
{
    public bool EnableCdn { get; init; } = false;
    public string ContentRoot { get; init; } = "static/";
    public string UrlPrefix { get; init; } = "http://0.0.0.0:8080/";
}

public record DiscordSettings
{
    public bool EnableBot { get; init; } = false;
    public string BotToken { get; init; } = string.Empty;
    public ulong[] Channels { get; init; } = [];
}

public record DebugSettings
{
    public bool WriteSslDebugKeys { get; init; }
    public bool EnableFileLogging { get; init; }
    public bool EnableDebugConsole { get; init; }
    public bool DisableTheaterJoinTimeout { get; init; }
}