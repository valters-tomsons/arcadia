namespace Arcadia.EA;

public record PlasmaUser
{
    public required ulong UserId { get; init; }
    public required string Username { get; init; }
    public required string Platform { get; set; }

    public ulong? PlatformId { get; set; }
}