using System;
using System.Security.Cryptography;

namespace CycloneGames.IO
{
    internal static class ByteBufferSecurity
    {
        internal static void Clear(byte[] content)
        {
            if (content != null && content.Length > 0)
            {
                CryptographicOperations.ZeroMemory(content.AsSpan());
            }
        }
    }
}
