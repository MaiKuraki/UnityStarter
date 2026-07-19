using System;
using System.Buffers;
using System.Security.Cryptography;

namespace CycloneGames.IO
{
    internal static class PooledByteBuffer
    {
        internal static byte[] Rent(int minimumLength)
        {
            return ArrayPool<byte>.Shared.Rent(minimumLength);
        }

        internal static void Return(
            byte[] buffer,
            PooledBufferClearMode clearMode,
            int usedLength)
        {
            if (buffer == null)
            {
                return;
            }

            switch (clearMode)
            {
                case PooledBufferClearMode.UsedRegion:
                    int clearLength = Math.Min(Math.Max(usedLength, 0), buffer.Length);
                    if (clearLength > 0)
                    {
                        CryptographicOperations.ZeroMemory(buffer.AsSpan(0, clearLength));
                    }
                    break;
                case PooledBufferClearMode.EntireBuffer:
                    CryptographicOperations.ZeroMemory(buffer.AsSpan());
                    break;
                case PooledBufferClearMode.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(clearMode));
            }

            ArrayPool<byte>.Shared.Return(buffer, false);
        }
    }
}
