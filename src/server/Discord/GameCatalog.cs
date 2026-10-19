using System.Collections.Frozen;
using Discord;
using Arcadia.EA;
using static Arcadia.Discord.Constants;

namespace Arcadia.Discord;

public sealed record StatusField(string Name, Func<GameServerListing, string?> Value);
public sealed record LevelInfo(string Display, string Image);
public sealed record LevelSource(string DataKey, FrozenDictionary<string, LevelInfo> Levels);

public sealed record GameInfo
{
    public required string Title { get; init; }
    public StatusField[] Fields { get; init; } = [];
    public Func<GameServerListing, bool>? Listed { get; init; }
    public LevelSource? Level { get; init; }
    public Func<GameServerListing, string?>? Footer { get; init; }
}

public static class GameCatalog
{
    public static readonly FrozenDictionary<string, LevelInfo> OnslaughtLevels = new Dictionary<string, LevelInfo>
    {
        { "Levels/ONS_MP_002", new("Valparaiso",     "BC2_Valparaiso.jpg") },
        { "Levels/ONS_MP_004", new("Isla Inocentes", "BC2_Isla_Inocentes.jpg") },
        { "Levels/ONS_MP_005", new("Atacama Desert", "BC2_Atacama_Desert.jpg") },
        { "Levels/ONS_MP_008", new("Nelson Bay",     "BC2_Nelson_Bay.jpg") },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, LevelInfo> BeachLevels = new Dictionary<string, LevelInfo>
    {
        { "Levels/Coral_sea",     new("Coral Sea",     "BF1943_Coral_Sea.jpg") },
        { "Levels/Wake_island_s", new("Wake Island",   "BF1943_Wake_Island.jpg") },
        { "Levels/Guadal_Canal",  new("Guadal Canal",  "BF1943_Guadalcanal.jpg") },
        { "Levels/Iwo_Jima_s",    new("Iwo Jima",      "BF1943_Iwo_Jima.jpg") },
    }.ToFrozenDictionary();

    // Gold Rush levels are CamelCase, Conquest (title update 1.20) levels are lowercase
    private static readonly FrozenDictionary<string, LevelInfo> BadCompanyLevels = new Dictionary<string, LevelInfo>
    {
        { "Levels/MP_BeachHead_Day1",  new("Harvest Day",        "BC1_Harvest_Day.jpg") },
        { "Levels/MP_BeachHead_Day2",  new("Over and Out",       "BC1_Over_and_Out.jpg") },
        { "Levels/MP_Harbour_Day1",    new("Valley Run",         "BC1_Valley_Run.jpg") },
        { "Levels/MP_Harbour_Day2",    new("Deconstruction",     "BC1_Deconstruction.jpg") },
        { "Levels/MP_Ignition_Day1",   new("Oasis",              "BC1_Oasis.jpg") },
        { "Levels/MP_Ignition_Day2",   new("Final Ignition",     "BC1_Final_Ignition.jpg") },
        { "Levels/MP_Monastery_Day1",  new("End of the Line",    "BC1_End_of_the_Line.jpg") },
        { "Levels/MP_Monastery_Day2",  new("Ascension",          "BC1_Ascension.jpg") },

        { "Levels/mp_golfcourse_cq",   new("Par for the Course", "BC1_Par_for_the_Course.jpg") },
        { "Levels/mp_ghost_town_cq",   new("Ghost Town",         "BC1_Ghost_Town.jpg") },
        { "Levels/mp_harvest_day_cq",  new("Harvest Day",        "BC1_Harvest_Day.jpg") },
        { "Levels/mp_crossroads_cq",   new("Crossing Over",      "BC1_Crossing_Over.jpg") },
        { "Levels/mp_oasis_cq",        new("Oasis",              "BC1_Oasis.jpg") },
        { "Levels/mp_zabograd_cq",     new("Acta Non Verba",     "BC1_Acta_Non_Verba.jpg") },
        { "Levels/mp_eotl_cq",         new("End of the Line",    "BC1_End_of_the_Line.jpg") },
        { "Levels/mp_ascension_cq",    new("Ascension",          "BC1_Ascension.jpg") },
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> BadCompanyModes = new Dictionary<string, string>
    {
        { "GOLDRUSH", "Gold Rush" },
        { "CONQUEST", "Conquest" },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> LotrModes = new Dictionary<string, string>
    {
        { "1988399932",  "Conquest" },
        { "42885688",    "Hero TDM" },
        { "2015881514",  "Team Deathmatch" },
        { "1503065498",  "Capture the Ring" },
        { "-122228709",  "War of the Ring" },
        { "270340015",   "War of the Ring (Lobby)" },
        { "-1028407685", "Instant Action (Lobby)" },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> LotrLevels = new Dictionary<string, string>
    {
        { "-1109594032", "The Black Gate" },
        { "-1789567933", "Helm's Deep" },
        { "-1342191936", "Isengard" },
        { "-1040791731", "Minas Morgul" },
        { "-1988909791", "Minas Tirith" },
        { "-388473313",  "Minas Tirith Top" },
        { "-1994967355", "Mines of Moria" },
        { "-262282117",  "Mount Doom" },
        { "-1683102250", "Osgiliath" },
        { "-2030748778", "Pelennor Fields" },
        { "-1389475930", "Rivendell" },
        { "261105074",   "The Shire" },
        { "1680074377",  "Weathertop" },
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<GameId, GameInfo> Games = new Dictionary<GameId, GameInfo>
    {
        [GameId.BEACH] = new()
        {
            Title = "Battlefield 1943",
            Level = new("B-U-level", BeachLevels),
            Fields =
            [
                new("Name", static s => Format.Sanitize(s.NAME)),
            ]
        },

        [GameId.BFBC] = new()
        {
            Title = "Battlefield: Bad Company",
            Level = new("B-U-level", BadCompanyLevels),
            Fields =
            [
                new("Name", static s => Format.Sanitize(s.NAME)),
                new("Gamemode", static s => Lookup(BadCompanyModes, s, "B-U-gamemode") ?? "`N/A`")
            ]
        },

        [GameId.BFBC2] = new()
        {
            Title = "Battlefield: Bad Company 2",
            Listed = static s => !string.IsNullOrWhiteSpace(Data(s, "B-U-level")),
            Level = new("B-U-level", OnslaughtLevels),
            Fields =
            [
                new("Name",       static s => Format.Sanitize(s.NAME.Replace("P2P-", string.Empty))),
                new("Gamemode",   static s => Text(s, "B-U-gamemode") ?? "`N/A`"),
                new("Difficulty", static s => Text(s, "B-U-difficulty")),
            ]
        },

        [GameId.AO3] = new()
        {
            Title = "Army of Two: 40th Day",
            Fields =
            [
                new("Name",     static s => Format.Sanitize(s.NAME)),
                new("Gamemode", static s => Text(s, "B-U-Mode") ?? "`N/A`"),
                new("Level",    static s => Text(s, "B-U-Map") ?? "`N/A`"),
                new("Playlist", static s => Text(s, "B-U-MapPlaylist") ?? "`N/A`"),
            ]
        },

        [GameId.MERCS2] = new()
        {
            Title = "Mercenaries 2",
            Fields =
            [
                new("Friendly Fire", static s => Enabled(s, "B-U-FriendlyFire") ? "Yes" : null),
                new("Money",         static s => long.TryParse(Data(s, "B-U-Money"), out var money) && money > 0 ? $"${money:N0}" : null),
                new("Mission",       static s => Text(s, "B-U-Mission")),
            ]
        },

        [GameId.LOTR] = new()
        {
            Title = "Lord of the Rings: Conquest",
            Listed = static s => !Enabled(s, "B-U-FriendsOnly"),
            Fields =
            [
                new("Name",  static s => Enabled(s, "B-U-PCDedicated") ? Format.Sanitize(s.NAME.Replace("\"", string.Empty)) : null),
                new("Mode",  static s => Lookup(LotrModes, s, "B-U-Mode")),
                new("Level", static s => Lookup(LotrLevels, s, "B-U-LevelName")),
            ]
        },

        [GameId.MOHAIR] = new()
        {
            Title = "Medal of Honor: Airborne",
            Fields =
            [
                new("Map",      static s => Text(s, "B-U-Map")),
                new("Gamemode", static s => Text(s, "B-U-GameType")),
            ]
        },

        [GameId.CNCRA3] = new()
        {
            Title = "Command & Conquest: Red Alert 3",
            Fields =
            [
                // There is `B-U-_gameType` but game always sends `skirmish`
                new("Mode",   static s => Data(s, "B-U-_matchMode") is { } mode ? (mode == "private" ? "Campaign" : "Skirmish") : null),
                new("Closed", static s => Enabled(s, "B-U-_closed") ? "Yes" : null),
            ]
        },
    }.ToFrozenDictionary();

    private static string? Data(GameServerListing server, string key)
        => server.Data.GetValueOrDefault(key);

    // Values sent by game clients, escaped for display in Discord markdown
    private static string? Text(GameServerListing server, string key)
        => Data(server, key) is { } value ? Format.Sanitize(value) : null;

    private static bool Enabled(GameServerListing server, string key)
        => Data(server, key) == "1";

    private static string? Lookup(FrozenDictionary<string, string> table, GameServerListing server, string key)
        => Data(server, key) is { } value ? table.GetValueOrDefault(value) : null;
}
