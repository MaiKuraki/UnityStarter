using System;
using System.IO;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Fast, allocation-free source image size reader. It parses only the PNG/JPEG headers and never
    /// loads a Texture2D, so it is safe to call from a preprocess hook on every imported image.
    /// </summary>
    /// <remarks>
    /// Disk strategy: one sequential read of a small window, then all parsing happens in memory.
    /// The previous implementation walked the JPEG marker chain with per-byte
    /// <see cref="Stream.ReadByte"/> calls and allocated a fresh 2-byte and 7-byte array for every
    /// file; at tens of thousands of images that turned a metadata query into tens of thousands of
    /// short-lived allocations. A 4 KB window covers essentially every real-world file, and the rare
    /// image with a multi-kilobyte EXIF or ICC payload falls back to one wider read instead of
    /// failing.
    /// Memory safety: every marker walk is bounds-checked against the number of bytes actually read,
    /// never against the buffer length, so a truncated or hostile file cannot read past the data.
    /// </remarks>
    public static class AtlasImageInfo
    {
        /// <summary>Window that covers virtually every PNG and JPEG header, ICC payload included.</summary>
        private const int FastScanLength = 4096;

        /// <summary>Fallback window for images whose metadata segments are unusually large.</summary>
        private const int DeepScanLength = 64 * 1024;

        /// <summary>Bytes needed to read a PNG IHDR: 8 signature + 4 length + 4 type + 4 width + 4 height.</summary>
        private const int MinimumHeaderLength = 24;

        [ThreadStatic]
        private static byte[] s_scanBuffer;

        private static byte[] GetScanBuffer(int minimumLength)
        {
            byte[] buffer = s_scanBuffer;
            if (buffer == null || buffer.Length < minimumLength)
            {
                buffer = new byte[minimumLength];
                s_scanBuffer = buffer;
            }

            return buffer;
        }

        public static bool TryReadSize(
            string absolutePath,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(absolutePath))
            {
                return false;
            }

            if (!File.Exists(absolutePath))
            {
                return false;
            }

            if (TryReadSizeCore(
                    absolutePath,
                    FastScanLength,
                    out width,
                    out height,
                    out bool windowExhausted))
            {
                return true;
            }

            // The window filled up before the frame header was reached. Widen once rather than
            // reporting failure: a large EXIF block is common in art exported straight from a DCC
            // tool, and silently treating those images as "unknown size" would disable the
            // oversize guard exactly where it matters.
            if (!windowExhausted)
            {
                return false;
            }

            return TryReadSizeCore(
                absolutePath,
                DeepScanLength,
                out width,
                out height,
                out _);
        }

        private static bool TryReadSizeCore(
            string absolutePath,
            int windowLength,
            out int width,
            out int height,
            out bool windowExhausted)
        {
            width = 0;
            height = 0;
            windowExhausted = false;

            byte[] buffer = GetScanBuffer(windowLength);
            int read;
            try
            {
                using (var stream = new FileStream(
                           absolutePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite,
                           windowLength,
                           FileOptions.SequentialScan))
                {
                    read = ReadAtLeast(stream, buffer, 0, windowLength);
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (read < MinimumHeaderLength)
            {
                return false;
            }

            windowExhausted = read >= windowLength;

            if (IsPng(buffer))
            {
                width = ReadBigEndianInt32(buffer, 16);
                height = ReadBigEndianInt32(buffer, 20);
                return width > 0 && height > 0;
            }

            if (!IsJpeg(buffer))
            {
                return false;
            }

            return TryParseJpegSize(buffer, read, out width, out height);
        }

        /// <summary>
        /// Walks the JPEG marker chain inside the already-read window. No stream access and no
        /// allocation: the segment length and the SOF payload are decoded straight from the buffer.
        /// </summary>
        private static bool TryParseJpegSize(byte[] buffer, int length, out int width, out int height)
        {
            width = 0;
            height = 0;

            int position = 2;
            while (position + 4 <= length)
            {
                if (buffer[position] != 0xFF)
                {
                    position++;
                    continue;
                }

                byte marker = buffer[position + 1];

                // 0xFF padding bytes precede an actual marker; skip them without consuming the marker.
                if (marker == 0xFF)
                {
                    position++;
                    continue;
                }

                // Standalone markers carry no length payload. Restart markers (D0-D7) and the
                // "no operation" marker (01) must be handled here, otherwise their data bytes would
                // be misread as a segment length and desynchronize the walk.
                if (IsStandaloneMarker(marker))
                {
                    position += 2;
                    continue;
                }

                // Start of scan: the frame header, if any, is already behind us.
                if (marker == 0xDA)
                {
                    return false;
                }

                int segmentLength = ReadBigEndianUInt16(buffer, position + 2);
                if (segmentLength < 2)
                {
                    return false;
                }

                if (IsStartOfFrameMarker(marker))
                {
                    // SOF payload: [0]=sample precision, [1..2]=height, [3..4]=width.
                    int payload = position + 4;
                    if (payload + 5 > length)
                    {
                        return false;
                    }

                    height = ReadBigEndianUInt16(buffer, payload + 1);
                    width = ReadBigEndianUInt16(buffer, payload + 3);
                    return width > 0 && height > 0;
                }

                position += 2 + segmentLength;
            }

            return false;
        }

        private static bool IsStandaloneMarker(byte marker)
        {
            if (marker == 0x01)
            {
                return true;
            }

            return marker == 0xD8
                   || marker == 0xD9
                   || (marker >= 0xD0 && marker <= 0xD7);
        }

        private static bool IsStartOfFrameMarker(byte marker)
        {
            // C4, C8 and CC are Huffman / arithmetic-coding table markers, not frame headers, so they
            // must be excluded even though they sit inside the same numeric range.
            switch (marker)
            {
                case 0xC0:
                case 0xC1:
                case 0xC2:
                case 0xC3:
                case 0xC5:
                case 0xC6:
                case 0xC7:
                case 0xC9:
                case 0xCA:
                case 0xCB:
                case 0xCD:
                case 0xCE:
                case 0xCF:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPng(byte[] header)
        {
            return header[0] == 0x89
                   && header[1] == 0x50
                   && header[2] == 0x4E
                   && header[3] == 0x47;
        }

        private static bool IsJpeg(byte[] header)
        {
            return header[0] == 0xFF && header[1] == 0xD8;
        }

        private static int ReadAtLeast(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24)
                   | (bytes[offset + 1] << 16)
                   | (bytes[offset + 2] << 8)
                   | bytes[offset + 3];
        }

        private static int ReadBigEndianUInt16(byte[] bytes, int offset)
        {
            return (bytes[offset] << 8) | bytes[offset + 1];
        }
    }
}
