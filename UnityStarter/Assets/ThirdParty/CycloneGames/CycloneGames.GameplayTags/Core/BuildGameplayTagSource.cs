using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.Hash.Core;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// The baked tag manifest format a Player build ships.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Layout, little-endian throughout:
   /// <c>[uint signature]["CGTG"]</c>, <c>[int tagCount]</c>,
   /// then <c>tagCount</c> entries of <c>[string name][string description][int flags]</c>,
   /// then <c>[ulong contentHash]</c> over every byte that precedes it.
   /// </para>
   /// <para>
   /// Strings are 7-bit-variable-length byte counts followed by UTF-8. Counts are canonically encoded -
   /// a non-final byte may not be zero - so the same manifest always hashes to the same value and a
   /// hand-edited manifest cannot smuggle a different length past the content hash.
   /// </para>
   /// <para>
   /// The content hash covers the manifest but not itself, which is what makes it a real integrity check:
   /// a truncated or edited payload is rejected before a single tag is registered.
   /// </para>
   /// </remarks>
   public static class BuildTagBinaryFormat
   {
      // BinaryWriter writes little-endian values, producing the ASCII bytes "CGTG".
      public const uint FileSignature = 0x47544743U;
      public const int ContentHashSize = sizeof(ulong);
      public const int MaxDataSizeBytes = 32 * 1024 * 1024;
      public const int MaxDescriptionLength = 4096;

      internal const int MaxTagNameUtf8Bytes = GameplayTagUtility.MaxTagNameLength * 4;
      internal const int MaxDescriptionUtf8Bytes = MaxDescriptionLength * 4;

      private static readonly UTF8Encoding s_StrictUtf8 = new(false, true);

      public static ulong ComputeContentHash64(byte[] data, int offset, int length)
      {
         if (data == null)
            throw new ArgumentNullException(nameof(data));
         if (offset < 0 || length < 0 || data.Length - offset < length)
            throw new ArgumentOutOfRangeException(nameof(length), "Invalid content hash range.");

         return GameplayTagUtility.ComputeStableHash64(new ReadOnlySpan<byte>(data, offset, length));
      }

      public static void ValidateEntry(string tagName, string description, GameplayTagFlags flags)
      {
         if (!GameplayTagUtility.IsNameValid(tagName, out string errorMessage))
            throw new InvalidDataException($"Invalid gameplay tag build name '{tagName}': {errorMessage}");
         if (description != null && description.Length > MaxDescriptionLength)
            throw new InvalidDataException($"Gameplay tag description cannot exceed {MaxDescriptionLength} UTF-16 code units.");
         if ((flags & ~GameplayTagFlags.HideInEditor) != 0)
            throw new InvalidDataException($"Gameplay tag build data contains unsupported flags value {(int)flags}.");
      }

      /// <summary>
      /// A forward-only reader over the manifest that allocates nothing but the strings it decodes.
      /// </summary>
      /// <remarks>
      /// The previous reader wrapped the payload in a <see cref="MemoryStream"/> and
      /// <see cref="BinaryReader"/> and decoded every string through a fresh <c>byte[]</c>, so loading a
      /// manifest of N tags allocated roughly N + 2 disposable objects on the critical path of a cold
      /// start. Reading straight out of the span removes all of it; the surviving allocations are the
      /// string objects the registry has to own anyway.
      /// </remarks>
      internal ref struct SpanReader
      {
         private readonly ReadOnlySpan<byte> m_Data;
         private int m_Position;

         public SpanReader(ReadOnlySpan<byte> data)
         {
            m_Data = data;
            m_Position = 0;
         }

         public int Position => m_Position;
         public int Remaining => m_Data.Length - m_Position;

         public uint ReadUInt32()
         {
            if (Remaining < 4)
               throw UnexpectedEnd("uint32");

            uint value = HashByteOrder.ReadUInt32LittleEndian(m_Data.Slice(m_Position, 4));
            m_Position += 4;
            return value;
         }

         public ulong ReadUInt64()
         {
            if (Remaining < 8)
               throw UnexpectedEnd("uint64");

            ulong value = HashByteOrder.ReadUInt64LittleEndian(m_Data.Slice(m_Position, 8));
            m_Position += 8;
            return value;
         }

         public int ReadInt32() => unchecked((int)ReadUInt32());

         /// <summary>
         /// Advances past one string without decoding it, returning its byte length. Used by the
         /// structure pass, which has to walk the whole manifest to locate the content hash before it can
         /// afford to trust any of it.
         /// </summary>
         public int SkipBoundedString(int maxUtf8Bytes, string fieldName)
         {
            int byteLength = ReadBoundedLength(maxUtf8Bytes, fieldName);
            if (Remaining < byteLength)
               throw UnexpectedEnd(fieldName);

            m_Position += byteLength;
            return byteLength;
         }

         public string ReadBoundedString(int maxUtf8Bytes, string fieldName)
         {
            int byteLength = ReadBoundedLength(maxUtf8Bytes, fieldName);
            if (Remaining < byteLength)
               throw UnexpectedEnd(fieldName);

            ReadOnlySpan<byte> bytes = m_Data.Slice(m_Position, byteLength);
            m_Position += byteLength;

            try
            {
               return s_StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
               throw new InvalidDataException($"Gameplay tag build {fieldName} is not valid UTF-8.", exception);
            }
         }

         private int ReadBoundedLength(int maxUtf8Bytes, string fieldName)
         {
            uint value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
               if (Remaining < 1)
                  throw UnexpectedEnd($"{fieldName} length");

               byte current = m_Data[m_Position++];
               if (shift == 28 && (current & 0xF0) != 0)
                  throw new InvalidDataException($"Gameplay tag build {fieldName} length is invalid.");

               value |= (uint)(current & 0x7F) << shift;
               if ((current & 0x80) == 0)
               {
                  if (shift > 0 && current == 0)
                     throw new InvalidDataException($"Gameplay tag build {fieldName} length is not canonically encoded.");

                  int length = checked((int)value);
                  if (length < 0 || length > maxUtf8Bytes)
                     throw new InvalidDataException($"Gameplay tag build {fieldName} exceeds its UTF-8 byte budget.");

                  return length;
               }
            }

            throw new InvalidDataException($"Gameplay tag build {fieldName} length is invalid.");
         }

         private static InvalidDataException UnexpectedEnd(string what)
            => new($"Gameplay tag build data ended while reading {what}.");
      }
   }

   /// <summary>
   /// The Player-side tag source: decodes the manifest baked into the build.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Validation runs in two passes. The first walks the structure to locate the content hash and bounds
   /// every string without decoding anything; only once the stored hash matches the computed one is a
   /// single tag registered. That ordering is what makes a corrupt manifest a clean failure instead of a
   /// half-populated registry.
   /// </para>
   /// <para>
   /// There is no reflection here and no attribute sweep, which is the whole point for IL2CPP: the managed
   /// stripper cannot strip what it cannot see, so nothing about this path depends on runtime type
   /// discovery. A HybridCLR hot-update assembly contributes tags through a generated
   /// <see cref="IGameplayTagCatalog"/> instead of through this file.
   /// </para>
   /// </remarks>
   internal sealed class BuildGameplayTagSource : IGameplayTagSource
   {
      private readonly byte[] m_OverrideData;

      public string Name => "Build";

      /// <summary>Creates a source that reads the manifest from the installed host platform.</summary>
      public BuildGameplayTagSource() { }

      /// <summary>
      /// Creates a source over a manifest the caller already holds. Tests and hosts that load the payload
      /// themselves use this instead of wiring a platform just to hand over one byte array.
      /// </summary>
      public BuildGameplayTagSource(byte[] data)
      {
         m_OverrideData = data ?? throw new ArgumentNullException(nameof(data));
      }

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         if (context == null)
            throw new ArgumentNullException(nameof(context));

         byte[] data;
         if (m_OverrideData != null)
         {
            data = m_OverrideData;
         }
         else if (!GameplayTagHost.Current.TryLoadBuildTagData(out data) ||
                  data == null || data.Length == 0)
         {
            throw new InvalidDataException(
               "Gameplay tag build data is missing or empty. A Player host must supply it through " +
               "IGameplayTagHostPlatform.TryLoadBuildTagData, or the registry must be given catalogs instead.");
         }

         if (data.Length > BuildTagBinaryFormat.MaxDataSizeBytes)
            throw new InvalidDataException($"Gameplay tag build data exceeds {BuildTagBinaryFormat.MaxDataSizeBytes} bytes.");

         ReadOnlySpan<byte> payload = data;

         // Pass 1: locate the content hash and validate the structure without decoding anything.
         int contentEnd;
         int tagCount;
         {
            BuildTagBinaryFormat.SpanReader reader = new(payload);
            if (reader.ReadUInt32() != BuildTagBinaryFormat.FileSignature)
               throw new InvalidDataException("Gameplay tag build data has an invalid file signature.");

            tagCount = reader.ReadInt32();
            if (tagCount <= 0 || tagCount > GameplayTagUtility.MaxRegisteredTagCount)
               throw new InvalidDataException("Gameplay tag build count is outside the registry budget.");

            for (int i = 0; i < tagCount; i++)
            {
               reader.SkipBoundedString(BuildTagBinaryFormat.MaxTagNameUtf8Bytes, "tag name");
               reader.SkipBoundedString(BuildTagBinaryFormat.MaxDescriptionUtf8Bytes, "description");
               reader.ReadInt32();
            }

            contentEnd = reader.Position;
            if (reader.Remaining < BuildTagBinaryFormat.ContentHashSize)
               throw new InvalidDataException("Gameplay tag build data is missing its content hash.");

            ulong storedContentHash = reader.ReadUInt64();
            ulong computedContentHash = GameplayTagUtility.ComputeStableHash64(payload.Slice(0, contentEnd));
            if (storedContentHash != computedContentHash)
               throw new InvalidDataException("Gameplay tag build content hash mismatch.");
            if (reader.Remaining != 0)
               throw new InvalidDataException("Gameplay tag build data contains trailing bytes.");
         }

         // Pass 2: the payload is proven intact, so decode and register.
         BuildTagBinaryFormat.SpanReader decoder = new(payload);
         decoder.ReadUInt32();
         decoder.ReadInt32();
         for (int i = 0; i < tagCount; i++)
         {
            string tagName = decoder.ReadBoundedString(BuildTagBinaryFormat.MaxTagNameUtf8Bytes, "tag name");
            string description = decoder.ReadBoundedString(BuildTagBinaryFormat.MaxDescriptionUtf8Bytes, "description");
            GameplayTagFlags flags = (GameplayTagFlags)decoder.ReadInt32();

            BuildTagBinaryFormat.ValidateEntry(tagName, description, flags);

            // A validated manifest declares every tag once. A silent dedupe would hide corruption, so a
            // name the context already holds is rejected - and the count probe allocates nothing, unlike
            // the HashSet the previous reader carried for this check.
            int before = context.RegisteredTagCount;
            context.RegisterTag(tagName, description, flags, this);
            if (context.RegisteredTagCount != before + 1)
               throw new InvalidDataException($"Gameplay tag build data contains duplicate tag '{tagName}'.");
         }
      }
   }
}
