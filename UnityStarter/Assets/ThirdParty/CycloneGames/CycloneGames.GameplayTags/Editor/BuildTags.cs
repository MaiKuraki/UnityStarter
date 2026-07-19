using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   public sealed class BuildTags : IPreprocessBuildWithReport, IPostprocessBuildWithReport
   {
      private const int OwnershipPhasePrepared = 0;
      private const int OwnershipPhasePayloadPromoted = 1;
      private const int OwnershipPhaseImported = 2;
      private const int MaxOwnershipMarkerSizeBytes = 4096;
      private const string GeneratedDirectoryAssetPath = "Assets/Resources";
      private const string GeneratedAssetPath = GeneratedDirectoryAssetPath + "/GameplayTags.bytes";

      private static readonly UTF8Encoding s_StrictUtf8 = new(false, true);

      [Serializable]
      private sealed class OwnershipRecord
      {
         public int phase;
         public string payloadSha256;
         public bool imported;
         public string assetGuid;
      }

      public int callbackOrder => 0;

      private static string AbsoluteDirectoryPath => Path.GetFullPath(
         Path.Combine(Application.dataPath, "Resources"));

      private static string AbsoluteAssetPath => Path.Combine(AbsoluteDirectoryPath, "GameplayTags.bytes");
      private static string AbsoluteAssetMetaPath => AbsoluteAssetPath + ".meta";
      private static string AbsoluteDirectoryMetaPath => AbsoluteDirectoryPath + ".meta";

      private static string OwnershipMarkerPath => Path.GetFullPath(
         Path.Combine(Application.dataPath, "..", "Library", "CycloneGames", "GameplayTags", "BuildAssetOwnership.json"));

      public void OnPreprocessBuild(BuildReport report)
      {
         GameplayTagManagerEditorInitialization.ConfigureEditorSources();
         GameplayTagManager.ReloadTags();

         RecoverOwnedStaleAsset();
         bool directoryExists = Directory.Exists(AbsoluteDirectoryPath);
         ValidateUnownedOutputState(
            File.Exists(AbsoluteAssetPath),
            File.Exists(AbsoluteAssetMetaPath),
            directoryExists,
            File.Exists(AbsoluteDirectoryMetaPath),
            File.Exists(OwnershipMarkerPath));
         RejectReparsePointAncestors(AbsoluteDirectoryPath);

         byte[] data = CreateBuildData();

         OwnershipRecord ownership = new()
         {
            phase = OwnershipPhasePrepared,
            payloadSha256 = ComputeSha256(data),
            imported = false,
            assetGuid = string.Empty
         };

         WriteOwnershipMarker(ownership);
         string temporaryPath = AbsoluteAssetPath + ".tmp-" + Guid.NewGuid().ToString("N");
         try
         {
            Directory.CreateDirectory(AbsoluteDirectoryPath);
            RejectReparsePointAncestors(AbsoluteDirectoryPath);
            if (!directoryExists)
            {
               AssetDatabase.ImportAsset(GeneratedDirectoryAssetPath, ImportAssetOptions.ForceSynchronousImport);
               if (!File.Exists(AbsoluteDirectoryMetaPath))
                  throw new BuildFailedException($"Failed to import '{GeneratedDirectoryAssetPath}'.");
            }

            WriteAllBytesDurably(temporaryPath, data);
            File.Move(temporaryPath, AbsoluteAssetPath);
            ownership.phase = OwnershipPhasePayloadPromoted;
            WriteOwnershipMarker(ownership);
            AssetDatabase.ImportAsset(GeneratedAssetPath, ImportAssetOptions.ForceSynchronousImport);

            ownership.assetGuid = ReadMetaGuid(AbsoluteAssetMetaPath);
            if (string.IsNullOrEmpty(ownership.assetGuid))
               throw new BuildFailedException($"Failed to establish ownership of '{GeneratedAssetPath}'.");
            ownership.imported = true;
            ownership.phase = OwnershipPhaseImported;
            WriteOwnershipMarker(ownership);
         }
         catch
         {
            TryCleanupAfterFailedPreprocess();
            throw;
         }
         finally
         {
            if (File.Exists(temporaryPath))
               File.Delete(temporaryPath);
         }
      }

      public void OnPostprocessBuild(BuildReport report)
      {
         CleanupOwnedAsset(throwOnAmbiguousState: false);
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
            throw new BuildFailedException($"Gameplay tag build text contains invalid UTF-16 data: {exception.Message}");
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

      internal static void ValidateUnownedOutputState(
         bool payloadExists,
         bool assetMetaExists,
         bool directoryExists,
         bool directoryMetaExists,
         bool ownershipMarkerExists)
      {
         if (ownershipMarkerExists)
            throw new BuildFailedException("A gameplay tag build ownership marker must be recovered before creating build data.");
         if (payloadExists || assetMetaExists)
            throw new BuildFailedException(
               $"Refusing to overwrite '{GeneratedAssetPath}' or its metadata. Move or remove the user-owned path before building.");
         if (directoryExists != directoryMetaExists)
            throw new BuildFailedException(
               $"Refusing to modify the inconsistent directory/metadata state for '{GeneratedDirectoryAssetPath}'. Resolve it before building.");
      }

      private static void RecoverOwnedStaleAsset()
      {
         if (!File.Exists(OwnershipMarkerPath))
            return;
         CleanupOwnedAsset(throwOnAmbiguousState: true);
      }

      private static void CleanupOwnedAsset(bool throwOnAmbiguousState)
      {
         if (!File.Exists(OwnershipMarkerPath))
            return;

         OwnershipRecord ownership;
         try
         {
            ownership = ReadOwnershipMarker();
            DeleteOwnedPayload(ownership);
            DeleteOwnershipMarker();
         }
         catch (Exception exception)
         {
            string message = $"Gameplay tag build output was preserved because ownership could not be proven: {exception.Message}";
            if (throwOnAmbiguousState)
               throw new BuildFailedException(message);
            Debug.LogError(message);
         }
      }

      private static void DeleteOwnedPayload(OwnershipRecord ownership)
      {
         bool payloadExists = File.Exists(AbsoluteAssetPath);
         bool metaExists = File.Exists(AbsoluteAssetMetaPath);
         if (!payloadExists && !metaExists)
            return;

         ValidatePayloadCleanupPhase(ownership.phase, payloadExists, metaExists);

         if (payloadExists)
         {
            string actualHash = ComputeSha256File(AbsoluteAssetPath, BuildTagBinaryFormat.MaxDataSizeBytes);
            if (!string.Equals(ownership.payloadSha256, actualHash, StringComparison.Ordinal))
               throw new InvalidDataException($"The payload hash for '{GeneratedAssetPath}' no longer matches the ownership marker.");
         }

         if (metaExists)
         {
            if (ownership.phase != OwnershipPhaseImported || !ownership.imported || string.IsNullOrEmpty(ownership.assetGuid))
               throw new InvalidDataException($"Metadata for '{GeneratedAssetPath}' exists without a committed ownership record.");
            string actualGuid = ReadMetaGuid(AbsoluteAssetMetaPath);
            if (!string.Equals(ownership.assetGuid, actualGuid, StringComparison.OrdinalIgnoreCase))
               throw new InvalidDataException($"The GUID for '{GeneratedAssetPath}' no longer matches the ownership marker.");
         }

         if (payloadExists && metaExists)
         {
            if (!AssetDatabase.DeleteAsset(GeneratedAssetPath) && File.Exists(AbsoluteAssetPath))
               throw new IOException($"Failed to delete owned build asset '{GeneratedAssetPath}'.");
         }
         else
         {
            if (payloadExists)
               File.Delete(AbsoluteAssetPath);
            if (metaExists)
               File.Delete(AbsoluteAssetMetaPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
         }
      }

      internal static void ValidatePayloadCleanupPhase(int phase, bool payloadExists, bool metaExists)
      {
         if (!payloadExists && !metaExists)
            return;
         if (phase < OwnershipPhasePayloadPromoted)
         {
            throw new InvalidDataException(
               $"The reserved build output exists before the ownership transaction recorded payload promotion. " +
               "The file was preserved because this transaction cannot prove that it created the file.");
         }
         if (metaExists && phase < OwnershipPhaseImported)
         {
            throw new InvalidDataException(
               "Build asset metadata exists before the ownership transaction committed its imported asset GUID.");
         }
      }

      private static OwnershipRecord ReadOwnershipMarker()
      {
         string json = ReadBoundedUtf8File(OwnershipMarkerPath, MaxOwnershipMarkerSizeBytes);
         OwnershipRecord ownership = JsonUtility.FromJson<OwnershipRecord>(json);
         if (ownership == null ||
             ownership.phase < OwnershipPhasePrepared || ownership.phase > OwnershipPhaseImported ||
             string.IsNullOrEmpty(ownership.payloadSha256) || ownership.payloadSha256.Length != 64 ||
             (ownership.phase == OwnershipPhaseImported &&
              (!ownership.imported || string.IsNullOrEmpty(ownership.assetGuid))))
         {
            throw new InvalidDataException("The gameplay tag build ownership marker is invalid.");
         }
         return ownership;
      }

      private static void WriteOwnershipMarker(OwnershipRecord ownership)
      {
         string directory = Path.GetDirectoryName(OwnershipMarkerPath);
         Directory.CreateDirectory(directory);
         string temporaryPath = OwnershipMarkerPath + ".tmp-" + Guid.NewGuid().ToString("N");
         try
         {
            byte[] data = new System.Text.UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(ownership));
            if (data.Length > MaxOwnershipMarkerSizeBytes)
               throw new InvalidDataException("The gameplay tag build ownership marker exceeds its size budget.");
            WriteAllBytesDurably(temporaryPath, data);
            if (File.Exists(OwnershipMarkerPath))
               File.Replace(temporaryPath, OwnershipMarkerPath, null);
            else
               File.Move(temporaryPath, OwnershipMarkerPath);
         }
         finally
         {
            if (File.Exists(temporaryPath))
               File.Delete(temporaryPath);
         }
      }

      private static void TryCleanupAfterFailedPreprocess()
      {
         try
         {
            CleanupOwnedAsset(throwOnAmbiguousState: true);
         }
         catch (Exception cleanupException)
         {
            Debug.LogError($"Gameplay tag build preprocessing failed and automatic cleanup stopped safely: {cleanupException.Message}");
         }
      }

      private static void DeleteOwnershipMarker()
      {
         if (File.Exists(OwnershipMarkerPath))
            File.Delete(OwnershipMarkerPath);
      }

      private static void RejectReparsePointAncestors(string path)
      {
         string assetsRoot = Path.GetFullPath(Application.dataPath);
         string current = Path.GetFullPath(path);
         while (!string.IsNullOrEmpty(current))
         {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
               throw new BuildFailedException("Gameplay tag build output paths cannot traverse symbolic links or junctions.");
            if (string.Equals(current, assetsRoot, Path.DirectorySeparatorChar == '\\'
                   ? StringComparison.OrdinalIgnoreCase
                   : StringComparison.Ordinal))
               return;
            current = Path.GetDirectoryName(current);
         }
         throw new BuildFailedException("Gameplay tag build output directory is outside the project Assets directory.");
      }

      private static string ReadMetaGuid(string metaPath)
      {
         if (!File.Exists(metaPath))
            return string.Empty;
         foreach (string line in File.ReadLines(metaPath))
         {
            if (line.StartsWith("guid: ", StringComparison.Ordinal))
               return line.Substring("guid: ".Length).Trim();
         }
         return string.Empty;
      }

      private static void WriteAllBytesDurably(string path, byte[] data)
      {
         using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
         stream.Write(data, 0, data.Length);
         stream.Flush(flushToDisk: true);
      }

      private static string ComputeSha256(byte[] data)
      {
         using SHA256 sha256 = SHA256.Create();
         return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", string.Empty);
      }

      internal static string ComputeSha256File(string path, long maxLength)
      {
         using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
         long length = stream.Length;
         if (length <= 0 || length > maxLength)
            throw new InvalidDataException($"File '{path}' is outside the allowed {maxLength}-byte recovery budget.");

         using ExactLengthReadStream boundedStream = new(stream, length, leaveOpen: true);
         using SHA256 sha256 = SHA256.Create();
         return BitConverter.ToString(sha256.ComputeHash(boundedStream)).Replace("-", string.Empty);
      }

      internal static string ReadBoundedUtf8File(string path, int maxLength)
      {
         using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
         long length = stream.Length;
         if (length <= 0 || length > maxLength)
            throw new InvalidDataException($"File '{path}' is outside the allowed {maxLength}-byte recovery budget.");
         Utf8FileSafety.RejectByteOrderMark(stream, path);

         using ExactLengthReadStream boundedStream = new(stream, length, leaveOpen: true);
         using StreamReader reader = new(
            boundedStream,
            new System.Text.UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: false);
         return reader.ReadToEnd();
      }
   }
}
