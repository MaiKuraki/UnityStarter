using System;
using System.Text;

namespace CycloneGames.IO
{
    /// <summary>
    /// Strict deterministic text decoding. BOM-less content uses exactly one caller-selected encoding.
    /// </summary>
    public static class TextCodec
    {
        private static readonly Encoding Utf8NoBomEncoding = new UTF8Encoding(false, true);
        private static readonly Encoding Utf16LittleEndian = new UnicodeEncoding(false, true, true);
        private static readonly Encoding Utf16BigEndian = new UnicodeEncoding(true, true, true);
        private static readonly Encoding Utf32LittleEndian = new UTF32Encoding(false, true);
        private static readonly Encoding Utf32BigEndian = new UTF32Encoding(true, true);

        public static Encoding Utf8NoBom => Utf8NoBomEncoding;

        public static string Decode(
            byte[] content,
            Encoding fallbackEncoding = null,
            bool detectByteOrderMark = true)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return Decode(content.AsSpan(), fallbackEncoding, detectByteOrderMark);
        }

        public static string Decode(
            ReadOnlySpan<byte> content,
            Encoding fallbackEncoding = null,
            bool detectByteOrderMark = true)
        {
            if (detectByteOrderMark
                && TryDetectByteOrderMark(content, out Encoding encoding, out int preambleLength))
            {
                return encoding.GetString(content.Slice(preambleLength));
            }

            Encoding strictEncoding = GetStrictEncoding(fallbackEncoding ?? Utf8NoBomEncoding);
            return strictEncoding.GetString(content);
        }

        public static bool TryDecode(
            byte[] content,
            out string text,
            Encoding fallbackEncoding = null,
            bool detectByteOrderMark = true)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return TryDecode(content.AsSpan(), out text, fallbackEncoding, detectByteOrderMark);
        }

        public static bool TryDecode(
            ReadOnlySpan<byte> content,
            out string text,
            Encoding fallbackEncoding = null,
            bool detectByteOrderMark = true)
        {
            try
            {
                text = Decode(content, fallbackEncoding, detectByteOrderMark);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                return false;
            }
        }

        public static byte[] Encode(
            string text,
            Encoding encoding = null,
            bool includeByteOrderMark = false)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            Encoding strictEncoding = GetStrictEncoding(encoding ?? Utf8NoBomEncoding);
            byte[] content = strictEncoding.GetBytes(text);
            if (!includeByteOrderMark)
            {
                return content;
            }

            byte[] preamble = strictEncoding.GetPreamble();
            if (preamble.Length == 0)
            {
                return content;
            }

            var result = new byte[checked(preamble.Length + content.Length)];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
            return result;
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

        private static bool TryDetectByteOrderMark(
            ReadOnlySpan<byte> content,
            out Encoding encoding,
            out int preambleLength)
        {
            if (content.Length >= 4)
            {
                if (content[0] == 0xFF && content[1] == 0xFE && content[2] == 0x00 && content[3] == 0x00)
                {
                    encoding = Utf32LittleEndian;
                    preambleLength = 4;
                    return true;
                }

                if (content[0] == 0x00 && content[1] == 0x00 && content[2] == 0xFE && content[3] == 0xFF)
                {
                    encoding = Utf32BigEndian;
                    preambleLength = 4;
                    return true;
                }
            }

            if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            {
                encoding = Utf8NoBomEncoding;
                preambleLength = 3;
                return true;
            }

            if (content.Length >= 2)
            {
                if (content[0] == 0xFE && content[1] == 0xFF)
                {
                    encoding = Utf16BigEndian;
                    preambleLength = 2;
                    return true;
                }

                if (content[0] == 0xFF && content[1] == 0xFE)
                {
                    encoding = Utf16LittleEndian;
                    preambleLength = 2;
                    return true;
                }
            }

            encoding = null;
            preambleLength = 0;
            return false;
        }
    }
}
