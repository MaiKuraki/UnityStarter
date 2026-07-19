using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Build tag binary format:
   /// [uint fileSignature] [int tagCount]
   /// [string tagName] [string description] [int flags] x tagCount
   /// [ulong contentHash64]
   /// </summary>
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

      internal static string ReadBoundedString(BinaryReader reader, int maxUtf8Bytes, string fieldName)
      {
         int byteLength = Read7BitEncodedInt(reader, fieldName);
         if (byteLength < 0 || byteLength > maxUtf8Bytes)
            throw new InvalidDataException($"Gameplay tag build {fieldName} exceeds its UTF-8 byte budget.");

         byte[] bytes = reader.ReadBytes(byteLength);
         if (bytes.Length != byteLength)
            throw new InvalidDataException($"Gameplay tag build data ended while reading {fieldName}.");

         try
         {
            return s_StrictUtf8.GetString(bytes);
         }
         catch (DecoderFallbackException exception)
         {
            throw new InvalidDataException($"Gameplay tag build {fieldName} is not valid UTF-8.", exception);
         }
      }

      private static int Read7BitEncodedInt(BinaryReader reader, string fieldName)
      {
         uint value = 0;
         for (int shift = 0; shift < 35; shift += 7)
         {
            byte current;
            try
            {
               current = reader.ReadByte();
            }
            catch (EndOfStreamException exception)
            {
               throw new InvalidDataException($"Gameplay tag build data ended while reading {fieldName} length.", exception);
            }

            if (shift == 28 && (current & 0xF0) != 0)
               throw new InvalidDataException($"Gameplay tag build {fieldName} length is invalid.");

            value |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
               if (shift > 0 && current == 0)
                  throw new InvalidDataException($"Gameplay tag build {fieldName} length is not canonically encoded.");
               return checked((int)value);
            }
         }

         throw new InvalidDataException($"Gameplay tag build {fieldName} length is invalid.");
      }
   }

   internal sealed class BuildGameplayTagSource : IGameplayTagSource
   {
      private readonly struct BuildTagEntry
      {
         public readonly string Name;
         public readonly string Description;
         public readonly GameplayTagFlags Flags;

         public BuildTagEntry(string name, string description, GameplayTagFlags flags)
         {
            Name = name;
            Description = description;
            Flags = flags;
         }
      }

      public string Name => "Build";

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         if (context == null)
            throw new ArgumentNullException(nameof(context));

         byte[] data = GameplayTagRuntimePlatform.LoadBuildTagData?.Invoke();
         if (data == null || data.Length == 0)
            throw new InvalidDataException("Gameplay tag build data is missing or empty.");
         if (data.Length > BuildTagBinaryFormat.MaxDataSizeBytes)
            throw new InvalidDataException($"Gameplay tag build data exceeds {BuildTagBinaryFormat.MaxDataSizeBytes} bytes.");

         try
         {
            using MemoryStream memoryStream = new(data, false);
            using BinaryReader reader = new(memoryStream, Encoding.UTF8, false);

            uint signature = reader.ReadUInt32();
            if (signature != BuildTagBinaryFormat.FileSignature)
               throw new InvalidDataException("Gameplay tag build data has an invalid file signature.");

            int tagCount = reader.ReadInt32();
            if (tagCount <= 0 || tagCount > GameplayTagUtility.MaxRegisteredTagCount)
               throw new InvalidDataException("Gameplay tag build count is outside the registry budget.");

            List<BuildTagEntry> entries = new(tagCount);
            HashSet<string> names = new(StringComparer.Ordinal);
            for (int i = 0; i < tagCount; i++)
            {
               string tagName = BuildTagBinaryFormat.ReadBoundedString(
                  reader, BuildTagBinaryFormat.MaxTagNameUtf8Bytes, "tag name");
               string description = BuildTagBinaryFormat.ReadBoundedString(
                  reader, BuildTagBinaryFormat.MaxDescriptionUtf8Bytes, "description");
               GameplayTagFlags flags = (GameplayTagFlags)reader.ReadInt32();
               BuildTagBinaryFormat.ValidateEntry(tagName, description, flags);
               if (!names.Add(tagName))
                  throw new InvalidDataException($"Gameplay tag build data contains duplicate tag '{tagName}'.");

               entries.Add(new BuildTagEntry(tagName, description, flags));
            }

            long contentEnd = memoryStream.Position;
            if (memoryStream.Length - memoryStream.Position < BuildTagBinaryFormat.ContentHashSize)
               throw new InvalidDataException("Gameplay tag build data is missing its content hash.");

            ulong storedContentHash = reader.ReadUInt64();
            ulong computedContentHash = BuildTagBinaryFormat.ComputeContentHash64(
               data, 0, checked((int)contentEnd));
            if (storedContentHash != computedContentHash)
               throw new InvalidDataException("Gameplay tag build content hash mismatch.");
            if (memoryStream.Position != memoryStream.Length)
               throw new InvalidDataException("Gameplay tag build data contains trailing bytes.");

            for (int i = 0; i < entries.Count; i++)
            {
               BuildTagEntry entry = entries[i];
               context.RegisterTag(entry.Name, entry.Description, entry.Flags, this);
            }
         }
         catch (EndOfStreamException exception)
         {
            throw new InvalidDataException("Gameplay tag build data is truncated.", exception);
         }
      }
   }
}
