using System;

using CycloneGames.Hash.Core;

using NUnit.Framework;

namespace CycloneGames.Hash.Tests.Editor
{
    public sealed class HashCoreTests
    {
        private static readonly byte[] Sequential257 = CreateSequentialData(257);

        [Test]
        public void Fnv1a64_MatchesKnownVectors()
        {
            Assert.That(Fnv1a64.Compute(ReadOnlySpan<byte>.Empty), Is.EqualTo(0xCBF29CE484222325UL));
            Assert.That(Fnv1a64.Compute(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' }), Is.EqualTo(0xA430D84680AABD0BUL));
        }

        [Test]
        public void Fnv1a64_SeededChunksMatchOneShot()
        {
            const ulong seed = 0x0123456789ABCDEFUL;
            ulong chunked = Fnv1a64.Compute(Sequential257.AsSpan(0, 31), seed);
            chunked = Fnv1a64.Compute(Sequential257.AsSpan(31, 128), chunked);
            chunked = Fnv1a64.Compute(Sequential257.AsSpan(159), chunked);

            Assert.That(chunked, Is.EqualTo(Fnv1a64.Compute(Sequential257, seed)));
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
                expected ^= '\u4F60';
                expected *= Fnv1a64.Prime;
            }

            Assert.That(Fnv1a64.ComputeUtf16Ordinal("A\u4F60"), Is.EqualTo(expected));
        }

        [Test]
        public void Fnv1a64_CombineUInt64MatchesLittleEndianByteHash()
        {
            const ulong value = 0x0102030405060708UL;
            ulong expected = Fnv1a64.Compute(
                new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 },
                Fnv1a64.OffsetBasis);

            Assert.That(
                Fnv1a64.CombineUInt64LittleEndian(Fnv1a64.OffsetBasis, value),
                Is.EqualTo(expected));
        }

        [Test]
        public void StableHash_RejectsNullStrings()
        {
            Assert.Throws<ArgumentNullException>(() => StableHash32.ComputeUtf16Ordinal((string)null));
            Assert.Throws<ArgumentNullException>(() => StableHash64.ComputeUtf16Ordinal((string)null));
        }

        [Test]
        public void StableHash_MapsZeroOnlyAtFinalization()
        {
            Assert.That(StableHash32.EnsureNonZero(0U), Is.EqualTo(StableHash32.NonZeroFallback));
            Assert.That(StableHash32.EnsureNonZero(7U), Is.EqualTo(7U));
            Assert.That(StableHash64.EnsureNonZero(0UL), Is.EqualTo(StableHash64.NonZeroFallback));
            Assert.That(StableHash64.EnsureNonZero(7UL), Is.EqualTo(7UL));
        }

        [Test]
        public void HashByteOrder_RoundTripsUInt32AndUInt64()
        {
            Span<byte> buffer = stackalloc byte[8];

            HashByteOrder.WriteUInt32LittleEndian(buffer, 0x01020304U);
            Assert.That(buffer.Slice(0, 4).ToArray(), Is.EqualTo(new byte[] { 0x04, 0x03, 0x02, 0x01 }));
            Assert.That(HashByteOrder.ReadUInt32LittleEndian(buffer), Is.EqualTo(0x01020304U));

            HashByteOrder.WriteUInt32BigEndian(buffer, 0x01020304U);
            Assert.That(buffer.Slice(0, 4).ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04 }));
            Assert.That(HashByteOrder.ReadUInt32BigEndian(buffer), Is.EqualTo(0x01020304U));

