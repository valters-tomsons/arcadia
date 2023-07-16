﻿using System.Net.Sockets;
using Org.BouncyCastle.Tls;
using Arcadia;
using Arcadia.Fesl;
using Arcadia.Tls;
using Arcadia.Tls.Crypto;
using Arcadia.Tls.Misc;

var config = Utils.BuildConfig();

var IssuerDN = string.Empty;
var SubjectDN = string.Empty;
if (config.MirrorUpstreamCert)
{
    Console.WriteLine($"Upstream cert mirroring enabled: {config.UpstreamHost}");
    (IssuerDN, SubjectDN) = TlsCertDumper.DumpPubFeslCert(config.UpstreamHost, config.Port);
}

var (feslCertKey, feslPubCert) = ProtoSslCertGenerator.GenerateVulnerableCert(IssuerDN, SubjectDN);
if(config.DumpPatchedCert)
{
    Console.WriteLine("Dumping patched certificate...");
    Utils.DumpCertificate(feslCertKey, feslPubCert, config.UpstreamHost);
}

var arcadiaTcpListener = new TcpListener(System.Net.IPAddress.Any, config.Port);
var arcadiaTlsCrypto = new Rc4TlsCrypto(true);

arcadiaTcpListener.Start();
Console.WriteLine($"Listening on tcp:{config.Port}");

if (config.EnableProxyMode)
{
    Console.WriteLine("Proxy mode enabled!");
}

while(true)
{
    var tcpClient = await arcadiaTcpListener.AcceptTcpClientAsync();
    var clientEndpoint = tcpClient.Client.RemoteEndPoint!.ToString()!;
    Console.WriteLine($"Opening connection from: {clientEndpoint}");

    // TODO: Run this in a separate thread.
    HandleClientConnection(tcpClient, clientEndpoint);
}

async void HandleClientConnection(TcpClient tcpClient, string clientEndpoint)
{
    var networkStream = tcpClient.GetStream();

    var connTls = new Ssl3TlsServer(arcadiaTlsCrypto, feslPubCert, feslCertKey);
    var arcadiaServerProtocol = new TlsServerProtocol(networkStream);

    try
    {
        arcadiaServerProtocol.Accept(connTls);
        Console.WriteLine("SSL Handshake with game client successful!");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Failed to accept connection: {e.Message}");

        arcadiaServerProtocol.Flush();
        arcadiaServerProtocol.Close();
        connTls.Cancel();

        return;
    }

    if (config.EnableProxyMode)
    {
        var proxy = new TlsClientProxy(arcadiaServerProtocol, arcadiaTlsCrypto);
        await proxy.StartProxy(config);

        return;
    }

    Console.WriteLine("Starting arcadia-emu FESL session");

    var readBuffer = new byte[4096];
    while (arcadiaServerProtocol.IsConnected)
    {
        int read;

        try
        {
            read = arcadiaServerProtocol.ReadApplicationData(readBuffer, 0, readBuffer.Length);
        }
        catch
        {
            Console.WriteLine($"Connection has been closed with {clientEndpoint}");
            break;
        }

        if (read == 0)
        {
            continue;
        }

        var packet = new FeslPacket(readBuffer[..read]);
        Console.WriteLine($"Type: {packet.Type}");
    }
}