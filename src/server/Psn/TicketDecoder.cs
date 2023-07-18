using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace Arcadia.Psn;

public class TicketDecoder
{
    private const string RpcnPub = @"
    -----BEGIN PUBLIC KEY-----
    ME4wEAYHKoZIzj0CAQYFK4EEACADOgAEsHvA8K3bl2V+nziQOejSucl9wqMdMELn
    0Eebk9gcQrCr32xCGRox4x+TNC+PAzvVKcLFf9taCn0=
    -----END PUBLIC KEY-----";

    private readonly ECPublicKeyParameters publicKey;

    public TicketDecoder()
    {
        using var stringReader = new StringReader(RpcnPub);
        var pemReader = new PemReader(stringReader);
        publicKey = (ECPublicKeyParameters)pemReader.ReadObject();
    }

    public Ticket DecodeFromASCIIString(string ticketData)
    {
        var rawTicket = Encoding.ASCII.GetBytes(ticketData);

        using var ms = new MemoryStream(rawTicket);
        using var br = new BinaryReader(ms);

        var ticket = new Ticket
        {
            Version = br.ReadUInt32(),
            Size = br.ReadUInt32(),
            Data = new List<TicketData>()
        };

        while (ms.Position < ms.Length)
        {
            ticket.Data.Add(ReadTicketData(br));
        }

        return ticket;
    }

    public bool VerifySignature(Ticket ticket)
    {
        if (ticket.Data.Count < 2)
        {
            Console.WriteLine("PSN: Ticket data count is less than 2");
            return false;
        }

        var userBlob = ticket.Data[0];
        var signature = ticket.Data[1];

        // The ticket signature data is the second blob data
        var signatureData = signature.Data.Skip(4).ToArray();

        // The signature is the last 56 bytes
        var sig = signatureData.Skip(signatureData.Length - 56).ToArray();

        // The remaining data is the signed blob
        var signedData = signatureData.Take(signatureData.Length - 56).ToArray();

        // Compare this with user blob's binary data
        var userData = userBlob.Data.Skip(4).ToArray();

        if (!signedData.SequenceEqual(userData))
        {
            Console.WriteLine("PSN: Signed data does not match user blob data");
            return false;
        }

        var signer = SignerUtilities.GetSigner("SHA-256withECDSA");
        signer.Init(false, publicKey);
        signer.BlockUpdate(userData, 0, userData.Length);
        return signer.VerifySignature(sig);
    }

    private static TicketData ReadTicketData(BinaryReader br)
    {
        var ticketData = new TicketData
        {
            Id = br.ReadUInt16(),
            Len = br.ReadUInt16(),
        };

        ticketData.Type = ticketData.Id switch
        {
            0 => TicketType.Empty,
            1 => TicketType.U32,
            2 => TicketType.U64,
            4 => TicketType.BString,
            7 => TicketType.Time,
            8 => TicketType.Binary,
            _ => ticketData.Id >= 0x3000 ? TicketType.Blob : throw new Exception($"Unexpected ticket data id: {ticketData.Id}")
        };

        ticketData.Data = br.ReadBytes(ticketData.Len);
        return ticketData;
    }
}