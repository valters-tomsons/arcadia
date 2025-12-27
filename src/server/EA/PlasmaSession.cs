namespace Arcadia.EA;

public class PlasmaSession
{
    public IEAConnection? FeslConnection { get; set; }
    public IEAConnection? TheaterConnection { get; set; }

    public required PlasmaUser User { get; init; }

    public required string LKEY { get; init; }
    public required string ClientString { get; init; }
    public required string PartitionId { get; init; }

    public int PID { get; set; }
    public long EGAM_TID { get; set; }
}