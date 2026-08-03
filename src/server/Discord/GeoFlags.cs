using System.Net;
using Microsoft.Extensions.Logging;

namespace Arcadia.Discord;

public sealed class GeoFlags(ILogger logger) : IDisposable
{
    private const string DatabaseFile = "ip-to-country.mmdb";

    private static readonly HashSet<string> EuCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR",
        "DE", "GR", "HU", "IE", "IT", "LV", "LT", "LU", "MT", "NL",
        "PL", "PT", "RO", "SK", "SI", "ES", "SE"
    };

    private readonly ILogger _logger = logger;
    private readonly Dictionary<IPAddress, string> _cache = [];

    private MaxMind.Db.Reader? _db;

    public void Load()
    {
        if (_db is not null || !File.Exists(DatabaseFile)) return;

        try
        {
            _db = new MaxMind.Db.Reader(DatabaseFile);
            _logger.LogInformation("GeoIP database loaded from '{FileName}', build: {BuildDate}", DatabaseFile, _db.Metadata.BuildDate);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load GeoIP database");
        }
    }

    // Returns the flag prefixed with a space, for appending to an embed title
    public string Suffix(string? address)
    {
        if (_db is null || !IPAddress.TryParse(address, out var ip)) return string.Empty;
        if (_cache.TryGetValue(ip, out var cached)) return cached;

        var flag = string.Empty;

        try
        {
            var countryCode = _db.Find<GeoResult>(ip)?.CountryCode;
            if (Flag(countryCode) is { Length: > 0 } symbol) flag = $" {symbol}";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GeoIP lookup failed for {Address}", ip);
        }

        _cache[ip] = flag;
        return flag;
    }

    private static string Flag(string? countryCode)
    {
        if (countryCode is not { Length: 2 }) return string.Empty;
        if (EuCountries.Contains(countryCode)) return "🇪🇺";

        countryCode = countryCode.ToUpperInvariant();

        // Convert each letter to a Regional Indicator Symbol
        var first = 0x1F1E6 + (countryCode[0] - 'A');
        var second = 0x1F1E6 + (countryCode[1] - 'A');

        return char.ConvertFromUtf32(first) + char.ConvertFromUtf32(second);
    }

    public void Dispose() => _db?.Dispose();

    [method: MaxMind.Db.Constructor]
    public record GeoResult(
        [MaxMind.Db.Parameter("country_code")] string CountryCode
    );
}
