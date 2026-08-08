using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CycloneGames.GameplayTags.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   internal sealed class GameplayTagsBuildAssetTransaction : IDisposable
   {
      internal const string GeneratedAssetPath =
         "Assets/Generated/CycloneGames.GameplayTags/Resources/CycloneGames.GameplayTags/GameplayTags.bytes";

      private const int JournalSchemaVersion = 1;
      private const int EffectKindDirectory = 0;
      private const int EffectKindFile = 1;
      private const int MaxEffectCount = 16;
      private const int MaxJournalSizeBytes = 128 * 1024;
      private const int MaxMetaFileSizeBytes = 16 * 1024;
      private const int MaxRelativePathLength = 512;
      private const string JournalFileName = "active.json";
      private const string JournalCandidateFileName = "active.json.new";
      private const string LockFileName = "build.lock";
      private const string ScratchDirectoryName = "scratch";

      private static readonly UTF8Encoding s_StrictUtf8 = new(false, true);
      private static readonly StringComparison s_PathComparison =
         Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
      private static readonly StringComparer s_PathComparer =
         Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

      private static readonly string[] s_GeneratedDirectoryPaths =
      {
         "Assets/Generated",
         "Assets/Generated/CycloneGames.GameplayTags",
         "Assets/Generated/CycloneGames.GameplayTags/Resources",
         "Assets/Generated/CycloneGames.GameplayTags/Resources/CycloneGames.GameplayTags"
      };

      [Serializable]
      private sealed class JournalRecord
      {
         public int schemaVersion;
         public string transactionId;
         public string phase;
         public string createdUtc;
         public int appliedEffectCount;
         public EffectRecord[] effects;
      }

      [Serializable]
      private sealed class EffectRecord
      {
         public int kind;
         public string relativePath;
         public bool beforeExists;
         public string beforeSha256;
         public string expectedSha256;
         public int maxLengthBytes;
         public string assetGuid;
         public string scratchFileName;
      }

      private sealed class PlannedEffect
      {
         public EffectRecord Record;
         public byte[] Content;
      }

      private readonly string m_ProjectRoot;
      private readonly string m_StateRoot;
      private readonly string m_JournalPath;
      private readonly string m_JournalCandidatePath;
      private readonly string m_ScratchRoot;
      private readonly bool m_SynchronizeAssetDatabase;
      private FileStream m_LockStream;
      private JournalRecord m_Journal;
      private bool m_Completed;

      private GameplayTagsBuildAssetTransaction(
         string projectRoot,
         string stateRoot,
         FileStream lockStream,
         bool synchronizeAssetDatabase)
      {
         m_ProjectRoot = projectRoot;
         m_StateRoot = stateRoot;
         m_JournalPath = Path.Combine(stateRoot, JournalFileName);
         m_JournalCandidatePath = Path.Combine(stateRoot, JournalCandidateFileName);
         m_ScratchRoot = Path.Combine(stateRoot, ScratchDirectoryName);
         m_LockStream = lockStream;
         m_SynchronizeAssetDatabase = synchronizeAssetDatabase;
      }

      internal static string GetCurrentProjectRoot()
      {
         return NormalizeProjectRoot(Path.Combine(Application.dataPath, ".."));
      }

      internal static GameplayTagsBuildAssetTransaction Begin(
         string projectRoot,
         byte[] payload,
         bool synchronizeAssetDatabase)
      {
         if (payload == null)
            throw new ArgumentNullException(nameof(payload));
         if (payload.Length <= 0 || payload.Length > BuildTagBinaryFormat.MaxDataSizeBytes)
         {
            throw new BuildFailedException(
               $"Gameplay-tag build payload is outside the {BuildTagBinaryFormat.MaxDataSizeBytes}-byte budget.");
         }

         GameplayTagsBuildAssetTransaction transaction = Acquire(
            projectRoot,
            synchronizeAssetDatabase);
         Exception primaryFailure = null;
         try
         {
            transaction.ThrowIfPendingOrUnknownState();
            List<PlannedEffect> effects = transaction.CreatePlan(payload);
            transaction.m_Journal = CreateJournal(effects);
            transaction.PersistJournal();
            transaction.ApplyEffects(effects);
            transaction.SynchronizeAndValidateImportedAsset();
            transaction.m_Journal.phase = "Published";
            transaction.PersistJournal();
            return transaction;
         }
         catch (Exception exception)
         {
            primaryFailure = exception;
            Exception cleanupFailure = null;
            if (transaction.m_Journal != null)
            {
               try
               {
                  transaction.CleanupJournaledEffects();
               }
               catch (Exception cleanupException)
               {
                  cleanupFailure = cleanupException;
               }
            }

            transaction.Dispose();
            if (cleanupFailure != null)
            {
               throw new BuildFailedException(
                  "Gameplay-tag build preprocessing failed and rollback also failed. " +
                  $"The recovery journal was retained. Primary failure: {primaryFailure.Message} " +
                  $"Rollback failure: {cleanupFailure.Message}");
            }
            throw ToBuildFailure(primaryFailure);
         }
      }

      internal static bool HasPendingState(string projectRoot)
      {
         string normalizedRoot = NormalizeProjectRoot(projectRoot);
         string stateRoot = ResolveProjectRelativePath(
            normalizedRoot,
            BuildTags.RecoveryStateDirectoryRelativePath);
         if (!Directory.Exists(stateRoot))
            return false;
         RejectReparsePoint(stateRoot);

         foreach (string entry in Directory.EnumerateFileSystemEntries(stateRoot))
         {
            if (!string.Equals(Path.GetFileName(entry), LockFileName, StringComparison.Ordinal))
               return true;
         }

         string lockPath = Path.Combine(stateRoot, LockFileName);
         if (!File.Exists(lockPath))
            return false;
         try
         {
            using FileStream stream = new(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return stream.Length != 0;
         }
         catch (IOException)
         {
            return true;
         }
      }

      internal static void Recover(string projectRoot, bool synchronizeAssetDatabase)
      {
         GameplayTagsBuildAssetTransaction transaction = Acquire(
            projectRoot,
            synchronizeAssetDatabase);
         try
         {
            JournalRecord journal = transaction.LoadAndReconcileJournal();
            if (journal == null)
            {
               transaction.ThrowIfUnknownStateWithoutJournal();
               return;
            }

            transaction.m_Journal = journal;
            transaction.CleanupJournaledEffects();
         }
         catch (Exception exception)
         {
            throw ToBuildFailure(exception);
         }
         finally
         {
            transaction.Dispose();
         }
      }

      internal void Complete()
      {
         if (m_Completed)
            throw new InvalidOperationException("The gameplay-tag build asset transaction is already complete.");
         if (m_Journal == null)
            throw new InvalidOperationException("The gameplay-tag build asset transaction has no active journal.");

         try
         {
            CleanupJournaledEffects();
         }
         catch (Exception exception)
         {
            throw ToBuildFailure(exception);
         }
      }

      public void Dispose()
      {
         FileStream lockStream = m_LockStream;
         m_LockStream = null;
         lockStream?.Dispose();
      }

      private static GameplayTagsBuildAssetTransaction Acquire(
         string projectRoot,
         bool synchronizeAssetDatabase)
      {
         string normalizedRoot = NormalizeProjectRoot(projectRoot);
         string stateRoot = ResolveProjectRelativePath(
            normalizedRoot,
            BuildTags.RecoveryStateDirectoryRelativePath);
         EnsureDirectoryPathCanBeCreated(normalizedRoot, stateRoot);
         Directory.CreateDirectory(stateRoot);
         RejectReparsePoint(stateRoot);

         string lockPath = Path.Combine(stateRoot, LockFileName);
         RejectReparsePointIfPresent(lockPath);
         FileStream lockStream;
         try
         {
            lockStream = new FileStream(
               lockPath,
               FileMode.OpenOrCreate,
               FileAccess.ReadWrite,
               FileShare.None,
               bufferSize: 1,
               FileOptions.WriteThrough);
         }
         catch (IOException exception)
         {
            throw new BuildFailedException(
               $"Gameplay-tag build state is locked by another process: {exception.Message}");
         }

         if (lockStream.Length != 0)
         {
            lockStream.Dispose();
            throw new BuildFailedException("Gameplay-tag transaction lock contains unexpected data.");
         }

         return new GameplayTagsBuildAssetTransaction(
            normalizedRoot,
            stateRoot,
            lockStream,
            synchronizeAssetDatabase);
      }

      private List<PlannedEffect> CreatePlan(byte[] payload)
      {
         ValidateGeneratedOutputState();
         List<PlannedEffect> effects = new(MaxEffectCount);
         foreach (string directoryPath in s_GeneratedDirectoryPaths)
         {
            string absoluteDirectoryPath = ResolveProjectRelativePath(m_ProjectRoot, directoryPath);
            if (Directory.Exists(absoluteDirectoryPath))
               continue;

            effects.Add(new PlannedEffect
            {
               Record = CreateDirectoryEffect(directoryPath)
            });

            string folderGuid = Guid.NewGuid().ToString("N");
            byte[] meta = CreateMetaFile(folderGuid, isFolder: true);
            effects.Add(new PlannedEffect
            {
               Record = CreateFileEffect(directoryPath + ".meta", meta, folderGuid, effects.Count),
               Content = meta
            });
         }

         effects.Add(new PlannedEffect
         {
            Record = CreateFileEffect(GeneratedAssetPath, payload, string.Empty, effects.Count),
            Content = payload
         });

         string assetGuid = Guid.NewGuid().ToString("N");
         byte[] assetMeta = CreateMetaFile(assetGuid, isFolder: false);
         effects.Add(new PlannedEffect
         {
            Record = CreateFileEffect(GeneratedAssetPath + ".meta", assetMeta, assetGuid, effects.Count),
            Content = assetMeta
         });

         if (effects.Count > MaxEffectCount)
            throw new InvalidDataException("Gameplay-tag build effect count exceeds its recovery budget.");
         return effects;
      }

      private void ValidateGeneratedOutputState()
      {
         foreach (string directoryPath in s_GeneratedDirectoryPaths)
         {
            string absoluteDirectoryPath = ResolveProjectRelativePath(m_ProjectRoot, directoryPath);
            string absoluteMetaPath = absoluteDirectoryPath + ".meta";
            bool directoryExists = Directory.Exists(absoluteDirectoryPath);
            bool metaExists = File.Exists(absoluteMetaPath);
            if (directoryExists != metaExists)
            {
               throw new BuildFailedException(
                  $"Refusing to modify inconsistent directory metadata at '{directoryPath}'.");
            }

            if (!directoryExists)
               continue;
            RejectReparsePoint(absoluteDirectoryPath);
            ValidateRegularFile(absoluteMetaPath, MaxMetaFileSizeBytes);
            if (string.IsNullOrEmpty(ReadMetaGuid(absoluteMetaPath)))
               throw new BuildFailedException($"Directory metadata at '{directoryPath}.meta' has no valid GUID.");
         }

         string outputPath = ResolveProjectRelativePath(m_ProjectRoot, GeneratedAssetPath);
         if (File.Exists(outputPath) || Directory.Exists(outputPath) ||
             File.Exists(outputPath + ".meta") || Directory.Exists(outputPath + ".meta"))
         {
            throw new BuildFailedException(
               $"Refusing to overwrite reserved gameplay-tag build output '{GeneratedAssetPath}'.");
         }
      }

      private static JournalRecord CreateJournal(List<PlannedEffect> effects)
      {
         EffectRecord[] records = new EffectRecord[effects.Count];
         for (int i = 0; i < effects.Count; i++)
            records[i] = effects[i].Record;
         return new JournalRecord
         {
            schemaVersion = JournalSchemaVersion,
            transactionId = Guid.NewGuid().ToString("N"),
            phase = "Prepared",
            createdUtc = DateTime.UtcNow.ToString("O"),
            appliedEffectCount = 0,
            effects = records
         };
      }

      private static EffectRecord CreateDirectoryEffect(string relativePath)
      {
         return new EffectRecord
         {
            kind = EffectKindDirectory,
            relativePath = relativePath,
            beforeExists = false,
            beforeSha256 = string.Empty,
            expectedSha256 = string.Empty,
            maxLengthBytes = 0,
            assetGuid = string.Empty,
            scratchFileName = string.Empty
         };
      }

      private static EffectRecord CreateFileEffect(
         string relativePath,
         byte[] content,
         string assetGuid,
         int effectIndex)
      {
         int maxLength = relativePath.EndsWith(".meta", StringComparison.Ordinal)
            ? MaxMetaFileSizeBytes
            : BuildTagBinaryFormat.MaxDataSizeBytes;
         return new EffectRecord
         {
            kind = EffectKindFile,
            relativePath = relativePath,
            beforeExists = false,
            beforeSha256 = string.Empty,
            expectedSha256 = ComputeSha256(content),
            maxLengthBytes = maxLength,
            assetGuid = assetGuid ?? string.Empty,
            scratchFileName = $"effect-{effectIndex:D2}.pending"
         };
      }

      private void ApplyEffects(List<PlannedEffect> effects)
      {
         Directory.CreateDirectory(m_ScratchRoot);
         RejectReparsePoint(m_ScratchRoot);

         for (int i = 0; i < effects.Count; i++)
         {
            PlannedEffect effect = effects[i];
            string targetPath = ResolveProjectRelativePath(m_ProjectRoot, effect.Record.relativePath);
            if (effect.Record.kind == EffectKindDirectory)
            {
               if (Directory.Exists(targetPath) || File.Exists(targetPath))
                  throw new IOException($"Planned directory target appeared unexpectedly: '{effect.Record.relativePath}'.");
               Directory.CreateDirectory(targetPath);
               RejectReparsePoint(targetPath);
            }
            else
            {
               string scratchPath = Path.Combine(m_ScratchRoot, effect.Record.scratchFileName);
               if (File.Exists(scratchPath) || Directory.Exists(scratchPath))
                  throw new IOException($"Transaction scratch target already exists: '{effect.Record.scratchFileName}'.");
               WriteAllBytesDurably(scratchPath, effect.Content);
               if (!string.Equals(
                      ComputeSha256File(scratchPath, effect.Record.maxLengthBytes),
                      effect.Record.expectedSha256,
                      StringComparison.Ordinal))
               {
                  throw new IOException($"Transaction scratch verification failed for '{effect.Record.relativePath}'.");
               }
               if (File.Exists(targetPath) || Directory.Exists(targetPath))
                  throw new IOException($"Planned file target appeared unexpectedly: '{effect.Record.relativePath}'.");
               File.Move(scratchPath, targetPath);
            }

            m_Journal.appliedEffectCount = i + 1;
            PersistJournal();
         }
      }

      private void SynchronizeAndValidateImportedAsset()
      {
         if (!m_SynchronizeAssetDatabase)
            return;

         AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
         EffectRecord assetMeta = m_Journal.effects.Single(
            static effect => effect.relativePath == GeneratedAssetPath + ".meta");
         string importedGuid = AssetDatabase.AssetPathToGUID(GeneratedAssetPath);
         if (string.IsNullOrEmpty(importedGuid) ||
             !string.Equals(importedGuid, assetMeta.assetGuid, StringComparison.OrdinalIgnoreCase))
         {
            throw new BuildFailedException(
               $"Unity did not import '{GeneratedAssetPath}' with the journaled GUID.");
         }
      }

      private void CleanupJournaledEffects()
      {
         ValidateJournal(m_Journal);
         PreflightCleanup(m_Journal);

         m_Journal.phase = "Cleaning";
         PersistJournal();
         for (int i = m_Journal.effects.Length - 1; i >= 0; i--)
            DeleteVerifiedEffect(m_Journal.effects[i]);

         if (m_SynchronizeAssetDatabase)
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

         DeleteKnownScratch(m_Journal);
         DeleteJournalFiles();
         m_Journal = null;
         m_Completed = true;
      }

      private void PreflightCleanup(JournalRecord journal)
      {
         ThrowIfUnknownStateEntries(journal);
         HashSet<string> plannedPaths = new(s_PathComparer);
         foreach (EffectRecord effect in journal.effects)
            plannedPaths.Add(ResolveProjectRelativePath(m_ProjectRoot, effect.relativePath));

         foreach (EffectRecord effect in journal.effects)
         {
            string path = ResolveProjectRelativePath(m_ProjectRoot, effect.relativePath);
            if (effect.kind == EffectKindFile)
            {
               if (Directory.Exists(path))
                  throw new InvalidDataException($"Expected a file but found a directory at '{effect.relativePath}'.");
               if (File.Exists(path))
                  VerifyKnownFile(path, effect);
               continue;
            }

            if (File.Exists(path))
               throw new InvalidDataException($"Expected a directory but found a file at '{effect.relativePath}'.");
            if (!Directory.Exists(path))
               continue;
            RejectReparsePoint(path);
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
               if (!plannedPaths.Contains(Path.GetFullPath(entry)))
               {
                  throw new InvalidDataException(
                     $"Unknown content exists in transaction-owned directory '{effect.relativePath}': " +
                     $"'{Path.GetFileName(entry)}'. No files were deleted.");
               }
            }
         }

         ValidateKnownScratch(journal);
      }

      private void DeleteVerifiedEffect(EffectRecord effect)
      {
         string path = ResolveProjectRelativePath(m_ProjectRoot, effect.relativePath);
         if (effect.kind == EffectKindFile)
         {
            if (!File.Exists(path))
               return;
            VerifyKnownFile(path, effect);
            File.Delete(path);
            if (File.Exists(path))
               throw new IOException($"Failed to delete transaction-owned file '{effect.relativePath}'.");
            return;
         }

         if (!Directory.Exists(path))
            return;
         RejectReparsePoint(path);
         if (Directory.EnumerateFileSystemEntries(path).Any())
         {
            throw new InvalidDataException(
               $"Transaction-owned directory '{effect.relativePath}' is not empty; it was preserved.");
         }
         Directory.Delete(path, recursive: false);
         if (Directory.Exists(path))
            throw new IOException($"Failed to delete transaction-owned directory '{effect.relativePath}'.");
      }

      private static void VerifyKnownFile(string path, EffectRecord effect)
      {
         ValidateRegularFile(path, effect.maxLengthBytes);
         string actualHash = ComputeSha256File(path, effect.maxLengthBytes);
         if (!string.Equals(actualHash, effect.expectedSha256, StringComparison.Ordinal))
         {
            throw new InvalidDataException(
               $"Transaction-owned file '{effect.relativePath}' no longer matches its journaled hash.");
         }
      }

      private void ValidateKnownScratch(JournalRecord journal)
      {
         if (!Directory.Exists(m_ScratchRoot))
            return;
         RejectReparsePoint(m_ScratchRoot);
         HashSet<string> allowedNames = new(
            journal.effects
               .Where(static effect => effect.kind == EffectKindFile)
               .Select(static effect => effect.scratchFileName),
            StringComparer.Ordinal);
         foreach (string entry in Directory.EnumerateFileSystemEntries(m_ScratchRoot))
         {
            if (Directory.Exists(entry) || !allowedNames.Contains(Path.GetFileName(entry)))
            {
               throw new InvalidDataException(
                  $"Unknown transaction scratch entry '{Path.GetFileName(entry)}' was preserved.");
            }
            RejectReparsePointIfPresent(entry);
         }
      }

      private void DeleteKnownScratch(JournalRecord journal)
      {
         if (!Directory.Exists(m_ScratchRoot))
            return;
         ValidateKnownScratch(journal);
         foreach (EffectRecord effect in journal.effects)
         {
            if (effect.kind != EffectKindFile)
               continue;
            string path = Path.Combine(m_ScratchRoot, effect.scratchFileName);
            if (File.Exists(path))
               File.Delete(path);
         }
         if (Directory.EnumerateFileSystemEntries(m_ScratchRoot).Any())
            throw new InvalidDataException("Gameplay-tag transaction scratch directory contains unknown entries.");
         Directory.Delete(m_ScratchRoot, recursive: false);
      }

      private void PersistJournal()
      {
         ValidateJournal(m_Journal);
         if (File.Exists(m_JournalCandidatePath) || Directory.Exists(m_JournalCandidatePath))
         {
            throw new IOException(
               $"Journal candidate '{JournalCandidateFileName}' already exists; explicit recovery is required.");
         }

         byte[] bytes = s_StrictUtf8.GetBytes(JsonUtility.ToJson(m_Journal, prettyPrint: false));
         if (bytes.Length <= 0 || bytes.Length > MaxJournalSizeBytes)
            throw new InvalidDataException("Gameplay-tag transaction journal exceeds its size budget.");
         WriteAllBytesDurably(m_JournalCandidatePath, bytes);
         if (File.Exists(m_JournalPath))
            File.Replace(m_JournalCandidatePath, m_JournalPath, null);
         else
            File.Move(m_JournalCandidatePath, m_JournalPath);
      }

      private JournalRecord LoadAndReconcileJournal()
      {
         bool activeExists = File.Exists(m_JournalPath);
         bool candidateExists = File.Exists(m_JournalCandidatePath);
         if (!activeExists && !candidateExists)
            return null;
         if (Directory.Exists(m_JournalPath) || Directory.Exists(m_JournalCandidatePath))
            throw new InvalidDataException("Gameplay-tag journal path has an invalid filesystem type.");

         JournalRecord active = activeExists ? ReadJournal(m_JournalPath) : null;
         JournalRecord candidate = candidateExists ? ReadJournal(m_JournalCandidatePath) : null;
         if (active != null && candidate != null)
         {
            if (!HaveSamePlan(active, candidate))
               throw new InvalidDataException("Gameplay-tag journal and candidate describe different transactions.");
            if (candidate.appliedEffectCount >= active.appliedEffectCount)
            {
               File.Replace(m_JournalCandidatePath, m_JournalPath, null);
               return candidate;
            }

            File.Delete(m_JournalCandidatePath);
            return active;
         }

         if (candidate != null)
         {
            File.Move(m_JournalCandidatePath, m_JournalPath);
            return candidate;
         }
         return active;
      }

      private static bool HaveSamePlan(JournalRecord left, JournalRecord right)
      {
         if (!string.Equals(left.transactionId, right.transactionId, StringComparison.Ordinal) ||
             left.effects.Length != right.effects.Length)
            return false;
         for (int i = 0; i < left.effects.Length; i++)
         {
            EffectRecord a = left.effects[i];
            EffectRecord b = right.effects[i];
            if (a.kind != b.kind ||
                !string.Equals(a.relativePath, b.relativePath, StringComparison.Ordinal) ||
                !string.Equals(a.expectedSha256, b.expectedSha256, StringComparison.Ordinal) ||
                !string.Equals(a.assetGuid, b.assetGuid, StringComparison.Ordinal) ||
                !string.Equals(a.scratchFileName, b.scratchFileName, StringComparison.Ordinal))
               return false;
         }
         return true;
      }

      private static JournalRecord ReadJournal(string path)
      {
         string json = ReadBoundedUtf8File(path, MaxJournalSizeBytes);
         JournalRecord journal;
         try
         {
            journal = JsonUtility.FromJson<JournalRecord>(json);
         }
         catch (Exception exception)
         {
            throw new InvalidDataException("Gameplay-tag transaction journal is not valid JSON.", exception);
         }
         ValidateJournal(journal);
         return journal;
      }

      private static void ValidateJournal(JournalRecord journal)
      {
         if (journal == null || journal.schemaVersion != JournalSchemaVersion)
            throw new InvalidDataException("Gameplay-tag transaction journal schema is invalid.");
         if (!Guid.TryParseExact(journal.transactionId, "N", out _))
            throw new InvalidDataException("Gameplay-tag transaction identifier is invalid.");
         if (journal.phase != "Prepared" && journal.phase != "Published" && journal.phase != "Cleaning")
            throw new InvalidDataException("Gameplay-tag transaction phase is invalid.");
         if (!DateTime.TryParse(
                journal.createdUtc,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out _))
            throw new InvalidDataException("Gameplay-tag transaction timestamp is invalid.");
         if (journal.effects == null || journal.effects.Length < 2 || journal.effects.Length > MaxEffectCount)
            throw new InvalidDataException("Gameplay-tag transaction effect count is invalid.");
         if (journal.appliedEffectCount < 0 || journal.appliedEffectCount > journal.effects.Length)
            throw new InvalidDataException("Gameplay-tag transaction progress is invalid.");

         HashSet<string> seen = new(StringComparer.Ordinal);
         int cursor = 0;
         foreach (string directoryPath in s_GeneratedDirectoryPaths)
         {
            if (cursor >= journal.effects.Length ||
                !string.Equals(journal.effects[cursor].relativePath, directoryPath, StringComparison.Ordinal))
               continue;
            ValidateDirectoryEffect(journal.effects[cursor], seen);
            cursor++;
            if (cursor >= journal.effects.Length)
               throw new InvalidDataException("Gameplay-tag directory effect has no metadata effect.");
            ValidateMetaEffect(journal.effects[cursor], directoryPath + ".meta", seen, isFolder: true);
            cursor++;
         }

         if (cursor >= journal.effects.Length)
            throw new InvalidDataException("Gameplay-tag payload effect is missing.");
         ValidatePayloadEffect(journal.effects[cursor], seen);
         cursor++;
         if (cursor >= journal.effects.Length)
            throw new InvalidDataException("Gameplay-tag payload metadata effect is missing.");
         ValidateMetaEffect(journal.effects[cursor], GeneratedAssetPath + ".meta", seen, isFolder: false);
         cursor++;
         if (cursor != journal.effects.Length)
            throw new InvalidDataException("Gameplay-tag transaction contains unsupported effects.");
      }

      private static void ValidateDirectoryEffect(EffectRecord effect, HashSet<string> seen)
      {
         ValidateCommonEffect(effect, seen);
         if (effect.kind != EffectKindDirectory ||
             !string.IsNullOrEmpty(effect.expectedSha256) ||
             effect.maxLengthBytes != 0 ||
             !string.IsNullOrEmpty(effect.assetGuid) ||
             !string.IsNullOrEmpty(effect.scratchFileName))
            throw new InvalidDataException("Gameplay-tag directory effect is invalid.");
      }

      private static void ValidatePayloadEffect(EffectRecord effect, HashSet<string> seen)
      {
         ValidateCommonEffect(effect, seen);
         if (effect.kind != EffectKindFile ||
             !string.Equals(effect.relativePath, GeneratedAssetPath, StringComparison.Ordinal) ||
             !IsSha256(effect.expectedSha256) ||
             effect.maxLengthBytes != BuildTagBinaryFormat.MaxDataSizeBytes ||
             !string.IsNullOrEmpty(effect.assetGuid) ||
             string.IsNullOrEmpty(effect.scratchFileName))
            throw new InvalidDataException("Gameplay-tag payload effect is invalid.");
      }

      private static void ValidateMetaEffect(
         EffectRecord effect,
         string expectedPath,
         HashSet<string> seen,
         bool isFolder)
      {
         ValidateCommonEffect(effect, seen);
         if (effect.kind != EffectKindFile ||
             !string.Equals(effect.relativePath, expectedPath, StringComparison.Ordinal) ||
             !Guid.TryParseExact(effect.assetGuid, "N", out _) ||
             effect.maxLengthBytes != MaxMetaFileSizeBytes ||
             string.IsNullOrEmpty(effect.scratchFileName))
            throw new InvalidDataException("Gameplay-tag metadata effect is invalid.");

         string expectedHash = ComputeSha256(CreateMetaFile(effect.assetGuid, isFolder));
         if (!string.Equals(expectedHash, effect.expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Gameplay-tag metadata effect hash is invalid.");
      }

      private static void ValidateCommonEffect(EffectRecord effect, HashSet<string> seen)
      {
         if (effect == null || string.IsNullOrEmpty(effect.relativePath) ||
             effect.relativePath.Length > MaxRelativePathLength ||
             effect.relativePath.IndexOf('\\') >= 0 ||
             !seen.Add(effect.relativePath) ||
             effect.beforeExists ||
             !string.IsNullOrEmpty(effect.beforeSha256))
            throw new InvalidDataException("Gameplay-tag transaction effect pre-state is invalid.");
      }

      private void ThrowIfPendingOrUnknownState()
      {
         foreach (string entry in Directory.EnumerateFileSystemEntries(m_StateRoot))
         {
            string name = Path.GetFileName(entry);
            if (string.Equals(name, LockFileName, StringComparison.Ordinal))
               continue;
            if (string.Equals(name, JournalFileName, StringComparison.Ordinal) ||
                string.Equals(name, JournalCandidateFileName, StringComparison.Ordinal) ||
                string.Equals(name, ScratchDirectoryName, StringComparison.Ordinal))
            {
               throw new BuildFailedException(
                  "A pending gameplay-tag build transaction requires explicit recovery. " +
                  "Run BuildTags.Recover(projectRoot) before building.");
            }
            throw new BuildFailedException(
               $"Unknown gameplay-tag transaction state '{name}' requires manual inspection.");
         }
      }

      private void ThrowIfUnknownStateWithoutJournal()
      {
         foreach (string entry in Directory.EnumerateFileSystemEntries(m_StateRoot))
         {
            string name = Path.GetFileName(entry);
            if (!string.Equals(name, LockFileName, StringComparison.Ordinal))
               throw new InvalidDataException($"Unknown gameplay-tag transaction state '{name}' was preserved.");
         }
      }

      private void ThrowIfUnknownStateEntries(JournalRecord journal)
      {
         foreach (string entry in Directory.EnumerateFileSystemEntries(m_StateRoot))
         {
            string name = Path.GetFileName(entry);
            if (string.Equals(name, LockFileName, StringComparison.Ordinal) ||
                string.Equals(name, JournalFileName, StringComparison.Ordinal) ||
                string.Equals(name, JournalCandidateFileName, StringComparison.Ordinal) ||
                string.Equals(name, ScratchDirectoryName, StringComparison.Ordinal))
               continue;
            throw new InvalidDataException($"Unknown gameplay-tag transaction state '{name}' was preserved.");
         }

         if (!File.Exists(m_JournalPath))
            throw new InvalidDataException("The active gameplay-tag transaction journal is missing.");
         if (File.Exists(m_JournalCandidatePath))
         {
            JournalRecord candidate = ReadJournal(m_JournalCandidatePath);
            if (!HaveSamePlan(journal, candidate))
            {
               throw new InvalidDataException(
                  "The gameplay-tag journal candidate describes an unknown transaction and was preserved.");
            }
         }
         ValidateKnownScratch(journal);
      }

      private void DeleteJournalFiles()
      {
         if (File.Exists(m_JournalCandidatePath))
         {
            JournalRecord candidate = ReadJournal(m_JournalCandidatePath);
            if (!HaveSamePlan(m_Journal, candidate))
               throw new InvalidDataException("Unknown gameplay-tag journal candidate was preserved.");
            File.Delete(m_JournalCandidatePath);
         }
         if (File.Exists(m_JournalPath))
            File.Delete(m_JournalPath);
         if (File.Exists(m_JournalCandidatePath) || File.Exists(m_JournalPath))
            throw new IOException("Failed to delete completed gameplay-tag transaction journal.");
      }

      private static byte[] CreateMetaFile(string guid, bool isFolder)
      {
         string content = isFolder
            ? "fileFormatVersion: 2\n" +
              $"guid: {guid}\n" +
              "folderAsset: yes\n" +
              "DefaultImporter:\n" +
              "  externalObjects: {}\n" +
              "  userData: \n" +
              "  assetBundleName: \n" +
              "  assetBundleVariant: \n"
            : "fileFormatVersion: 2\n" +
              $"guid: {guid}\n" +
              "TextScriptImporter:\n" +
              "  externalObjects: {}\n" +
              "  userData: \n" +
              "  assetBundleName: \n" +
              "  assetBundleVariant: \n";
         return s_StrictUtf8.GetBytes(content);
      }

      private static string ReadMetaGuid(string metaPath)
      {
         foreach (string line in File.ReadLines(metaPath, s_StrictUtf8))
         {
            if (line.StartsWith("guid: ", StringComparison.Ordinal))
               return line.Substring("guid: ".Length).Trim();
         }
         return string.Empty;
      }

      private static string NormalizeProjectRoot(string projectRoot)
      {
         if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Project root must not be empty.", nameof(projectRoot));
         string normalized = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
         if (!Directory.Exists(normalized) || !Directory.Exists(Path.Combine(normalized, "Assets")))
            throw new DirectoryNotFoundException($"Unity project root is invalid: '{normalized}'.");
         RejectReparsePointAncestors(normalized, normalized);
         return normalized;
      }

      private static string ResolveProjectRelativePath(string projectRoot, string relativePath)
      {
         if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > MaxRelativePathLength ||
             Path.IsPathRooted(relativePath) || relativePath.IndexOf('\\') >= 0)
            throw new InvalidDataException("Gameplay-tag transaction contains an invalid relative path.");
         string[] segments = relativePath.Split('/');
         if (segments.Any(static segment =>
                string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
            throw new InvalidDataException("Gameplay-tag transaction path traversal was rejected.");

         string combined = projectRoot;
         foreach (string segment in segments)
            combined = Path.Combine(combined, segment);
         string resolved = Path.GetFullPath(combined);
         string prefix = projectRoot + Path.DirectorySeparatorChar;
         if (!resolved.StartsWith(prefix, s_PathComparison))
            throw new InvalidDataException("Gameplay-tag transaction path escapes the Unity project root.");
         RejectReparsePointAncestors(projectRoot, Path.GetDirectoryName(resolved));
         return resolved;
      }

      private static void EnsureDirectoryPathCanBeCreated(string projectRoot, string path)
      {
         string current = path;
         while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
            current = Path.GetDirectoryName(current);
         if (string.IsNullOrEmpty(current) ||
             (!string.Equals(current, projectRoot, s_PathComparison) &&
              !current.StartsWith(projectRoot + Path.DirectorySeparatorChar, s_PathComparison)))
            throw new InvalidDataException("Gameplay-tag state directory escapes the project root.");
         RejectReparsePointAncestors(projectRoot, current);
      }

      private static void RejectReparsePointAncestors(string projectRoot, string path)
      {
         string current = Path.GetFullPath(path);
         while (true)
         {
            RejectReparsePointIfPresent(current);
            if (string.Equals(current, projectRoot, s_PathComparison))
               return;
            string parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, s_PathComparison))
               throw new InvalidDataException("Gameplay-tag path is outside the project root.");
            current = parent;
         }
      }

      private static void RejectReparsePoint(string path)
      {
         if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
         if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse points are not allowed in gameplay-tag build paths: '{path}'.");
      }

      private static void RejectReparsePointIfPresent(string path)
      {
         if ((File.Exists(path) || Directory.Exists(path)) &&
             (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse points are not allowed in gameplay-tag build paths: '{path}'.");
      }

      private static void ValidateRegularFile(string path, int maxLength)
      {
         RejectReparsePointIfPresent(path);
         using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
         if (stream.Length <= 0 || stream.Length > maxLength)
         {
            throw new InvalidDataException(
               $"File '{path}' is outside the allowed {maxLength}-byte recovery budget.");
         }
      }

      private static void WriteAllBytesDurably(string path, byte[] data)
      {
         using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
         stream.Write(data, 0, data.Length);
         stream.Flush(flushToDisk: true);
      }

      internal static string ComputeSha256File(string path, long maxLength)
      {
         RejectReparsePointIfPresent(path);
         using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
         long length = stream.Length;
         if (length <= 0 || length > maxLength)
            throw new InvalidDataException($"File '{path}' is outside the allowed {maxLength}-byte recovery budget.");

         using ExactLengthReadStream boundedStream = new(stream, length, leaveOpen: true);
         using SHA256 sha256 = SHA256.Create();
         return ToUpperHex(sha256.ComputeHash(boundedStream));
      }

      internal static string ReadBoundedUtf8File(string path, int maxLength)
      {
         RejectReparsePointIfPresent(path);
         using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
         long length = stream.Length;
         if (length <= 0 || length > maxLength)
            throw new InvalidDataException($"File '{path}' is outside the allowed {maxLength}-byte recovery budget.");
         Utf8FileSafety.RejectByteOrderMark(stream, path);

         using ExactLengthReadStream boundedStream = new(stream, length, leaveOpen: true);
         using StreamReader reader = new(
            boundedStream,
            s_StrictUtf8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: false);
         return reader.ReadToEnd();
      }

      private static string ComputeSha256(byte[] data)
      {
         using SHA256 sha256 = SHA256.Create();
         return ToUpperHex(sha256.ComputeHash(data));
      }

      private static string ToUpperHex(byte[] bytes)
      {
         return BitConverter.ToString(bytes).Replace("-", string.Empty);
      }

      private static bool IsSha256(string value)
      {
         if (string.IsNullOrEmpty(value) || value.Length != 64)
            return false;
         for (int i = 0; i < value.Length; i++)
         {
            char character = value[i];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'A' && character <= 'F')))
               return false;
         }
         return true;
      }

      private static BuildFailedException ToBuildFailure(Exception exception)
      {
         if (exception is BuildFailedException buildFailure)
            return buildFailure;
         return new BuildFailedException($"Gameplay-tag build asset transaction failed: {exception.Message}");
      }
   }
}