            HashByteOrder.WriteUInt64LittleEndian(buffer, 0x0102030405060708UL);
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }));
            Assert.That(HashByteOrder.ReadUInt64LittleEndian(buffer), Is.EqualTo(0x0102030405060708UL));

            HashByteOrder.WriteUInt64BigEndian(buffer, 0x0102030405060708UL);
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }));
            Assert.That(HashByteOrder.ReadUInt64BigEndian(buffer), Is.EqualTo(0x0102030405060708UL));
        }

        [Test]
        public void HashByteOrder_RejectsShortBuffers()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HashByteOrder.ReadUInt32LittleEndian(new byte[3]));
            Assert.Throws<ArgumentOutOfRangeException>(() => HashByteOrder.WriteUInt32BigEndian(new byte[3], 1U));
            Assert.Throws<ArgumentOutOfRangeException>(() => HashByteOrder.ReadUInt64BigEndian(new byte[7]));
            Assert.Throws<ArgumentOutOfRangeException>(() => HashByteOrder.WriteUInt64LittleEndian(new byte[7], 1UL));
        }

        [TestCase(0, 0xEF46DB3751D8E999UL)]
        [TestCase(1, 0xE934A84ADB052768UL)]
        [TestCase(31, 0xC346D2B59B4D8EE1UL)]
        [TestCase(32, 0xCBF59C5116FF32B4UL)]
        [TestCase(33, 0x0C535D1ACAFB8EADUL)]
        [TestCase(64, 0xF7C67301DB6713F0UL)]
        public void XxHash64_MatchesIndependentReferenceVectors(int length, ulong expected)
        {
            byte[] data = CreateSequentialData(length);

            Assert.That(XxHash64.Compute(data), Is.EqualTo(expected));
        }

        [Test]
        public void XxHash64_MatchesTextAndSeededReferenceVectors()
        {
            Assert.That(XxHash64.Compute(new byte[] { (byte)'a' }), Is.EqualTo(0xD24EC4F1A98C6E5BUL));
            Assert.That(XxHash64.Compute(new byte[] { (byte)'a', (byte)'b', (byte)'c' }), Is.EqualTo(0x44BC2CF5AD770999UL));
            Assert.That(XxHash64.Compute(Sequential257, 42UL), Is.EqualTo(0xE3DC51B1D7346E1BUL));
        }

        [Test]
        public void XxHash64_DefaultStateMatchesCreatedStateAcrossFirstStripe()
        {
            XxHash64 defaultState = default;
            defaultState.Append(Sequential257);

            XxHash64 createdState = XxHash64.Create();
            createdState.Append(Sequential257);

            Assert.That(defaultState.GetDigest(), Is.EqualTo(createdState.GetDigest()));
        }

        [Test]
        public void XxHash64_AllChunkBoundariesMatchOneShot()
        {
            ulong expected = XxHash64.Compute(Sequential257, 42UL);

            for (int chunkSize = 1; chunkSize <= 65; chunkSize++)
            {
                XxHash64 state = XxHash64.Create(42UL);
                int offset = 0;
                while (offset < Sequential257.Length)
                {
                    int count = Math.Min(chunkSize, Sequential257.Length - offset);
                    state.Append(Sequential257, offset, count);
                    offset += count;
                }

                Assert.That(state.GetDigest(), Is.EqualTo(expected), $"Chunk size {chunkSize} diverged.");
            }
        }

        [Test]
        public void XxHash64_DigestIsNonDestructiveAndStateCanContinue()
        {
            XxHash64 state = XxHash64.Create();
            state.Append(Sequential257, 0, 31);
            ulong first = state.GetDigest();

            Assert.That(state.GetDigest(), Is.EqualTo(first));

            state.Append(Sequential257, 31, Sequential257.Length - 31);

            Assert.That(state.GetDigest(), Is.EqualTo(XxHash64.Compute(Sequential257)));
        }

        [Test]
        public void XxHash64_ResetReusesStateWithNewSeed()
        {
            XxHash64 state = XxHash64.Create();
            state.Append(Sequential257);

            state.Reset(42UL);
            state.Append(Sequential257);

            Assert.That(state.GetDigest(), Is.EqualTo(XxHash64.Compute(Sequential257, 42UL)));
        }

        [Test]
        public void XxHash64_WritesCanonicalAndLittleEndianRepresentations()
        {
            XxHash64 state = XxHash64.Create();
            state.Append(ReadOnlySpan<byte>.Empty);
            Span<byte> canonical = stackalloc byte[XxHash64.HashSizeInBytes];
            Span<byte> littleEndian = stackalloc byte[XxHash64.HashSizeInBytes];

            Assert.That(state.TryWriteHash(canonical), Is.True);
            Assert.That(state.TryWriteHashBigEndian(canonical), Is.True);
            Assert.That(state.TryWriteHashLittleEndian(littleEndian), Is.True);
            Assert.That(canonical.ToArray(), Is.EqualTo(new byte[] { 0xEF, 0x46, 0xDB, 0x37, 0x51, 0xD8, 0xE9, 0x99 }));
            Assert.That(littleEndian.ToArray(), Is.EqualTo(new byte[] { 0x99, 0xE9, 0xD8, 0x51, 0x37, 0xDB, 0x46, 0xEF }));
        }

        [Test]
        public void XxHash64_WriteMethodsRejectShortDestinationsWithoutMutation()
        {
            XxHash64 state = XxHash64.Create();
            state.Append(Sequential257);
            ulong before = state.GetDigest();
            Span<byte> destination = stackalloc byte[XxHash64.HashSizeInBytes - 1];

            Assert.That(state.TryWriteHash(destination), Is.False);
            Assert.That(state.TryWriteHashBigEndian(destination), Is.False);
            Assert.That(state.TryWriteHashLittleEndian(destination), Is.False);
            Assert.That(state.GetDigest(), Is.EqualTo(before));
        }

        private static byte[] CreateSequentialData(int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)i;
            }

            return data;
        }
    }
}
