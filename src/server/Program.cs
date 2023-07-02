using System.Net.Sockets;
using System.Security.Cryptography;

var server = new TcpListener(System.Net.IPAddress.Any, 18800);
server.Start();

var bufferStream = new MemoryStream();
var buffer = new byte[1024];

while(true)
{
    var client = await server.AcceptTcpClientAsync();
    var stream = client.GetStream();

    while(stream.DataAvailable)
    {
        var bytes = await stream.ReadAsync(buffer);
        await bufferStream.WriteAsync(buffer.AsMemory(0, bytes));

        var dataHash = SHA256.HashData(bufferStream.ToArray().Take(bytes).ToArray());
        var hash = BitConverter.ToString(dataHash).Replace("-", "").ToLower();
        Console.WriteLine(hash);
    }

    bufferStream.Position = 0;
}