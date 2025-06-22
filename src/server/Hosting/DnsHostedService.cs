using DNS.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

public class DnsHostedService : IHostedService
{
    private readonly ILogger<DnsHostedService> _logger;
    private readonly MasterFile? _masterFile;
    private readonly DnsServer? _server;

    public DnsHostedService(IOptions<DnsSettings> settings, ILogger<DnsHostedService> logger)
    {
        _logger = logger;

        var options = settings.Value;
        if (!options.EnableDns) return;

        var hostAddr = options.ServerAddress;

        _masterFile = new MasterFile();
        _server = new DnsServer(_masterFile, "1.1.1.1", port: options.DnsPort);

        _masterFile.AddIPAddressResourceRecord("messaging.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("theater.ps3.arcadia", hostAddr);
        _masterFile.AddIPAddressResourceRecord("easo.ea.com", hostAddr);

        _masterFile.AddIPAddressResourceRecord("bfbc2-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("beach-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("ao3-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("nfsps2-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("bfbc-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("ao2-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("simpsons-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("mohair-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("nfsps-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("nfs-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("nfsmw2-ps3.fesl.ea.com", hostAddr);
        _masterFile.AddIPAddressResourceRecord("hl2-ps3.fesl.ea.com", hostAddr);

        _server.Listening += (sender, args) => _logger.LogInformation($"DNS server listening!");
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