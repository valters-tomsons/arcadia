namespace Arcadia.EA.Ports;

public enum FeslGamePort : int
{
    BadCompanyPS3 = 18800,
    RomePS3 = 18121,
    RomeDemoPS3 = 18171,
    BeachPS3 = 18231,
    RomePC = 18390,
    ArmyOfTwoPS3 = 18340,
    ArmyOfTwo2PS3 = 18141,
    NfsShift = 18221
}

public enum TheaterGamePort : int
{
    BadCompanyPS3 = 18805,
    RomePS3 = 18126,
    BeachPS3 = 18236,
    RomePC = 18395
}

public enum FeslServerPort : int
{
    RomePC = 19021
}

public enum TheaterServerPort : int
{
    RomePC = 19026
}

public static class PortExtensions
{
    public static bool IsFeslPort(int port) => Enum.IsDefined(typeof(FeslGamePort), port) || Enum.IsDefined(typeof(FeslServerPort), port);
    public static bool IsTheater(int port) => Enum.IsDefined(typeof(TheaterGamePort), port) || Enum.IsDefined(typeof(TheaterServerPort), port);
}