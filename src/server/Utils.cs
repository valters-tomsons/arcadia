using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Arcadia;

public static class Utils
{
    public static byte[] HexStringToBytes(string value)
    {
        string cleanedRequest = value.Replace(" ", string.Empty).Replace("\n", string.Empty);
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

    public static byte[][] SplitAt(byte[] source, int index)
    {
        byte[] first = new byte[index];
        byte[] second = new byte[source.Length - index];

        Array.Copy(source, 0, first, 0, index);
        Array.Copy(source, index, second, 0, source.Length - index);

        return [first, second];
    }

    public static Dictionary<string, string> ParseFeslPacketToDict(byte[] data)
    {
        var dataString = Encoding.ASCII.GetString(data);

        var dataSplit = dataString.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x.Replace("\0", string.Empty))).ToArray();
        dataSplit = dataSplit.Select(x => x.Replace("\0", string.Empty)).ToArray();

        var dataDict = new Dictionary<string, string>();
        for (var i = 0; i < dataSplit.Length; i++)
        {
            var entrySplit = dataSplit[i].Split('=', StringSplitOptions.TrimEntries);

            var parameter = entrySplit[0];
            var value = entrySplit.Length > 1 ? entrySplit[1].Replace(@"\", string.Empty) : string.Empty;

            dataDict.TryAdd(parameter, value);
        }

        return dataDict;
    }

    public static StringBuilder DataDictToPacketString(Dictionary<string, string> packetData)
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
    public static int FindBytePattern(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> searchPattern, int offset = 0)
    {
        if (searchPattern.IsEmpty || buffer.Length < searchPattern.Length || offset > buffer.Length - searchPattern.Length)
        {
            return -1;
        }

        int endIndex = buffer.Length - searchPattern.Length + 1;
        for (int i = offset; i < endIndex; i++)
        {
            if (buffer[i] == searchPattern[0] && buffer.Slice(i, searchPattern.Length).SequenceEqual(searchPattern))
            {
                return i;
            }
        }

        return -1;
    }

    public static void RemoveItemFromBag<T>(this ConcurrentBag<T> bag, T item)
    {
        while (!bag.IsEmpty)
        {
            bag.TryTake(out T? result);
            if (result is null) continue;
            if (result.Equals(item))
            {
                break;
            }

            bag.Add(result);
        }
    }

    public static void AddEntitlements(this Dictionary<string, string> response, long userId, (string Group, string Tag, long Id)[] entitlements)
    {
        for (var i = 0; i < entitlements.Length; i++)
        {
            var (Group, Tag, Id) = entitlements[i];

            response.Add($"entitlements.{i}.entitlementId", $"{Id}");
            response.Add($"entitlements.{i}.entitlementTag", Tag);
            response.Add($"entitlements.{i}.groupName", Group);
            response.Add($"entitlements.{i}.grantDate", "2023-12-08T23:59Z");
            response.Add($"entitlements.{i}.status", "ACTIVE");
            response.Add($"entitlements.{i}.userId", $"{userId}");
            response.Add($"entitlements.{i}.version", "0");
            response.Add($"entitlements.{i}.productId", string.Empty);
            response.Add($"entitlements.{i}.statusReasonCode", string.Empty);
            response.Add($"entitlements.{i}.terminationDate", string.Empty);
        }

        response.Add("entitlements.[]", $"{entitlements.Length}");
    }
}