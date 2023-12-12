namespace Arcadia.EA.Constants;

public static class BadCompany
{
    public const string FeslAddress = "bfbc-ps3.fesl.ea.com";
    public const string TheaterAddress = "bfbc-ps3.theater.ea.com";
    public const int FeslPort = 18800;
    public const int TheaterPort = 18805;
}

public static class Rome
{
    public const string FeslAddressPs3 = "bfbc2-ps3.fesl.ea.com";
    public const string TheaterAddressPs3 = "bfbc2-ps3.theater.ea.com";
    public const int FeslClientPortPs3 = 18121;
    public const int TheaterClientPortPs3 = 18126;
    public const int FeslServerPortPc = 19021;
    public const int TheaterServerPortPc = 19026;
}

public static class Beach
{
    public const string FeslAddress = "beach-ps3.fesl.ea.com";
    public const string TheaterAddress = "beach-ps3.theater.ea.com";
    public const int FeslPort = 18231;
    public const int TheaterPort = 18236;
}

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