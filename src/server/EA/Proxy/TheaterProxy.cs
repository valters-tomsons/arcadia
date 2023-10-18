using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.EA.Proxy;

public class TheaterProxy
{
    private readonly ILogger<TheaterProxy> _logger;
    
    private readonly ArcadiaSettings _arcadiaSettings;

    private TcpClient? _arcadiaClient;
    private TcpClient? _upstreamClient;
    private NetworkStream? _arcadiaStream;
    private NetworkStream? _upstreamStream;

    public TheaterProxy(ILogger<TheaterProxy> logger, IOptions<ArcadiaSettings> arcadiaSettings)
    {
        _logger = logger;
        _arcadiaSettings = arcadiaSettings.Value;
    }

    public async Task StartProxy(TcpClient client)
    {
        _arcadiaClient = client;
        _arcadiaStream = _arcadiaClient.GetStream();

        InitializeUpstreamClient();

        if (_arcadiaStream is null || _upstreamStream is null)
            throw new Exception("Streams are null");

        await StartProxying();
    }

    private void InitializeUpstreamClient()
    {
        _logger.LogInformation($"Connecting to upstream {_arcadiaSettings.TheaterAddress}:{_arcadiaSettings.TheaterPort}");
        
        _upstreamClient = new TcpClient(_arcadiaSettings.TheaterAddress, _arcadiaSettings.TheaterPort);
        _upstreamStream = _upstreamClient.GetStream();
    }

    private async Task StartProxying()
    {
        var clientToUpstreamTask = Task.Run(async () =>
        {
            try
            {
                byte[] buffer = new byte[8096];
                int read;

                while ((read = await _arcadiaStream!.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    _logger.LogDebug($"Theater packet received from client: {Encoding.ASCII.GetString(buffer, 0, read)}");
                    await _upstreamStream!.WriteAsync(buffer, 0, read);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to proxy data from client: {e.Message}");
            }

            return Task.CompletedTask;
        });

        var upstreamToClientTask = Task.Run(async () =>
        {
            try
            {
                byte[] buffer = new byte[8096];
                int read;

                while ((read = await _upstreamStream!.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    _logger.LogDebug($"Theater packet received from server: {Encoding.ASCII.GetString(buffer, 0, read)}");
                    await _arcadiaStream!.WriteAsync(buffer, 0, read);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to proxy data from upstream: {e.Message}");
            }

            return Task.CompletedTask;
        });

        await Task.WhenAny(clientToUpstreamTask, upstreamToClientTask);
        _logger.LogInformation("Proxy connection closed");
    }
}