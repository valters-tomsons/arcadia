using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
        Analyze(captureFile);
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

    public void Analyze(IStorageFile file)
    {
        var config = new DeviceConfiguration();

        using var captureDevice = new CaptureFileReaderDevice(file.Path.LocalPath);
        captureDevice.Open(config);

        var packets = 0;
        while (captureDevice.GetNextPacket(out var packet) != GetPacketStatus.NoRemainingPackets)
        {
            HandleCapturedPacket(packet);
            packets++;
        }

        Console.WriteLine($"Total packets: {packets}");
    }

    private void HandleCapturedPacket(PacketCapture e)
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

        if (transportPacket is TcpPacket tcpPacket)
        {
            IdentifyTls(tcpPacket.PayloadData);
        }
    }

    private (byte majorVer, byte minorVer) IdentifyTls(byte[] payload)
    {
        if (payload.Length == 0 || payload[0] != 0x16)
        {
            return (0,0);
        }

        var recordLength = (payload[3] << 8) + payload[4];
        var tlsLengthValid = recordLength == (payload.Length - 5);

        if (!tlsLengthValid)
        {
            return (0,0);
        }

        var majorVer = payload[1];
        var minorVer = payload[2];

        if (majorVer == 3 && minorVer == 0)
        {
            Console.WriteLine("> SSLv3");
        }

        return (majorVer, minorVer);
    }
}