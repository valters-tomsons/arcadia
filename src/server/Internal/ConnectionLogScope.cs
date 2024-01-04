namespace Arcadia.Internal;

public record ConnectionLogScope
{
    public string ConnectionId { get; init; } = Guid.NewGuid().ToString();
    public string ClientEndpoint { get; set; } = string.Empty;
}