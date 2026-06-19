using System;
using CycloneGames.Hash.Core;
using NUnit.Framework;

namespace CycloneGames.Hash.Tests.Editor
{
    public sealed class HashCoreTests
    {
        [Test]
        public void Fnv1a64_MatchesKnownVectors()
        {
            Assert.That(Fnv1a64.Compute(ReadOnlySpan<byte>.Empty), Is.EqualTo(0xCBF29CE484222325UL));
            Assert.That(Fnv1a64.Compute(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' }), Is.EqualTo(0xA430D84680AABD0BUL));
        }

        [Test]
        public void Fnv1a64_Utf16OrdinalMatchesLegacyStableIdSemantics()
        {
            ulong expected;
            unchecked
            {
                expected = Fnv1a64.OffsetBasis;
                expected ^= 'A';
                expected *= Fnv1a64.Prime;
                expected ^= '.';
                expected *= Fnv1a64.Prime;
                expected ^= 'B';
                expected *= Fnv1a64.Prime;
            }

            Assert.That(Fnv1a64.ComputeUtf16Ordinal("A.B"), Is.EqualTo(expected));
        }

        [Test]
        public void StableHash64_CombinesUInt64InLittleEndianOrder()
        {
            ulong seed = Fnv1a64.OffsetBasis;
            ulong value = 0x0102030405060708UL;
            ulong expected = Fnv1a64.Compute(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }, seed);

            Assert.That(StableHash64.CombineUInt64LittleEndian(seed, value), Is.EqualTo(expected));
        }

        [Test]
        public void XxHash64_MatchesKnownEmptyVector()
        {
            Assert.That(XxHash64.Compute(ReadOnlySpan<byte>.Empty), Is.EqualTo(0xEF46DB3751D8E999UL));
        }

        [Test]
        public void XxHash64_StreamingMatchesOneShot()
        {
            byte[] data = new byte[257];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i * 31);
            }

            XxHash64 streaming = XxHash64.Create(seed: 42UL);
            streaming.Append(data, 0, 17);
            streaming.Append(data, 17, 91);
            streaming.Append(data, 108, data.Length - 108);

            Assert.That(streaming.GetDigest(), Is.EqualTo(XxHash64.Compute(data, seed: 42UL)));
        }

        [Test]
        public void HashByteOrder_WritesExplicitEndianValues()
        {
            Span<byte> littleEndian = stackalloc byte[8];
            Span<byte> bigEndian = stackalloc byte[8];

            HashByteOrder.WriteUInt64LittleEndian(littleEndian, 0x0102030405060708UL);
            HashByteOrder.WriteUInt64BigEndian(bigEndian, 0x0102030405060708UL);

            Assert.That(littleEndian.ToArray(), Is.EqualTo(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }));
            Assert.That(bigEndian.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }));
        }
    }
}
