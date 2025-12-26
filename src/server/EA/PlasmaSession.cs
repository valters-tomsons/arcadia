namespace Arcadia.EA;

public class PlasmaSession
{
    public IEAConnection? FeslConnection { get; set; }
    public IEAConnection? TheaterConnection { get; set; }

    public int PID { get; set; }

    public required long UID { get; init; }
    public required string NAME { get; init; }
    public required string LKEY { get; init; }

    public required string ClientString { get; init; }
    public required string PartitionId { get; init; }

    public required string PlatformName { get; set; }
}