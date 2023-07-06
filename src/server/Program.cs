using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using server;

const int tcpPort = 18800;

var server = new TcpListener(System.Net.IPAddress.Any, tcpPort);
server.Start();

Console.WriteLine($"Server listening on :{tcpPort}");

var readBuffer = new byte[1024];

while(true)
{
    var client = await server.AcceptTcpClientAsync();
    var stream = client.GetStream();

    var cert = new X509Certificate2("fesl_vuln.pfx", "123456");
    var sslStream = new SslStream(stream, false);

    // await sslStream.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.Ssl3, false);
    await sslStream.AuthenticateAsServerAsync(cert);

    List<Packet> dataObjs = new();

    while(stream.DataAvailable)
    {
        var read = await sslStream.ReadAsync(readBuffer);
        var buffer = readBuffer[..read];

        Console.WriteLine($"Received {read} bytes from client");
        Console.WriteLine(Encoding.ASCII.GetString(buffer));
        Console.WriteLine(Encoding.Unicode.GetString(buffer));
        Console.WriteLine(Encoding.UTF8.GetString(buffer));

        var packetType = buffer[..4];

        Console.WriteLine($"Packet Type: {Encoding.ASCII.GetString(packetType)}");

	await Task.Delay(2500);

        Console.WriteLine("Heading to a crash!");

        var packets = SplitByteArray(buffer, Encoding.Unicode.GetBytes("\n\x00"));

        if (packets.Length > 2)
        {
            foreach (var packet in packets)
            {
                var fixedPacketType = packet[..4];
                var fixedPacket = packet[12..];

                if (fixedPacket.Length == 0)
                {
                    break;
                }
                else
                {
                    var pkgData = Encoding.Unicode.GetBytes("\n\x00");
                    var kk = fixedPacket.Concat(pkgData).ToArray();
                    var pkk = new Packet(kk).DataInterpreter();
                    Console.WriteLine($"data: {fixedPacket}, type: {fixedPacketType}");
                }
            }
        }
        else{
            var pkgData = Encoding.Unicode.GetBytes("\n\x00");
            var kk = packets[0][12..].Concat(pkgData).ToArray();
            var pkk = new Packet(kk).DataInterpreter();
        }
    }
}

static byte[][] SplitByteArray(byte[] data, byte[] delimiter)
{
    List<byte[]> packets = new List<byte[]>();

    int startIndex = 0;
    int delimiterIndex = Array.IndexOf(data, delimiter[0], startIndex);

    while (delimiterIndex != -1 && delimiterIndex + delimiter.Length <= data.Length)
    {
        bool isDelimiterMatch = true;

        for (int i = 1; i < delimiter.Length; i++)
        {
            if (data[delimiterIndex + i] != delimiter[i])
            {
                isDelimiterMatch = false;
                break;
            }
        }

        if (isDelimiterMatch)
        {
            int packetLength = delimiterIndex - startIndex;
            byte[] packet = new byte[packetLength];
            Array.Copy(data, startIndex, packet, 0, packetLength);
            packets.Add(packet);
            startIndex = delimiterIndex + delimiter.Length;
        }

        delimiterIndex = Array.IndexOf(data, delimiter[0], startIndex);
    }

    if (startIndex < data.Length)
    {
        int packetLength = data.Length - startIndex;
        byte[] packet = new byte[packetLength];
        Array.Copy(data, startIndex, packet, 0, packetLength);
        packets.Add(packet);
    }

    return packets.ToArray();
}
