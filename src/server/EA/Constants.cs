namespace Arcadia.EA.Constants;

public static class FeslTransmissionType
{
    public const uint Ping = 0x00000000;
    public const uint SinglePacketResponse = 0x80000000;
    public const uint MultiPacketResponse = 0xb0000000;
    public const uint SinglePacketRequest = 0xc0000000;
    public const uint MultiPacketRequest = 0xf0000000;
}

public static class TheaterTransmissionType
{
    public const uint Request = 0x40000000;
    public const uint OkResponse = 0x00000000;
}