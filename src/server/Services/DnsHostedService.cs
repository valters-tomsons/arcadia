using DNS.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Services;

public class DnsHostedService : IHostedService
{
    private readonly ILogger<DnsHostedService> _logger;

    private readonly MasterFile? _masterFile;
    private readonly DnsServer? _server;

    public DnsHostedService(ILogger<DnsHostedService> logger, IOptions<ArcadiaSettings> arcadiaSettings,
        IOptions<FeslSettings> feslSettings, IOptions<DnsSettings> dnsSettings)
    {
        _logger = logger;

        var options = dnsSettings.Value;
        if (!options.EnableDns) return;

        var arcadia = arcadiaSettings.Value;
        var fesl = feslSettings.Value;

        _masterFile = new MasterFile();
        _server = new DnsServer(_masterFile, "1.1.1.1", port: options.DnsPort);

        // Arcadia services
        _masterFile.AddIPAddressResourceRecord("theater.ps3.arcadia", options.FeslAddress);

        // Override EA PS3 backends
        _masterFile.AddIPAddressResourceRecord("beach-ps3.fesl.ea.com", options.FeslAddress);
        _masterFile.AddIPAddressResourceRecord("beach-ps3.theater.ea.com", options.FeslAddress);
        _masterFile.AddIPAddressResourceRecord("bfbc-ps3.fesl.ea.com", options.FeslAddress);
        _masterFile.AddIPAddressResourceRecord("bfbc-ps3.theater.ea.com", options.FeslAddress);
        _masterFile.AddIPAddressResourceRecord("bfbc2-ps3.fesl.ea.com", options.FeslAddress);
        _masterFile.AddIPAddressResourceRecord("bfbc2-ps3.theater.ea.com", options.FeslAddress);

        // Block EA Telemetry (breaks BFBC1)
        // _masterFile.AddIPAddressResourceRecord("messaging.ea.com", "127.0.0.1");

        _server.Listening += (sender, args) => _logger.LogInformation("DNS server listening!");
        _server.Requested += (sender, args) => _logger.LogDebug("DNS request: {Request}", args.Request);
        _server.Responded += (sender, args) => _logger.LogDebug("DNS response: {Response}", args.Response);
        _server.Errored += (sender, args) => _logger.LogError("DNS error: {Exception}", args.Exception);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _server?.Listen();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Dispose();
        return Task.CompletedTask;
    }
}