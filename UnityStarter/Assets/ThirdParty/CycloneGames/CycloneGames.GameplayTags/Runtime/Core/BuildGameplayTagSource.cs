using System;
using System.IO;

namespace CycloneGames.GameplayTags.Runtime
{
   /// <summary>
   /// Build tag binary format:
   ///   [byte version=1] [int tagCount] [string tagName × tagCount] [uint crc32]
   /// CRC32 is used for corruption detection (disk I/O errors, truncation).
   /// Not intended for tamper-proof verification — use SHA256/xxHash64 for hot-update integrity.
   /// </summary>
   public static class BuildTagBinaryFormat
   {
      public const byte CurrentVersion = 1;

      public static uint ComputeCrc32(byte[] data, int offset, int length)
      {
         uint crc = 0xFFFFFFFF;
         for (int i = offset; i < offset + length; i++)
         {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
               crc = (crc >> 1) ^ (0xEDB88320 & ~((crc & 1) - 1));
         }
         return crc ^ 0xFFFFFFFF;
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
            if (version != BuildTagBinaryFormat.CurrentVersion)
            {
               GameplayTagLogger.LogError($"[BuildGameplayTagSource] Unsupported binary format version: {version}. Expected: {BuildTagBinaryFormat.CurrentVersion}.");
               return;
            }

            int tagCount = reader.ReadInt32();
            long dataStart = memoryStream.Position;

            for (int i = 0; i < tagCount; i++)
            {
               string tagName = reader.ReadString();
               context.RegisterTag(tagName, string.Empty, GameplayTagFlags.None, this);
            }

            long dataEnd = memoryStream.Position;

            // Verify CRC32 checksum
            if (memoryStream.Position + 4 <= memoryStream.Length)
            {
               uint storedCrc = reader.ReadUInt32();
               uint computedCrc = BuildTagBinaryFormat.ComputeCrc32(data, (int)dataStart, (int)(dataEnd - dataStart));
               if (storedCrc != computedCrc)
               {
                  GameplayTagLogger.LogWarning("[BuildGameplayTagSource] CRC32 checksum mismatch — build tag data may be corrupted.");
               }
            }
         }
         catch (Exception e)
         {
            GameplayTagLogger.LogError($"[BuildGameplayTagSource] Failed to load gameplay tags from build data. Exception: {e}");
         }
      }
   }
}
