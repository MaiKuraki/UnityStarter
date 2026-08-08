using System;
using System.IO;
using System.Text;
using CycloneGames.GameplayTags.Core;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   /// <summary>
   /// Publishes the runtime gameplay-tag catalog for the duration of a Player build.
   /// </summary>
   public sealed class BuildTags : IPreprocessBuildWithReport, IPostprocessBuildWithReport
   {
      public const string RecoveryStateDirectoryRelativePath =
         ".buildpipeline/transactions/gameplay-tags";

      private static readonly UTF8Encoding s_StrictUtf8 = new(false, true);
      private static GameplayTagsBuildAssetTransaction s_ActiveTransaction;

      public int callbackOrder => 0;

      public void OnPreprocessBuild(BuildReport report)
      {
         if (s_ActiveTransaction != null)
         {
            throw new BuildFailedException(
               "A gameplay-tag build asset transaction is already active in this Unity process.");
         }

         GameplayTagManagerEditorInitialization.ConfigureEditorSources();
         GameplayTagManager.ReloadTags();

         string projectRoot = GameplayTagsBuildAssetTransaction.GetCurrentProjectRoot();
         byte[] data = CreateBuildData();
         s_ActiveTransaction = GameplayTagsBuildAssetTransaction.Begin(
            projectRoot,
            data,
            synchronizeAssetDatabase: true);
      }

      public void OnPostprocessBuild(BuildReport report)
      {
         GameplayTagsBuildAssetTransaction transaction = s_ActiveTransaction;
         s_ActiveTransaction = null;
         if (transaction == null)
         {
            string projectRoot = GameplayTagsBuildAssetTransaction.GetCurrentProjectRoot();
            if (GameplayTagsBuildAssetTransaction.HasPendingState(projectRoot))
            {
               throw new BuildFailedException(
                  "Gameplay-tag build state is pending, but the in-process transaction owner was lost. " +
                  "Run BuildTags.Recover(projectRoot) before starting another build.");
            }
            return;
         }

         try
         {
            transaction.Complete();
         }
         finally
         {
            transaction.Dispose();
         }
      }

      /// <summary>
      /// Explicitly recovers a pending gameplay-tag build asset transaction.
      /// This method is intentionally dependency-free so a build orchestrator can invoke it through reflection.
      /// </summary>
      public static void Recover(string projectRoot)
      {
         GameplayTagsBuildAssetTransaction.Recover(
            projectRoot,
            synchronizeAssetDatabase: true);
      }

      internal static byte[] CreateBuildData()
      {
         ReadOnlySpan<GameplayTag> tags = GameplayTagManager.GetAllTags();
         if (tags.IsEmpty)
            throw new BuildFailedException("Gameplay tag build data must contain at least one definition.");

         int dataSize = CalculateBuildDataSize(tags);
         byte[] data = new byte[dataSize];
         using MemoryStream stream = new(data, 0, data.Length, writable: true, publiclyVisible: true);
         using BinaryWriter writer = new(stream, s_StrictUtf8, true);
         writer.Write(BuildTagBinaryFormat.FileSignature);
         writer.Write(tags.Length);
         for (int i = 0; i < tags.Length; i++)
         {
            GameplayTag tag = tags[i];
            BuildTagBinaryFormat.ValidateEntry(tag.Name, tag.Description, tag.Flags);
            writer.Write(tag.Name);
            writer.Write(tag.Description ?? string.Empty);
            writer.Write((int)tag.Flags);
         }
         writer.Flush();

         if (!stream.TryGetBuffer(out ArraySegment<byte> contentBuffer))
            throw new InvalidOperationException("Gameplay tag build buffer is not accessible.");
         int contentLength = checked((int)stream.Position);
         ulong contentHash = BuildTagBinaryFormat.ComputeContentHash64(
            contentBuffer.Array,
            contentBuffer.Offset,
            contentLength);
         writer.Write(contentHash);
         writer.Flush();
         if (stream.Position != dataSize)
            throw new InvalidOperationException("Gameplay tag build buffer did not match its precomputed size.");
         return data;
      }

      internal static int CalculateBuildDataSize(ReadOnlySpan<GameplayTag> tags)
      {
         long size = sizeof(uint) + sizeof(int) + BuildTagBinaryFormat.ContentHashSize;
         try
         {
            for (int i = 0; i < tags.Length; i++)
            {
               GameplayTag tag = tags[i];
               BuildTagBinaryFormat.ValidateEntry(tag.Name, tag.Description, tag.Flags);
               int nameByteCount = s_StrictUtf8.GetByteCount(tag.Name);
               int descriptionByteCount = s_StrictUtf8.GetByteCount(tag.Description ?? string.Empty);
               size += Get7BitEncodedIntSize(nameByteCount) + nameByteCount;
               size += Get7BitEncodedIntSize(descriptionByteCount) + descriptionByteCount;
               size += sizeof(int);
               ValidateBuildDataSize(size);
            }
         }
         catch (EncoderFallbackException exception)
         {
            throw new BuildFailedException(
               $"Gameplay tag build text contains invalid UTF-16 data: {exception.Message}");
         }

         ValidateBuildDataSize(size);
         return checked((int)size);
      }

      internal static void ValidateBuildDataSize(long size)
      {
         if (size <= 0 || size > BuildTagBinaryFormat.MaxDataSizeBytes)
         {
            throw new BuildFailedException(
               $"Generated gameplay tag data exceeds the {BuildTagBinaryFormat.MaxDataSizeBytes}-byte build budget.");
         }
      }

      internal static int Get7BitEncodedIntSize(int value)
      {
         if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

         int size = 1;
         uint remaining = (uint)value;
         while ((remaining >>= 7) != 0)
            size++;
         return size;
      }
   }
}
