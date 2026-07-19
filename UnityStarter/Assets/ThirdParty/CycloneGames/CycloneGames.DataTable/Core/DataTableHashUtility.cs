using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CycloneGames.DataTable
{
    public static class DataTableHashUtility
    {
        private static readonly char[] HexChars =
        {
            '0', '1', '2', '3', '4', '5', '6', '7',
            '8', '9', 'a', 'b', 'c', 'd', 'e', 'f',
        };

        public static string ComputeSha256Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            return ComputeSha256Hex(new ReadOnlyMemory<byte>(bytes));
        }

        public static string ComputeSha256Hex(ReadOnlyMemory<byte> bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash;
                if (MemoryMarshal.TryGetArray(bytes, out ArraySegment<byte> segment))
                {
                    byte[] source = segment.Array ?? Array.Empty<byte>();
                    hash = sha256.ComputeHash(source, segment.Offset, segment.Count);
                }
                else
                {
                    byte[] copy = bytes.ToArray();
                    hash = sha256.ComputeHash(copy);
                }

                return ToLowerHex(hash);
            }
        }

        public static bool Sha256Matches(byte[] bytes, string expectedSha256Hex)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            return Sha256Matches(new ReadOnlyMemory<byte>(bytes), expectedSha256Hex);
        }

        public static bool Sha256Matches(ReadOnlyMemory<byte> bytes, string expectedSha256Hex)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256Hex))
            {
                return false;
            }

            string actualHash = ComputeSha256Hex(bytes);
            return string.Equals(
                actualHash,
                NormalizeSha256Hex(expectedSha256Hex),
                StringComparison.Ordinal);
        }

        public static string NormalizeSha256Hex(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        private static string ToLowerHex(byte[] bytes)
        {
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = bytes[i];
                chars[i * 2] = HexChars[value >> 4];
                chars[(i * 2) + 1] = HexChars[value & 0x0F];
            }

            return new string(chars);
        }
    }
}
