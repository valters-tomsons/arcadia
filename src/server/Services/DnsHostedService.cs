using DNS.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Arcadia.Services;

public class DnsHostedService : IHostedService
{
    private readonly DnsSettings? _settings;
    private readonly MasterFile? _masterFile;
    private readonly DnsServer? _server;
    private Task? _serverTask;

    public DnsHostedService(IOptions<DnsSettings> settings, IOptions<ArcadiaSettings> arcadiaSettings, IOptions<FeslSettings> feslSettings)
    {
        var options = settings.Value;
        if (!options.EnableDns) return;

        _settings = options;

        var arcadia = arcadiaSettings.Value;
        var fesl = feslSettings.Value;

        _masterFile = new MasterFile();
        _server = new DnsServer(_masterFile);

        _masterFile.AddIPAddressResourceRecord(arcadia.TheaterAddress, "192.168.0.39");
        _masterFile.AddIPAddressResourceRecord(fesl.ServerAddress, "192.168.0.39");
        _masterFile.AddIPAddressResourceRecord(arcadia.GameServerAddress, "192.168.0.39");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            _serverTask = Task.Run(async () => await _server.Listen(), cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if(_serverTask is null) return;
        await _serverTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }
}