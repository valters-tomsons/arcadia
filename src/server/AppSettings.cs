namespace server;

public record AppSettings
{
    public int Port { get; init; } = Constants.Beach_FeslPort;
}