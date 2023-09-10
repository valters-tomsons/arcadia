using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arcadia.Tls.Crypto;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace dump_pcap;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Opened += OnOpened;
        InitializeComponent();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        using var captureFile = await SelectCaptureFile();
        if (captureFile == null) throw new Exception("No capture file selected!");

        Dictionary<string, string> sessionKeys = new();
        using (var sslKeyFile = await SelectKeyFile())
        {
            if (sslKeyFile != null)
            {
                sessionKeys = await LoadSessionKeys(sslKeyFile);
            }
        }

        Analyze(captureFile, sessionKeys);
    }

    private async Task<IStorageFile?> SelectKeyFile()
    {
        var pickerOptions = new FilePickerOpenOptions
        {
            Title = "Select a SSLKEYLOG file",
            AllowMultiple = false,
            FileTypeFilter = new FilePickerFileType[] {
                new("All files") {
                    Patterns = new [] { "*.*", }
                }
            }
        };

        var topLevel = GetTopLevel(this) ?? throw new Exception("MainWindow.GetTopLevel() failed!");
        var selection = await topLevel.StorageProvider.OpenFilePickerAsync(pickerOptions);

        return selection?.FirstOrDefault();
    }

    public async Task<IStorageFile?> SelectCaptureFile()
    {
        var pickerOptions = new FilePickerOpenOptions
        {
            Title = "Select a capture file",
            AllowMultiple = false,
            FileTypeFilter = new FilePickerFileType[] {
                new("Capture files") {
                    Patterns = new [] { "*.cap", "*.pcapng" }
                }
            }
        };

        var topLevel = GetTopLevel(this) ?? throw new Exception("MainWindow.GetTopLevel() failed!");
        var selection = await topLevel.StorageProvider.OpenFilePickerAsync(pickerOptions);

        return selection?.FirstOrDefault();
    }

    public void Analyze(IStorageFile captureFile, Dictionary<string, string> sessionKeys)
    {
        var config = new DeviceConfiguration();

        using var captureDevice = new CaptureFileReaderDevice(captureFile.Path.LocalPath);
        captureDevice.Open(config);

        var packets = 0;
        while (captureDevice.GetNextPacket(out var packet) != GetPacketStatus.NoRemainingPackets)
        {
            HandleCapturedPacket(packet, sessionKeys);
            packets++;
        }

        Console.WriteLine($"Total packets: {packets}");
    }

    private async Task<Dictionary<string, string>> LoadSessionKeys(IStorageFile keyFile)
    {
        var sessionKeys = new Dictionary<string, string>();
        var lines = await File.ReadAllLinesAsync(keyFile.Path.LocalPath);

        foreach(var line in lines)
        {
            if (!line.StartsWith("CLIENT_RANDOM")) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) continue;

            var clientRandom = parts[1];
            var sessionKey = parts[2];
            sessionKeys.Add(clientRandom, sessionKey);
        }

        return sessionKeys;
    }

    private void HandleCapturedPacket(PacketCapture e, Dictionary<string, string> sessionKeys)
    {
        var capture = e.GetPacket();
        var ethPacket = Packet.ParsePacket(capture.LinkLayerType, capture.Data) as EthernetPacket;

        if (ethPacket?.PayloadPacket is not IPPacket ipPacket || ipPacket?.PayloadPacket is not TransportPacket transportPacket)
        {
            return;
        }

        var source = ipPacket.SourceAddress;
        var sourcePort = transportPacket.SourcePort;
        var dest = ipPacket.DestinationAddress;
        var destPort = transportPacket.DestinationPort;
        Console.WriteLine($"{e.Header.Timeval} | {source}:{sourcePort} -> {dest}:{destPort}");

        if (transportPacket is TcpPacket tcpPacket && IsSslV3Packet(tcpPacket.PayloadData))
        {
            InitializeCipher(tcpPacket.PayloadData, sessionKeys);
        }
    }

    private void InitializeCipher(byte[] payload, Dictionary<string, string> sessionKeys)
    {
        var clientRandom = Crypto.ExtractClientRandomFromClientHello(payload);
        if (clientRandom is null) return;

        var clientKey = BitConverter.ToString(clientRandom).Replace("-", "");

        sessionKeys.TryGetValue(clientKey, out var sessionKeyString);
        if (sessionKeyString is null) return;

        // var crypto = new BcTlsCrypto();
        // crypto.CreateCipher(new Rc4TlsCrypto());

        // var kk = new KeyParameter()
    }

    private void Decrypt(byte[] payload, Dictionary<string, string> sessionKeys)
    {
        var crypto = new Rc4TlsCrypto();
    }

    private bool IsSslV3Packet(byte[] payload)
    {
        if (payload.Length < 5)
        {
            return false;
        }

        var recordLength = (payload[3] << 8) + payload[4];
        var tlsLengthValid = recordLength == (payload.Length - 5);

        if (!tlsLengthValid)
        {
            return false;
        }

        if (payload[1] != 3 && payload[2] != 0)
        {
            return false;
        }

        return true;
    }
}