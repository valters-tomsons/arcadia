using System;

namespace dump_pcap;

public static class Crypto
{
    public static byte[]? ExtractClientRandomFromClientHello(byte[] payloadData)
    {
        // Check for TLS Handshake Client Hello
        if (payloadData[0] != 0x16 || payloadData[5] != 0x01)
        {
            return null;
        }

        var clientRandom = payloadData[11..43];
        Console.WriteLine($"Handshake secret: {BitConverter.ToString(clientRandom)}");

        return clientRandom;
    }
}