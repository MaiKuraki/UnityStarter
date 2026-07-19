using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Explicit endian helpers for serialized 32-bit and 64-bit hash values.
    /// FNV interoperable byte vectors use little-endian order. The canonical xxHash representation
    /// uses big-endian order.
    /// </summary>
    public static class HashByteOrder
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(source);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(source);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(source);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt64LittleEndian(Span<byte> destination, ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt64BigEndian(source);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt64BigEndian(Span<byte> destination, ulong value)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
        }
    }
}
