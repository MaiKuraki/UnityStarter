using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Pure C# binary serialization for GameplayTag data over network.
   /// Engine-agnostic — works with any networking framework (Netcode, Mirror, FishNet, custom).
   ///
   /// Wire format (compact):
   ///   [ushort tagCount] [ushort runtimeIndex] × tagCount
   ///
   /// For delta mode:
   ///   [byte deltaFlag=0xFD] [ushort addCount] [ushort idx]... [ushort removeCount] [ushort idx]...
   ///
   /// All indices are runtime indices from the current TagDataSnapshot.
   /// Both sides must share the same tag registration (same build/same hot-update version).
   /// </summary>
   public static class GameplayTagNetSerializer
   {
      private const byte DeltaMarker = 0xFD;
      private const byte FullMarker = 0xFE;
      private const byte VersionByte = 1;

      // --- Full Snapshot Serialization ---

      /// <summary>
      /// Serialize all explicit tags in a container to a byte array.
      /// Compact: 3 + 2*N bytes (version + marker + count + indices).
      /// </summary>
      public static byte[] SerializeFull<T>(in T container) where T : IGameplayTagContainer
      {
         int count = container.ExplicitTagCount;
         byte[] buffer = new byte[3 + count * 2]; // version(1) + marker(1) + count(2 as ushort) - 1 byte overlap = 3 + 2*N
         int offset = 0;

         buffer[offset++] = VersionByte;
         buffer[offset++] = FullMarker;
         WriteUInt16(buffer, ref offset, (ushort)count);

         foreach (GameplayTag tag in container.GetExplicitTags())
         {
            WriteUInt16(buffer, ref offset, (ushort)tag.RuntimeIndex);
         }

         return buffer;
      }

      /// <summary>
      /// Serialize all explicit tags to a pre-allocated buffer. Returns bytes written.
      /// </summary>
      public static int SerializeFull<T>(in T container, byte[] buffer, int startOffset) where T : IGameplayTagContainer
      {
         int offset = startOffset;

         buffer[offset++] = VersionByte;
         buffer[offset++] = FullMarker;
         WriteUInt16(buffer, ref offset, (ushort)container.ExplicitTagCount);

         foreach (GameplayTag tag in container.GetExplicitTags())
         {
            WriteUInt16(buffer, ref offset, (ushort)tag.RuntimeIndex);
         }

         return offset - startOffset;
      }

      /// <summary>
      /// Returns the byte size needed for a full serialization.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int GetFullSerializedSize<T>(in T container) where T : IGameplayTagContainer
      {
         return 4 + container.ExplicitTagCount * 2;
      }

      /// <summary>
      /// Deserialize a full snapshot into a container, replacing all existing tags.
      /// </summary>
      public static void DeserializeFull(GameplayTagContainer container, byte[] data, int offset = 0)
      {
         if (data == null || data.Length < offset + 4)
            throw new ArgumentException("Invalid tag network data: too short.");

         byte version = data[offset++];
         if (version != VersionByte)
            throw new NotSupportedException($"Unsupported tag net serialization version: {version}");

         byte marker = data[offset++];
         if (marker != FullMarker)
            throw new ArgumentException($"Expected full marker 0x{FullMarker:X2}, got 0x{marker:X2}.");

         ushort count = ReadUInt16(data, ref offset);

         container.Clear();
         for (int i = 0; i < count; i++)
         {
            ushort runtimeIndex = ReadUInt16(data, ref offset);
            GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(runtimeIndex);
            if (tag.IsValid)
               container.AddTag(tag);
         }
      }

      // --- Delta Serialization ---

      /// <summary>
      /// Serialize a delta (add/remove) between old and new container states.
      /// </summary>
      public static byte[] SerializeDelta(GameplayTagContainer current, GameplayTagContainer previous)
      {
         using (Pools.ListPool<GameplayTag>.Get(out var added))
         using (Pools.ListPool<GameplayTag>.Get(out var removed))
         {
            current.GetDiffExplicitTags(previous, added, removed);
            return SerializeDelta(added, removed);
         }
      }

      /// <summary>
      /// Serialize explicit add/remove lists as a delta packet.
      /// </summary>
      public static byte[] SerializeDelta(System.Collections.Generic.List<GameplayTag> added, System.Collections.Generic.List<GameplayTag> removed)
      {
         int addCount = added?.Count ?? 0;
         int removeCount = removed?.Count ?? 0;

         // If nothing changed, return minimal empty delta
         byte[] buffer = new byte[2 + 2 + addCount * 2 + 2 + removeCount * 2];
         int offset = 0;

         buffer[offset++] = VersionByte;
         buffer[offset++] = DeltaMarker;
         WriteUInt16(buffer, ref offset, (ushort)addCount);
         for (int i = 0; i < addCount; i++)
            WriteUInt16(buffer, ref offset, (ushort)added[i].RuntimeIndex);

         WriteUInt16(buffer, ref offset, (ushort)removeCount);
         for (int i = 0; i < removeCount; i++)
            WriteUInt16(buffer, ref offset, (ushort)removed[i].RuntimeIndex);

         return buffer;
      }

      /// <summary>
      /// Apply a delta packet to a container.
      /// </summary>
      public static void ApplyDelta(GameplayTagContainer container, byte[] data, int offset = 0)
      {
         if (data == null || data.Length < offset + 4)
            throw new ArgumentException("Invalid tag delta data: too short.");

         byte version = data[offset++];
         if (version != VersionByte)
            throw new NotSupportedException($"Unsupported tag net serialization version: {version}");

         byte marker = data[offset++];
         if (marker != DeltaMarker)
            throw new ArgumentException($"Expected delta marker 0x{DeltaMarker:X2}, got 0x{marker:X2}.");

         ushort addCount = ReadUInt16(data, ref offset);
         for (int i = 0; i < addCount; i++)
         {
            ushort runtimeIndex = ReadUInt16(data, ref offset);
            GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(runtimeIndex);
            if (tag.IsValid)
               container.AddTag(tag);
         }

         ushort removeCount = ReadUInt16(data, ref offset);
         for (int i = 0; i < removeCount; i++)
         {
            ushort runtimeIndex = ReadUInt16(data, ref offset);
            GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(runtimeIndex);
            if (tag.IsValid)
               container.RemoveTag(tag);
         }
      }

      /// <summary>
      /// Detect whether a packet is full or delta by peeking at the marker byte.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool IsDeltaPacket(byte[] data, int offset = 0)
      {
         return data != null && data.Length > offset + 1 && data[offset + 1] == DeltaMarker;
      }

      /// <summary>
      /// Auto-detect and apply either full or delta packet.
      /// </summary>
      public static void Deserialize(GameplayTagContainer container, byte[] data, int offset = 0)
      {
         if (IsDeltaPacket(data, offset))
            ApplyDelta(container, data, offset);
         else
            DeserializeFull(container, data, offset);
      }

      // --- Mask Serialization (for GameplayTagMask 256-bit) ---

      /// <summary>
      /// Serialize a 256-bit mask to exactly 32 bytes.
      /// </summary>
      public static unsafe void SerializeMask(in GameplayTagMask mask, byte[] buffer, int offset)
      {
         fixed (byte* ptr = &buffer[offset])
         fixed (GameplayTagMask* maskPtr = &mask)
         {
            ulong* src = (ulong*)maskPtr;
            ulong* dst = (ulong*)ptr;
            dst[0] = src[0];
            dst[1] = src[1];
            dst[2] = src[2];
            dst[3] = src[3];
         }
      }

      /// <summary>
      /// Deserialize 32 bytes into a GameplayTagMask.
      /// </summary>
      public static unsafe GameplayTagMask DeserializeMask(byte[] buffer, int offset)
      {
         GameplayTagMask mask = default;
         fixed (byte* ptr = &buffer[offset])
         {
            ulong* src = (ulong*)ptr;
            ulong* dst = (ulong*)&mask;
            dst[0] = src[0];
            dst[1] = src[1];
            dst[2] = src[2];
            dst[3] = src[3];
         }
         return mask;
      }

      // --- Internal helpers ---

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
      {
         buffer[offset++] = (byte)(value & 0xFF);
         buffer[offset++] = (byte)(value >> 8);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static ushort ReadUInt16(byte[] data, ref int offset)
      {
         ushort value = (ushort)(data[offset] | (data[offset + 1] << 8));
         offset += 2;
         return value;
      }
   }
}
