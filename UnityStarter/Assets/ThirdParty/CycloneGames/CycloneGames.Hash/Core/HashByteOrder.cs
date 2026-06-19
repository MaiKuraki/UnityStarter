using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace CycloneGames.Hash.Core
{
    /// <summary>
    /// Explicit endian helpers for serialized hash values.
    /// </summary>
    public static class HashByteOrder
    {
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
