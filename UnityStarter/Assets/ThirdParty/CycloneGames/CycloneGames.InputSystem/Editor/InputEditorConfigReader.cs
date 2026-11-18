using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Bounded strict UTF-8 whole-file read for the Input System Editor.
    /// Reads through a sequential shared FileStream, verifies the exact length,
    /// does not strip a BOM, and always zeroes the temporary byte buffer.
    /// </summary>
    internal static class InputEditorConfigReader
    {
        internal static string ReadStrictUtf8(
            string path,
            int maxByteCount,
            Encoding strictUtf8)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Path cannot be null, empty, or whitespace.",
                    nameof(path));
            }

            if (maxByteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxByteCount));
            }

            byte[] content;
            using (var source = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                long length = source.Length;
                if (length < 0L || length > maxByteCount || length > int.MaxValue)
                {
                    throw new IOException(
                        $"File '{Path.GetFileName(path)}' is {length} bytes and exceeds the {maxByteCount}-byte read limit.");
                }

                content = new byte[(int)length];
                try
                {
                    int offset = 0;
                    while (offset < content.Length)
                    {
                        int read = source.Read(content, offset, content.Length - offset);
                        if (read == 0)
                        {
                            throw new EndOfStreamException(
                                $"File ended before {content.Length} bytes were read.");
                        }

                        offset += read;
                    }

                    if (source.ReadByte() >= 0)
                    {
                        throw new IOException("File length changed while it was being read.");
                    }
                }
                catch
                {
                    Clear(content);
                    throw;
                }
            }

            try
            {
                return GetStrictEncoding(strictUtf8).GetString(content);
            }
            finally
            {
                Clear(content);
            }
        }

        /// <summary>
        /// Zeroes a temporary buffer. Kept internal so EditMode tests can assert that
        /// the failure and success paths leave no readable configuration bytes behind.
        /// </summary>
        internal static void Clear(byte[] content)
        {
            if (content != null && content.Length > 0)
            {
                CryptographicOperations.ZeroMemory(content.AsSpan());
            }
        }

        private static Encoding GetStrictEncoding(Encoding encoding)
        {
            if (encoding.DecoderFallback == DecoderFallback.ExceptionFallback
                && encoding.EncoderFallback == EncoderFallback.ExceptionFallback)
            {
                return encoding;
            }

            Encoding strictEncoding = (Encoding)encoding.Clone();
            strictEncoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            strictEncoding.EncoderFallback = EncoderFallback.ExceptionFallback;
            return strictEncoding;
        }
    }
}
