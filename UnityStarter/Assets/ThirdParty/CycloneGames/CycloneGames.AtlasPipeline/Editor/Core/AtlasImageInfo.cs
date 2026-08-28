using System;
using System.IO;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Fast, allocation-conscious source image size reader. It only parses PNG/JPEG headers and
    /// never loads a Texture2D into memory, so it is safe to call during preprocess.
    /// </summary>
    public static class AtlasImageInfo
    {
        private const int BufferSize = 64 * 1024;
        private const int HeaderBufferLength = 32;

        [ThreadStatic]
        private static byte[] s_headerBuffer;

        private static byte[] GetHeaderBuffer()
        {
            // Reuse the 32-byte header buffer to avoid one array allocation per file
            // during import. [ThreadStatic] keeps it safe if this is ever called off-thread.
            return s_headerBuffer ?? (s_headerBuffer = new byte[HeaderBufferLength]);
        }

        public static bool TryReadSize(
            string absolutePath,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            {
                return false;
            }

            // Buffered read: JPEG headers can carry large EXIF/ICC segments, and byte-wise
            // reads degrade into one syscall per byte. 64 KB comfortably covers every segment
            // that precedes SOF.
            using (FileStream stream = new FileStream(
                       absolutePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite,
                       BufferSize))
            {
                byte[] header = GetHeaderBuffer();
                int read = stream.Read(header, 0, header.Length);
                if (read < 24)
                {
                    return false;
                }

                if (IsPng(header))
                {
                    width = ReadBigEndianInt32(header, 16);
                    height = ReadBigEndianInt32(header, 20);
                    return width > 0 && height > 0;
                }

                if (IsJpeg(header))
                {
                    return TryReadJpegSize(stream, out width, out height);
                }
            }

            return false;
        }

        private static bool IsPng(byte[] header)
        {
            return header.Length >= 8
                   && header[0] == 0x89
                   && header[1] == 0x50
                   && header[2] == 0x4E
                   && header[3] == 0x47;
        }

        private static bool IsJpeg(byte[] header)
        {
            return header.Length >= 2
                   && header[0] == 0xFF
                   && header[1] == 0xD8;
        }

        private static bool TryReadJpegSize(
            FileStream stream,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            stream.Position = 2;

            while (true)
            {
                if (stream.Position >= stream.Length)
                {
                    return false;
                }

                int markerByte = ReadByte(stream);
                if (markerByte < 0)
                {
                    return false;
                }

                if (markerByte != 0xFF)
                {
                    continue;
                }

                int marker;
                do
                {
                    marker = ReadByte(stream);
                    if (marker < 0)
                    {
                        return false;
                    }
                }
                while (marker == 0xFF);

                if (marker == 0xD8 || marker == 0xD9)
                {
                    continue;
                }

                if (marker == 0xDA)
                {
                    return false;
                }

                byte[] lengthBytes = new byte[2];
                if (ReadExact(stream, lengthBytes, 0, lengthBytes.Length) != lengthBytes.Length)
                {
                    return false;
                }

                int segmentLength = ReadBigEndianUInt16(lengthBytes, 0);
                if (segmentLength < 2)
                {
                    return false;
                }

                if (marker == 0xC0
                    || marker == 0xC1
                    || marker == 0xC2
                    || marker == 0xC3
                    || marker == 0xC5
                    || marker == 0xC6
                    || marker == 0xC7
                    || marker == 0xC9
                    || marker == 0xCA
                    || marker == 0xCB
                    || marker == 0xCD
                    || marker == 0xCE
                    || marker == 0xCF)
                {
                    byte[] sof = new byte[7];
                    if (ReadExact(stream, sof, 0, sof.Length) != sof.Length)
                    {
                        return false;
                    }

                    // SOF segment layout: [0]=precision [1..2]=height [3..4]=width [5]=Nf [6]=component id
                    height = ReadBigEndianUInt16(sof, 1);
                    width = ReadBigEndianUInt16(sof, 3);
                    return width > 0 && height > 0;
                }

                stream.Position += segmentLength - 2;
            }
        }

        private static int ReadByte(Stream stream)
        {
            return stream.ReadByte();
        }

        private static int ReadExact(
            Stream stream,
            byte[] buffer,
            int offset,
            int count)
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
