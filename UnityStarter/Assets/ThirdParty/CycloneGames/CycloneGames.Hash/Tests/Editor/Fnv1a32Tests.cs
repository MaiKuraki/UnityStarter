using System;
using CycloneGames.Hash.Core;
using NUnit.Framework;

namespace CycloneGames.Hash.Tests.Editor
{
    public sealed class Fnv1a32Tests
    {
        [Test]
        public void Fnv1a32_Empty_Returns_OffsetBasis()
        {
            Assert.That(Fnv1a32.Compute(ReadOnlySpan<byte>.Empty), Is.EqualTo(Fnv1a32.OffsetBasis));
            Assert.That(Fnv1a32.OffsetBasis, Is.EqualTo(0x811C9DC5u));
        }

        [Test]
        public void Fnv1a32_MatchesKnownVector()
        {
            // Canonical FNV-1a 32-bit test vector for the single byte 'a'.
            Assert.That(Fnv1a32.Compute(new byte[] { (byte)'a' }), Is.EqualTo(0xE40C292Cu));
        }

        [Test]
        public void Fnv1a32_CombineUInt32_Matches_LittleEndian_ByteHash()
        {
            uint seed = Fnv1a32.OffsetBasis;
            uint value = 0x01020304u;

            uint combined = Fnv1a32.CombineUInt32LittleEndian(seed, value);
            uint byteWise = Fnv1a32.Compute(new byte[] { 0x04, 0x03, 0x02, 0x01 }, seed);

            Assert.That(combined, Is.EqualTo(byteWise));
        }

        [Test]
        public void Fnv1a32_Utf16Ordinal_Matches_Manual_Loop()
        {
            uint expected;
            unchecked
            {
                expected = Fnv1a32.OffsetBasis;
                expected ^= 'A';
                expected *= Fnv1a32.Prime;
                expected ^= '.';
                expected *= Fnv1a32.Prime;
                expected ^= 'B';
                expected *= Fnv1a32.Prime;
            }

            Assert.That(Fnv1a32.ComputeUtf16Ordinal("A.B"), Is.EqualTo(expected));
        }

        [Test]
        public void Fnv1a32_Ascii_Utf16Ordinal_Equals_ByteCompute()
        {
            // For ASCII (chars <= 0xFF) the UTF-16-ordinal fold matches the byte-wise hash.
            uint ordinal = Fnv1a32.ComputeUtf16Ordinal("abc");
            uint bytes = Fnv1a32.Compute(new byte[] { (byte)'a', (byte)'b', (byte)'c' });

            Assert.That(ordinal, Is.EqualTo(bytes));
        }

        [Test]
        public void Fnv1a32_SeededChunksMatchOneShot()
        {
            byte[] data = new byte[257];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i * 31);
            }

            const uint seed = 0x12345678U;
            uint chunked = Fnv1a32.Compute(data.AsSpan(0, 17), seed);
            chunked = Fnv1a32.Compute(data.AsSpan(17, 91), chunked);
            chunked = Fnv1a32.Compute(data.AsSpan(108), chunked);

            Assert.That(chunked, Is.EqualTo(Fnv1a32.Compute(data, seed)));
        }

        [Test]
        public void Fnv1a32_Utf16Ordinal_IsNotUtf16LittleEndianByteHash()
        {
            const string text = "\u4F60";
            uint ordinal = Fnv1a32.ComputeUtf16Ordinal(text);
            uint utf16LittleEndian = Fnv1a32.Compute(new byte[] { 0x60, 0x4F });

            Assert.That(ordinal, Is.Not.EqualTo(utf16LittleEndian));
        }
    }
}
