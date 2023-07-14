namespace server;

public record AppSettings
{
    public int Port { get; init; } = Constants.Beach_FeslPort;
    public string UpstreamHost { get; init; } = "beach-ps3.fesl.ea.com";
}