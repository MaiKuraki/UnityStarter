using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   public enum GameplayTagNetPacketKind : byte
   {
      Delta = 0xFD,
      Full = 0xFE
   }

   /// <summary>
   /// Framework-agnostic binary serialization for gameplay tag replication.
   /// Current packets use stable 64-bit tag ids plus a tag manifest hash so
   /// clients and servers fail fast when their tag tables differ.
   /// </summary>
   public static class GameplayTagNetSerializer
   {
      /// <summary>
      /// Latest gameplay tag serializer wire-format version emitted by this module.
      /// This is scoped to GameplayTagNetSerializer payloads, not the whole game network protocol.
      /// </summary>
      public const byte CurrentProtocolVersion = 1;

      /// <summary>
      /// Oldest gameplay tag serializer wire-format version accepted by this module.
      /// Keep equal to <see cref="CurrentProtocolVersion"/> until a compatible newer format exists.
      /// </summary>
      public const byte MinimumSupportedProtocolVersion = 1;

      private const int ProtocolVersionSize = sizeof(byte);
      private const int PacketKindSize = sizeof(byte);
      private const int CountSize = sizeof(int);
      private const int ManifestHashSize = sizeof(ulong);
      private const int StableIdSize = sizeof(ulong);
      private const int MaskWordCount = 4;
      private const int HeaderSize = ProtocolVersionSize + PacketKindSize + ManifestHashSize + CountSize;
      private const int DeltaBaseSize = HeaderSize + CountSize;
      private const int MaskSerializedSize = MaskWordCount * sizeof(ulong);

      public static byte[] SerializeFull<T>(in T container) where T : IGameplayTagContainer
      {
         int size = GetFullSerializedSize(container);
         byte[] buffer = new byte[size];
         SerializeFull(container, buffer, 0);
         return buffer;
      }

      public static byte[] SerializeFull(ReadOnlyGameplayTagContainer snapshot)
      {
         int count = snapshot?.ExplicitTagCount ?? 0;
         byte[] buffer = new byte[GetFullSerializedSizeFromCount(count)];
         int offset = 0;
         WriteHeader(buffer, ref offset, GameplayTagNetPacketKind.Full, count);

         if (snapshot == null)
            return buffer;

         ReadOnlySpan<int> indices = snapshot.GetExplicitIndices();
         for (int i = 0; i < indices.Length; i++)
            WriteUInt64(buffer, ref offset, GameplayTagManager.GetStableIdFromRuntimeIndex(indices[i]));

         return buffer;
      }

      public static int SerializeFull<T>(in T container, byte[] buffer, int startOffset) where T : IGameplayTagContainer
      {
         int count = container is null ? 0 : container.ExplicitTagCount;
         int requiredSize = GetFullSerializedSizeFromCount(count);
         EnsureWriteCapacity(buffer, startOffset, requiredSize);

         int offset = startOffset;
         WriteHeader(buffer, ref offset, GameplayTagNetPacketKind.Full, count);

         if (container is null)
            return offset - startOffset;

         foreach (GameplayTag tag in container.GetExplicitTags())
         {
            if (!tag.IsValid)
               throw new ArgumentException("Cannot serialize an invalid gameplay tag.", nameof(container));

            WriteUInt64(buffer, ref offset, tag.StableId);
         }

         return offset - startOffset;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int GetFullSerializedSize<T>(in T container) where T : IGameplayTagContainer
      {
         int count = container is null ? 0 : container.ExplicitTagCount;
         return GetFullSerializedSizeFromCount(count);
      }

      public static void DeserializeFull(GameplayTagContainer container, byte[] data, int offset = 0)
      {
         if (container == null)
            throw new ArgumentNullException(nameof(container));

         int readOffset = offset;
         int count = ReadHeader(data, ref readOffset, GameplayTagNetPacketKind.Full);
         EnsureReadCapacity(data, readOffset, GetStableIdPayloadSize(count));

         container.Clear();
         for (int i = 0; i < count; i++)
         {
            ulong stableId = ReadUInt64(data, ref readOffset);
            if (GameplayTagManager.TryGetTagFromStableId(stableId, out GameplayTag tag))
            {
               container.AddTag(tag);
               continue;
            }

            throw new InvalidOperationException($"Gameplay tag stable ID 0x{stableId:X16} does not exist in the local manifest.");
         }
      }

      public static byte[] SerializeDelta(GameplayTagContainer current, GameplayTagContainer previous)
      {
         if (current == null)
            throw new ArgumentNullException(nameof(current));

         if (previous == null)
            throw new ArgumentNullException(nameof(previous));

         using (Pools.ListPool<GameplayTag>.Get(out var added))
         using (Pools.ListPool<GameplayTag>.Get(out var removed))
         {
            current.GetDiffExplicitTags(previous, added, removed);
            return SerializeDelta(added, removed);
         }
      }

      public static int SerializeDelta(GameplayTagContainer current, GameplayTagContainer previous, byte[] buffer, int startOffset)
      {
         if (current == null)
            throw new ArgumentNullException(nameof(current));

         if (previous == null)
            throw new ArgumentNullException(nameof(previous));

         using (Pools.ListPool<GameplayTag>.Get(out var added))
         using (Pools.ListPool<GameplayTag>.Get(out var removed))
         {
            current.GetDiffExplicitTags(previous, added, removed);
            return SerializeDelta(added, removed, buffer, startOffset);
         }
      }

      public static byte[] SerializeDelta(List<GameplayTag> added, List<GameplayTag> removed)
      {
         int addCount = added?.Count ?? 0;
         int removeCount = removed?.Count ?? 0;
         byte[] buffer = new byte[GetDeltaSerializedSize(addCount, removeCount)];
         SerializeDelta(added, removed, buffer, 0);
         return buffer;
      }

      public static int SerializeDelta(List<GameplayTag> added, List<GameplayTag> removed, byte[] buffer, int startOffset)
      {
         int addCount = added?.Count ?? 0;
         int removeCount = removed?.Count ?? 0;
         int requiredSize = GetDeltaSerializedSize(addCount, removeCount);
         EnsureWriteCapacity(buffer, startOffset, requiredSize);

         int offset = startOffset;
         WriteHeader(buffer, ref offset, GameplayTagNetPacketKind.Delta, addCount);
         for (int i = 0; i < addCount; i++)
         {
            if (!added[i].IsValid)
               throw new ArgumentException("Cannot serialize an invalid added gameplay tag.", nameof(added));

            WriteUInt64(buffer, ref offset, added[i].StableId);
         }

         WriteInt32(buffer, ref offset, removeCount);
         for (int i = 0; i < removeCount; i++)
         {
            if (!removed[i].IsValid)
               throw new ArgumentException("Cannot serialize an invalid removed gameplay tag.", nameof(removed));

            WriteUInt64(buffer, ref offset, removed[i].StableId);
         }

         return offset - startOffset;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int GetDeltaSerializedSize(int addCount, int removeCount)
      {
         if (addCount < 0) throw new ArgumentOutOfRangeException(nameof(addCount));
         if (removeCount < 0) throw new ArgumentOutOfRangeException(nameof(removeCount));
         if (addCount > int.MaxValue - removeCount)
            throw new ArgumentOutOfRangeException(nameof(addCount), "Gameplay tag delta packet is too large.");

         return GetPacketSize(DeltaBaseSize, addCount + removeCount);
      }

      public static void ApplyDelta(GameplayTagContainer container, byte[] data, int offset = 0)
      {
         if (container == null)
            throw new ArgumentNullException(nameof(container));

         int readOffset = offset;
         int addCount = ReadHeader(data, ref readOffset, GameplayTagNetPacketKind.Delta);
         EnsureReadCapacity(data, readOffset, GetPacketSize(4, addCount));

         for (int i = 0; i < addCount; i++)
         {
            ulong stableId = ReadUInt64(data, ref readOffset);
            if (GameplayTagManager.TryGetTagFromStableId(stableId, out GameplayTag tag))
            {
               container.AddTag(tag);
               continue;
            }

            throw new InvalidOperationException($"Gameplay tag stable ID 0x{stableId:X16} does not exist in the local manifest.");
         }

         int removeCount = ReadInt32(data, ref readOffset);
         if (removeCount < 0)
            throw new ArgumentException("Invalid tag delta data: negative remove count.");

         EnsureReadCapacity(data, readOffset, GetStableIdPayloadSize(removeCount));
         for (int i = 0; i < removeCount; i++)
         {
            ulong stableId = ReadUInt64(data, ref readOffset);
            if (GameplayTagManager.TryGetTagFromStableId(stableId, out GameplayTag tag))
            {
               container.RemoveTag(tag);
               continue;
            }

            throw new InvalidOperationException($"Gameplay tag stable ID 0x{stableId:X16} does not exist in the local manifest.");
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool IsDeltaPacket(byte[] data, int offset = 0)
      {
         return TryGetPacketKind(data, offset, out GameplayTagNetPacketKind kind) &&
                kind == GameplayTagNetPacketKind.Delta;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool IsFullPacket(byte[] data, int offset = 0)
      {
         return TryGetPacketKind(data, offset, out GameplayTagNetPacketKind kind) &&
                kind == GameplayTagNetPacketKind.Full;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool TryGetPacketKind(byte[] data, int offset, out GameplayTagNetPacketKind kind)
      {
         kind = default;

         if (data == null ||
             offset < 0 ||
             data.Length <= offset + 1 ||
             !IsSupportedProtocolVersion(data[offset]))
         {
            return false;
         }

         byte marker = data[offset + 1];
         if (marker == (byte)GameplayTagNetPacketKind.Full)
         {
            kind = GameplayTagNetPacketKind.Full;
            return true;
         }

         if (marker == (byte)GameplayTagNetPacketKind.Delta)
         {
            kind = GameplayTagNetPacketKind.Delta;
            return true;
         }

         return false;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool IsSupportedProtocolVersion(byte protocolVersion)
      {
         return protocolVersion >= MinimumSupportedProtocolVersion &&
                protocolVersion <= CurrentProtocolVersion;
      }

      public static void Deserialize(GameplayTagContainer container, byte[] data, int offset = 0)
      {
         if (IsDeltaPacket(data, offset))
            ApplyDelta(container, data, offset);
         else
            DeserializeFull(container, data, offset);
      }

      public static void SerializeMask(in GameplayTagMask mask, byte[] buffer, int offset)
      {
         EnsureWriteCapacity(buffer, offset, MaskSerializedSize);
         int writeOffset = offset;
         WriteUInt64(buffer, ref writeOffset, mask.GetWord(0));
         WriteUInt64(buffer, ref writeOffset, mask.GetWord(1));
         WriteUInt64(buffer, ref writeOffset, mask.GetWord(2));
         WriteUInt64(buffer, ref writeOffset, mask.GetWord(3));
      }

      public static GameplayTagMask DeserializeMask(byte[] buffer, int offset)
      {
         EnsureReadCapacity(buffer, offset, MaskSerializedSize);
         int readOffset = offset;
         GameplayTagMask mask = default;
         ulong w0 = ReadUInt64(buffer, ref readOffset);
         ulong w1 = ReadUInt64(buffer, ref readOffset);
         ulong w2 = ReadUInt64(buffer, ref readOffset);
         ulong w3 = ReadUInt64(buffer, ref readOffset);
         SetWordBits(ref mask, 0, w0);
         SetWordBits(ref mask, 1, w1);
         SetWordBits(ref mask, 2, w2);
         SetWordBits(ref mask, 3, w3);
         return mask;
      }

      private static void SetWordBits(ref GameplayTagMask mask, int wordIndex, ulong word)
      {
         int baseIndex = wordIndex << 6;
         while (word != 0)
         {
            int bit = BitScanForward(word);
            mask.SetBit(baseIndex + bit);
            word &= word - 1;
         }
      }

      private static void WriteHeader(byte[] buffer, ref int offset, GameplayTagNetPacketKind kind, int count)
      {
         if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

         buffer[offset++] = CurrentProtocolVersion;
         buffer[offset++] = (byte)kind;
         WriteUInt64(buffer, ref offset, GameplayTagManager.CurrentManifestHash);
         WriteInt32(buffer, ref offset, count);
      }

      private static int ReadHeader(byte[] data, ref int offset, GameplayTagNetPacketKind expectedKind)
      {
         EnsureReadCapacity(data, offset, HeaderSize);

         byte version = data[offset++];
         if (!IsSupportedProtocolVersion(version))
         {
            throw new NotSupportedException(
               $"Unsupported tag net serialization version: {version}. Supported range: {MinimumSupportedProtocolVersion}-{CurrentProtocolVersion}.");
         }

         byte marker = data[offset++];
         if (marker != (byte)expectedKind)
            throw new ArgumentException($"Expected packet kind 0x{(byte)expectedKind:X2}, got 0x{marker:X2}.");

         ulong manifestHash = ReadUInt64(data, ref offset);
         if (manifestHash != GameplayTagManager.CurrentManifestHash)
         {
            throw new InvalidOperationException(
               $"Gameplay tag manifest mismatch. Local=0x{GameplayTagManager.CurrentManifestHash:X16}, Remote=0x{manifestHash:X16}.");
         }

         int count = ReadInt32(data, ref offset);
         if (count < 0)
            throw new ArgumentException("Invalid tag network data: negative tag count.");

         return count;
      }

      private static void EnsureWriteCapacity(byte[] buffer, int offset, int requiredSize)
      {
         if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
         if (offset < 0 || requiredSize < 0 || buffer.Length - offset < requiredSize)
            throw new ArgumentException("Output buffer is too small for gameplay tag serialization.");
      }

      private static int GetFullSerializedSizeFromCount(int count)
      {
         return GetPacketSize(HeaderSize, count);
      }

      private static int GetStableIdPayloadSize(int count)
      {
         if (count < 0 || count > int.MaxValue / StableIdSize)
            throw new ArgumentOutOfRangeException(nameof(count), "Gameplay tag packet count is out of range.");

         return count * StableIdSize;
      }

      private static int GetPacketSize(int fixedSize, int stableIdCount)
      {
         int payloadSize = GetStableIdPayloadSize(stableIdCount);
         if (fixedSize > int.MaxValue - payloadSize)
            throw new ArgumentOutOfRangeException(nameof(stableIdCount), "Gameplay tag packet is too large.");

         return fixedSize + payloadSize;
      }

      private static void EnsureReadCapacity(byte[] data, int offset, int requiredSize)
      {
         if (data == null)
            throw new ArgumentNullException(nameof(data));
         if (offset < 0 || requiredSize < 0 || data.Length - offset < requiredSize)
            throw new ArgumentException("Invalid gameplay tag network data: truncated packet.");
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static void WriteInt32(byte[] buffer, ref int offset, int value)
      {
         unchecked
         {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static int ReadInt32(byte[] data, ref int offset)
      {
         int value = data[offset] |
                     (data[offset + 1] << 8) |
                     (data[offset + 2] << 16) |
                     (data[offset + 3] << 24);
         offset += 4;
         return value;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static void WriteUInt64(byte[] buffer, ref int offset, ulong value)
      {
         unchecked
         {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
            buffer[offset++] = (byte)(value >> 32);
            buffer[offset++] = (byte)(value >> 40);
            buffer[offset++] = (byte)(value >> 48);
            buffer[offset++] = (byte)(value >> 56);
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static ulong ReadUInt64(byte[] data, ref int offset)
      {
         ulong value = data[offset] |
                       ((ulong)data[offset + 1] << 8) |
                       ((ulong)data[offset + 2] << 16) |
                       ((ulong)data[offset + 3] << 24) |
                       ((ulong)data[offset + 4] << 32) |
                       ((ulong)data[offset + 5] << 40) |
                       ((ulong)data[offset + 6] << 48) |
                       ((ulong)data[offset + 7] << 56);
         offset += 8;
         return value;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static int BitScanForward(ulong value)
      {
         if (value == 0) return 64;
         int n = 0;
         if ((value & 0xFFFFFFFFUL) == 0) { n += 32; value >>= 32; }
         if ((value & 0xFFFFUL) == 0) { n += 16; value >>= 16; }
         if ((value & 0xFFUL) == 0) { n += 8; value >>= 8; }
         if ((value & 0xFUL) == 0) { n += 4; value >>= 4; }
         if ((value & 0x3UL) == 0) { n += 2; value >>= 2; }
         if ((value & 0x1UL) == 0) { n += 1; }
         return n;
      }
   }
}
