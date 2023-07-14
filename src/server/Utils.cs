using System.Reflection;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace server;

public static class Utils
{
    public static byte[]? ReflectMasterSecretFromBCTls(TlsSecret secret)
    {
        // We need to use reflection to access the master secret from BC
        // because using Extract() destroys the key for subsequent calls
        const BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        var field = typeof(BcTlsSecret).GetField("m_data", bindingFlags);
        return (byte[]?)field?.GetValue(secret);
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