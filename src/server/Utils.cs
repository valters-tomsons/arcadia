using System.Reflection;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;

namespace Arcadia;

public static class Utils
{
    public static byte[] HexStringToBytes(string value)
    {
        string cleanedRequest = value.Replace(" ", "").Replace("\n", "");
        byte[] byteArray = Enumerable.Range(0, cleanedRequest.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(cleanedRequest.Substring(x, 2), 16))
                             .ToArray();
        return byteArray;
    }

    public static byte[]? ReflectMasterSecretFromBCTls(TlsSecret secret)
    {
        // We need to use reflection to access the master secret from BC
        // because using Extract() destroys the key for subsequent calls
        const BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        var field = typeof(BcTlsSecret).GetField("m_data", bindingFlags);
        return (byte[]?)field?.GetValue(secret);
    }

    public static void DumpCertificate(AsymmetricKeyParameter privateKey, Certificate certificate, string serviceName)
    {
        var prefix = serviceName.Split('.')[0];

        // Export Private Key
        {
            var file = $"{prefix}-private.pem";

            using var textWriter = new StreamWriter(file);
            using var pemWriter = new PemWriter(textWriter);
            pemWriter.WriteObject(privateKey);
            pemWriter.Writer.Flush();
        }

        // Export Certificate
        {
            var file = $"{prefix}-certificate.pem";

            var x509 = new X509Certificate(certificate.GetCertificateAt(0).GetEncoded());

            using var textWriter = new StreamWriter(file);
            using var pemWriter = new PemWriter(textWriter);
            pemWriter.WriteObject(x509);
            pemWriter.Writer.Flush();
        }
    }

    public static byte[][] SplitAt(byte[] source, int index)
    {
        byte[] first = new byte[index];
        byte[] second = new byte[source.Length - index];
        Array.Copy(source, 0, first, 0, index);
        Array.Copy(source, index, second, 0, source.Length - index);
        return new[] { first, second };
    }

    public static Dictionary<string, object> ParseFeslPacketToDict(byte[] data)
    {
        var dataString = Encoding.ASCII.GetString(data);

        var dataSplit = dataString.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x.Replace("\0", string.Empty))).ToArray();
        dataSplit = dataSplit.Select(x => x.Replace("\0", string.Empty)).ToArray();

        var dataDict = new Dictionary<string, object>();
        for (var i = 0; i < dataSplit.Length; i++)
        {
            var entrySplit = dataSplit[i].Split('=', StringSplitOptions.TrimEntries);

            var parameter = entrySplit[0];
            var value = entrySplit.Length > 1 ? entrySplit[1].Replace(@"\", string.Empty) : string.Empty;

            dataDict.TryAdd(parameter, value);
        }

        return dataDict;
    }

    public static StringBuilder DataDictToPacketString(Dictionary<string, object> packetData)
    {
        var dataBuilder = new StringBuilder();

        for (var i = 0; i < packetData.Count; i++)
        {
            var line = packetData.ElementAt(i);
            var parameter = line.Key;
            var value = line.Value;

            dataBuilder.Append(parameter).Append('=');

            if (value.ToString()?.Contains(' ') == true)
            {
                dataBuilder.Append('"').Append(value).Append('"');
            }
            else
            {
                dataBuilder.Append(value);
            }

            dataBuilder.Append('\n');
        }

        if (dataBuilder.Length > 0)
        {
            dataBuilder.Remove(dataBuilder.Length - 1, 1);
        }

        dataBuilder.Append('\0');
        return dataBuilder;
    }

    /// <summary>
    /// Returns index of provided byte pattern in a buffer,
    /// returns -1 if not found
    /// </summary>
    public static int FindBytePattern(byte[] buffer, byte[] searchPattern, int offset = 0)
    {
        int found = -1;
        if (buffer.Length > 0 && searchPattern.Length > 0 && offset <= (buffer.Length - searchPattern.Length) && buffer.Length >= searchPattern.Length)
        {
            for (int i = offset; i <= buffer.Length - searchPattern.Length; i++)
            {
                if (buffer[i] == searchPattern[0])
                {
                    if (buffer.Length > 1)
                    {
                        bool matched = true;
                        for (int y = 1; y <= searchPattern.Length - 1; y++)
                        {
                            if (buffer[i + y] != searchPattern[y])
                            {
                                matched = false;
                                break;
                            }
                        }
                        if (matched)
                        {
                            found = i;
                            break;
                        }
                    }
                    else
                    {
                        found = i;
                        break;
                    }
                }
            }
        }
        return found;
    }
}