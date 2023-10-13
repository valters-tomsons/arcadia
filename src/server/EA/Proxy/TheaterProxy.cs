using System.Net.Sockets;
using System.Text;

namespace Arcadia.EA.Proxy;

public class TheaterProxy
{
    private readonly TcpClient _arcadiaClient;
    private TcpClient? _upstreamClient;
    private NetworkStream _arcadiaStream;
    private NetworkStream? _upstreamStream;

    public TheaterProxy(TcpClient client)
    {
        _arcadiaClient = client;
        _arcadiaStream = _arcadiaClient.GetStream();
    }

    public async Task StartProxy(ArcadiaSettings config)
    {
        InitializeUpstreamClient(config);
        await StartProxying();
    }

    private void InitializeUpstreamClient(ArcadiaSettings config)
    {
        _upstreamClient = new TcpClient("beach-ps3.theater.ea.com", config.TheaterPort);
        _upstreamStream = _upstreamClient.GetStream();
    }

    private async Task StartProxying()
    {
        var clientToUpstreamTask = Task.Run(async () =>
        {
            try
            {
                byte[] buffer = new byte[5120];
                int read;

                while ((read = await _arcadiaStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    Console.WriteLine($"Theater packet received from client: {Encoding.ASCII.GetString(buffer, 0, read)}");
                    await _upstreamStream!.WriteAsync(buffer, 0, read);
                }
            }
            catch { }

            return Task.CompletedTask;
        });

        var upstreamToClientTask = Task.Run(async () =>
        {
            try
            {
                byte[] buffer = new byte[5120];
                int read;

                while ((read = await _upstreamStream!.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    Console.WriteLine($"Theater packet received from server: {Encoding.ASCII.GetString(buffer, 0, read)}");
                    await _arcadiaStream.WriteAsync(buffer, 0, read);
                }
            }
            catch { }

            return Task.CompletedTask;
        });

        await Task.WhenAll(clientToUpstreamTask, upstreamToClientTask);
        Console.WriteLine("Proxy connection closed, exiting...");
    }

    private static Packet? AnalyzeTheaterPacket(byte[] buffer)
    {
        var packet = new Packet(buffer);
        if (string.IsNullOrWhiteSpace(packet.Type))
        {
            return null;
        }

        return packet;
    }
}