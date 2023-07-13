using System.Reflection;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace server;

public static class BCUtils
{
    public static byte[]? ReflectMasterSecret(TlsSecret secret)
    {
        // We need to use reflection to access the master secret from BC
        // because using Extract() destroys the key for subsequent calls
        const BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        var field = typeof(BcTlsSecret).GetField("m_data", bindingFlags);
        return (byte[]?)field?.GetValue(secret);
    }
}

public static class ByteSearch
{
    /// <summary>
    /// Returns index of provided byte pattern in a buffer,
    /// returns -1 if not found
    /// </summary>
    public static int FindPattern(byte[] buffer, byte[] searchPattern, int offset = 0)
    {
        int found = -1;
        //only look at this if we have a populated search array and search bytes with a sensible start
        if (buffer.Length > 0 && searchPattern.Length > 0 && offset <= (buffer.Length - searchPattern.Length) && buffer.Length >= searchPattern.Length)
        {
            //iterate through the array to be searched
            for (int i = offset; i <= buffer.Length - searchPattern.Length; i++)
            {
                //if the start bytes match we will start comparing all other bytes
                if (buffer[i] == searchPattern[0])
                {
                    if (buffer.Length > 1)
                    {
                        //multiple bytes to be searched we have to compare byte by byte
                        bool matched = true;
                        for (int y = 1; y <= searchPattern.Length - 1; y++)
                        {
                            if (buffer[i + y] != searchPattern[y])
                            {
                                matched = false;
                                break;
                            }
                        }
                        //everything matched up
                        if (matched)
                        {
                            found = i;
                            break;
                        }
                    }
                    else
                    {
                        //search byte is only one bit nothing else to do
                        found = i;
                        break; //stop the loop
                    }
                }
            }
        }
        return found;
    }
}