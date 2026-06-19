using System;
using System.Collections.Generic;
using System.IO;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Build tag binary format:
   ///   [byte formatVersion] [int tagCount] [string tagName x tagCount] [ulong payloadHash64]
   /// PayloadHash64 is used for corruption detection (disk I/O errors, truncation).
   /// It is not intended for tamper-proof verification; use a signed manifest or SHA256 for hot-update content integrity.
   /// </summary>
   public static class BuildTagBinaryFormat
   {
      public const byte CurrentFormatVersion = 1;
      public const int PayloadHashSize = 8;

      public static ulong ComputePayloadHash64(byte[] data, int offset, int length)
      {
         if (data == null)
         {
            throw new ArgumentNullException(nameof(data));
         }

         if (offset < 0 || length < 0 || data.Length - offset < length)
         {
            throw new ArgumentOutOfRangeException(nameof(length), "Invalid payload hash range.");
         }

         return GameplayTagUtility.ComputeStableHash64(new ReadOnlySpan<byte>(data, offset, length));
      }
   }

   /// <summary>
   /// Loads gameplay tags from a build-time generated Resources asset.
   /// This source is intended for use in builds where tags are pre-compiled for performance.
   /// </summary>
   internal class BuildGameplayTagSource : IGameplayTagSource
   {
      public string Name => "Build";

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         try
         {
            byte[] data = GameplayTagRuntimePlatform.LoadBuildTagData?.Invoke();

            if (data == null || data.Length == 0)
               return;

            using MemoryStream memoryStream = new(data);
            using BinaryReader reader = new(memoryStream);

            byte version = reader.ReadByte();
            if (version != BuildTagBinaryFormat.CurrentFormatVersion)
            {
               GameplayTagLogger.LogError($"[BuildGameplayTagSource] Unsupported binary format version: {version}. Expected: {BuildTagBinaryFormat.CurrentFormatVersion}.");
               return;
            }

            int tagCount = reader.ReadInt32();
            if (tagCount < 0)
            {
               GameplayTagLogger.LogError("[BuildGameplayTagSource] Invalid negative tag count. Build tag data will not be registered.");
               return;
            }

            long dataStart = memoryStream.Position;

            List<string> tagNames = new(tagCount);
            for (int i = 0; i < tagCount; i++)
            {
               string tagName = reader.ReadString();
               tagNames.Add(tagName);
            }

            long dataEnd = memoryStream.Position;

            if (memoryStream.Position + BuildTagBinaryFormat.PayloadHashSize > memoryStream.Length)
            {
               GameplayTagLogger.LogError("[BuildGameplayTagSource] Missing payload hash. Build tag data will not be registered.");
               return;
            }

            ulong storedPayloadHash = reader.ReadUInt64();
            ulong computedPayloadHash = BuildTagBinaryFormat.ComputePayloadHash64(data, (int)dataStart, (int)(dataEnd - dataStart));
            if (storedPayloadHash != computedPayloadHash)
            {
               GameplayTagLogger.LogError("[BuildGameplayTagSource] Payload hash mismatch. Build tag data will not be registered.");
               return;
            }

            if (memoryStream.Position != memoryStream.Length)
            {
               GameplayTagLogger.LogError("[BuildGameplayTagSource] Unexpected trailing bytes. Build tag data will not be registered.");
               return;
            }

            for (int i = 0; i < tagNames.Count; i++)
            {
               context.RegisterTag(tagNames[i], string.Empty, GameplayTagFlags.None, this);
            }
         }
         catch (Exception e)
         {
            GameplayTagLogger.LogError($"[BuildGameplayTagSource] Failed to load gameplay tags from build data. Exception: {e}");
         }
      }
   }
}
