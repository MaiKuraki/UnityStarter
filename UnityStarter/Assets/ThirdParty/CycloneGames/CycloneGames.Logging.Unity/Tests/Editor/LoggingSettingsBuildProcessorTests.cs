using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CycloneGames.Logging.Pipeline;
using CycloneGames.Logging.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Tests.Editor
{
    public sealed class LoggingSettingsBuildProcessorTests
    {
        private readonly List<LoggingSettings> _settings = new List<LoggingSettings>();
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests = null;
            LoggingSettingsBuildOverrideTransaction.RecoveryCheckpointForTests = null;
            LoggingSettingsBuildRecovery.Recover(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests = null;
            LoggingSettingsBuildOverrideTransaction.RecoveryCheckpointForTests = null;
            LoggingSettingsBuildRecovery.Recover(_projectRoot);

            for (int i = 0; i < _settings.Count; i++)
            {
                if (_settings[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_settings[i]);
                }
            }

            _settings.Clear();
        }

        [Test]
        public void ProvenanceValidation_AcceptsMatchingIdentityAndPayload()
        {
            LoggingSettings settings = CreateSettings();
            string transactionId = System.Guid.NewGuid().ToString("N");
            string projectToken = System.Guid.NewGuid().ToString("N");
            string payloadHash = LoggingSettingsBuildOverrideTransaction.ComputePayloadHashForTests(settings);
            settings.SetBuildOverrideProvenance(transactionId, projectToken, payloadHash);

            bool valid = LoggingSettingsBuildOverrideTransaction.ValidateProvenanceForTests(
                settings,
                transactionId,
                projectToken,
                payloadHash,
                out string error);

            Assert.IsTrue(valid, error);
        }

        [Test]
        public void ProvenanceValidation_RejectsTransactionMismatch()
        {
            LoggingSettings settings = CreateSettings();
            string transactionId = System.Guid.NewGuid().ToString("N");
            string projectToken = System.Guid.NewGuid().ToString("N");
            string payloadHash = LoggingSettingsBuildOverrideTransaction.ComputePayloadHashForTests(settings);
            settings.SetBuildOverrideProvenance(transactionId, projectToken, payloadHash);

            bool valid = LoggingSettingsBuildOverrideTransaction.ValidateProvenanceForTests(
                settings,
                System.Guid.NewGuid().ToString("N"),
                projectToken,
                payloadHash,
                out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("identity", error);
        }

        [Test]
        public void ProvenanceValidation_RejectsPayloadMutation()
        {
            LoggingSettings settings = CreateSettings();
            string transactionId = System.Guid.NewGuid().ToString("N");
            string projectToken = System.Guid.NewGuid().ToString("N");
            string payloadHash = LoggingSettingsBuildOverrideTransaction.ComputePayloadHashForTests(settings);
            settings.SetBuildOverrideProvenance(transactionId, projectToken, payloadHash);
            settings.minimumSeverity = LogSeverity.Error;

            bool valid = LoggingSettingsBuildOverrideTransaction.ValidateProvenanceForTests(
                settings,
                transactionId,
                projectToken,
                payloadHash,
                out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("payload hash", error);
        }

        [Test]
        public void CompletedTransaction_RemovesGeneratedAssetAndRecoveryState()
        {
            LoggingSettings settings = CreateSettings();
            using (LoggingSettingsBuildOverrideTransaction transaction =
                   LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, settings))
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<LoggingSettings>(
                        LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath),
                    Is.Not.Null);
                transaction.Complete();
            }

            Assert.That(
                AssetDatabase.LoadAssetAtPath<LoggingSettings>(
                    LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath),
                Is.Null);
            Assert.That(Directory.Exists(GetTransactionDirectory()), Is.False);
        }

        [Test]
        public void PendingTransaction_BlocksNormalBuildUntilExplicitRecovery()
        {
            LoggingSettings settings = CreateSettings();
            LoggingSettingsBuildOverrideTransaction transaction =
                LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, settings);
            transaction.Dispose();

            Assert.Throws<System.InvalidOperationException>(
                () => LoggingSettingsBuildOverrideTransaction.ThrowIfPendingEvidence(_projectRoot));
            Assert.Throws<BuildFailedException>(() => new LoggingSettingsBuildProcessor().OnPreprocessBuild(null));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<LoggingSettings>(
                    LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath),
                Is.Not.Null,
                "Normal preprocessing must not recover or delete pending state implicitly.");

            LoggingSettingsBuildRecovery.Recover(_projectRoot);
            Assert.DoesNotThrow(
                () => LoggingSettingsBuildOverrideTransaction.ThrowIfPendingEvidence(_projectRoot));
        }

        [Test]
        public void CleanupRefusesModifiedAssetAndRetainsRecoverableEvidence()
        {
            LoggingSettings settings = CreateSettings();
            LoggingSettingsBuildOverrideTransaction transaction =
                LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, settings);
            string assetAbsolutePath = Path.Combine(
                _projectRoot,
                LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath
                    .Replace('/', Path.DirectorySeparatorChar));
            byte[] originalAssetBytes = File.ReadAllBytes(assetAbsolutePath);

            LoggingSettings generated = AssetDatabase.LoadAssetAtPath<LoggingSettings>(
                LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath);
            generated.minimumSeverity = LogSeverity.Fatal;
            EditorUtility.SetDirty(generated);
            AssetDatabase.SaveAssetIfDirty(generated);
            AssetDatabase.ImportAsset(
                LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Assert.Throws<System.InvalidOperationException>(() => transaction.Complete());
            transaction.Dispose();
            Assert.That(File.Exists(Path.Combine(GetTransactionDirectory(), "journal.json")), Is.True);
            Assert.That(File.Exists(assetAbsolutePath), Is.True);

            File.WriteAllBytes(assetAbsolutePath, originalAssetBytes);
            AssetDatabase.ImportAsset(
                LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.DoesNotThrow(() => LoggingSettingsBuildRecovery.Recover(_projectRoot));
        }

        [Test]
        public void Recovery_UsesValidBackupWhenMainJournalIsInterrupted()
        {
            LoggingSettings settings = CreateSettings();
            LoggingSettingsBuildOverrideTransaction transaction =
                LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, settings);
            transaction.Dispose();

            string stateDirectory = GetTransactionDirectory();
            string journalPath = Path.Combine(stateDirectory, "journal.json");
            string backupPath = Path.Combine(stateDirectory, "journal.json.bak");
            File.Copy(journalPath, backupPath);
            File.WriteAllText(journalPath, "{interrupted", new UTF8Encoding(false));

            Assert.DoesNotThrow(() => LoggingSettingsBuildRecovery.Recover(_projectRoot));
            Assert.That(Directory.Exists(stateDirectory), Is.False);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<LoggingSettings>(
                    LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath),
                Is.Null);
        }

        [Test]
        public void Recovery_ClosesPreparedCreateAssetCrashWindowUsingEmbeddedProvenance()
        {
            LoggingSettings settings = CreateSettings();
            LoggingSettingsBuildOverrideTransaction transaction =
                LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, settings);
            transaction.Dispose();

            string journalPath = Path.Combine(GetTransactionDirectory(), "journal.json");
            string journal = File.ReadAllText(journalPath);
            journal = journal.Replace("\"phase\":\"Active\"", "\"phase\":\"Prepared\"");
            journal = new Regex("\"assetGuid\":\"[0-9a-fA-F]{32}\"")
                .Replace(journal, "\"assetGuid\":\"\"", 1);
            journal = Regex.Replace(journal, "\"assetSha256\":\"[0-9a-fA-F]{64}\"", "\"assetSha256\":\"\"");
            journal = Regex.Replace(journal, "\"assetBytes\":[0-9]+", "\"assetBytes\":0");
            File.WriteAllText(journalPath, journal, new UTF8Encoding(false));

            Assert.DoesNotThrow(() => LoggingSettingsBuildRecovery.Recover(_projectRoot));
            Assert.That(Directory.Exists(GetTransactionDirectory()), Is.False);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<LoggingSettings>(
                    LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath),
                Is.Null);
        }

        [TestCase((int)LoggingSettingsBuildFolderCheckpoint.IntentPersisted)]
        [TestCase((int)LoggingSettingsBuildFolderCheckpoint.FolderCreated)]
        [TestCase((int)LoggingSettingsBuildFolderCheckpoint.AppliedPersisted)]
        [TestCase((int)LoggingSettingsBuildFolderCheckpoint.GuidResolved)]
        [TestCase((int)LoggingSettingsBuildFolderCheckpoint.FolderMoved)]
        [TestCase((int)LoggingSettingsBuildFolderCheckpoint.GuidPersisted)]
        public void FolderCreationInterruption_ExplicitRecoveryReconcilesEveryDurableStage(
            int interruptionPointValue)
        {
            var interruptionPoint = (LoggingSettingsBuildFolderCheckpoint)interruptionPointValue;
            LoggingSettings settings = CreateSettings();
            string finalFolderAssetPath = null;
            string stagingFolderAssetPath = null;
            LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests =
                (finalAssetPath, stagingAssetPath, checkpoint) =>
                {
                    if (finalFolderAssetPath != null || checkpoint != interruptionPoint)
                    {
                        return;
                    }

                    finalFolderAssetPath = finalAssetPath;
                    stagingFolderAssetPath = stagingAssetPath;
                    throw new InvalidOperationException("Simulated process interruption during folder creation.");
                };

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, settings));
            }
            finally
            {
                LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests = null;
            }

            Assert.That(finalFolderAssetPath, Is.Not.Null);
            Assert.That(stagingFolderAssetPath, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(GetTransactionDirectory(), "journal.json")), Is.True);

            string finalFolderAbsolutePath = Path.Combine(
                _projectRoot,
                finalFolderAssetPath.Replace('/', Path.DirectorySeparatorChar));
            string stagingFolderAbsolutePath = Path.Combine(
                _projectRoot,
                stagingFolderAssetPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.DoesNotThrow(() => LoggingSettingsBuildRecovery.Recover(_projectRoot));
            Assert.That(Directory.Exists(GetTransactionDirectory()), Is.False);
            Assert.That(Directory.Exists(finalFolderAbsolutePath), Is.False);
            Assert.That(File.Exists(finalFolderAbsolutePath + ".meta"), Is.False);
            Assert.That(Directory.Exists(stagingFolderAbsolutePath), Is.False);
            Assert.That(File.Exists(stagingFolderAbsolutePath + ".meta"), Is.False);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<LoggingSettings>(
                    LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath),
                Is.Null);
        }

        [Test]
        public void IntentOnlyRecovery_RefusesToAdoptAnUnrelatedFinalPathFolder()
        {
            string unrelatedFolderAssetPath = null;
            LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests =
                (finalAssetPath, stagingAssetPath, checkpoint) =>
                {
                    if (checkpoint == LoggingSettingsBuildFolderCheckpoint.IntentPersisted)
                    {
                        unrelatedFolderAssetPath = finalAssetPath;
                        throw new InvalidOperationException("Simulated interruption before CreateFolder.");
                    }
                };

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, CreateSettings()));
            }
            finally
            {
                LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests = null;
            }

            Assert.That(unrelatedFolderAssetPath, Is.Not.Null);
            Assert.That(AssetDatabase.IsValidFolder(unrelatedFolderAssetPath), Is.False);
            int separatorIndex = unrelatedFolderAssetPath.LastIndexOf('/');
            string unrelatedParentAssetPath = unrelatedFolderAssetPath.Substring(0, separatorIndex);
            string unrelatedFolderName = unrelatedFolderAssetPath.Substring(separatorIndex + 1);
            string unrelatedGuid = AssetDatabase.CreateFolder(
                unrelatedParentAssetPath,
                unrelatedFolderName);
            Assert.That(unrelatedGuid, Has.Length.EqualTo(32));

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    LoggingSettingsBuildRecovery.Recover(_projectRoot));
                Assert.That(AssetDatabase.IsValidFolder(unrelatedFolderAssetPath), Is.True);
                Assert.That(Directory.Exists(GetTransactionDirectory()), Is.True);
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(unrelatedFolderAssetPath))
                {
                    Assert.That(AssetDatabase.DeleteAsset(unrelatedFolderAssetPath), Is.True);
                }

                LoggingSettingsBuildRecovery.Recover(_projectRoot);
            }
        }

        [TestCase((int)LoggingSettingsBuildRecoveryCheckpoint.RecoveryAnchorPersisted)]
        [TestCase((int)LoggingSettingsBuildRecoveryCheckpoint.OriginalCandidatesPruned)]
        public void RecoveryNormalizationInterruption_PreservesADurableFolderOwnershipCandidate(
            int interruptionPointValue)
        {
            string finalFolderAssetPath = null;
            string stagingFolderAssetPath = null;
            LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests =
                (finalAssetPath, stagingAssetPath, checkpoint) =>
                {
                    if (finalFolderAssetPath != null ||
                        checkpoint != LoggingSettingsBuildFolderCheckpoint.FolderCreated)
                    {
                        return;
                    }

                    finalFolderAssetPath = finalAssetPath;
                    stagingFolderAssetPath = stagingAssetPath;
                    throw new InvalidOperationException("Simulated process interruption after CreateFolder.");
                };

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, CreateSettings()));
            }
            finally
            {
                LoggingSettingsBuildOverrideTransaction.FolderCheckpointForTests = null;
            }

            var interruptionPoint =
                (LoggingSettingsBuildRecoveryCheckpoint)interruptionPointValue;
            LoggingSettingsBuildOverrideTransaction.RecoveryCheckpointForTests = checkpoint =>
            {
                if (checkpoint == interruptionPoint)
                {
                    throw new InvalidOperationException("Simulated process interruption during recovery normalization.");
                }
            };

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    LoggingSettingsBuildRecovery.Recover(_projectRoot));
            }
            finally
            {
                LoggingSettingsBuildOverrideTransaction.RecoveryCheckpointForTests = null;
            }

            string finalFolderAbsolutePath = Path.Combine(
                _projectRoot,
                finalFolderAssetPath.Replace('/', Path.DirectorySeparatorChar));
            string stagingFolderAbsolutePath = Path.Combine(
                _projectRoot,
                stagingFolderAssetPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(Directory.Exists(GetTransactionDirectory()), Is.True);
            Assert.That(Directory.Exists(stagingFolderAbsolutePath), Is.True);

            Assert.DoesNotThrow(() => LoggingSettingsBuildRecovery.Recover(_projectRoot));
            Assert.That(Directory.Exists(GetTransactionDirectory()), Is.False);
            Assert.That(Directory.Exists(finalFolderAbsolutePath), Is.False);
            Assert.That(File.Exists(finalFolderAbsolutePath + ".meta"), Is.False);
            Assert.That(Directory.Exists(stagingFolderAbsolutePath), Is.False);
            Assert.That(File.Exists(stagingFolderAbsolutePath + ".meta"), Is.False);
        }

        [Test]
        public void Journal_DoesNotPersistAbsoluteProjectPath()
        {
            LoggingSettings settings = CreateSettings();
            LoggingSettingsBuildOverrideTransaction transaction =
                LoggingSettingsBuildOverrideTransaction.Begin(_projectRoot, settings);
            transaction.Dispose();

            string journal = File.ReadAllText(Path.Combine(GetTransactionDirectory(), "journal.json"));

            StringAssert.DoesNotContain(_projectRoot.Replace('\\', '/'), journal.Replace('\\', '/'));
            StringAssert.Contains("projectToken", journal);
        }

        [Test]
        public void JournalRead_RejectsOversizedInputBeforeJsonParsing()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.Logging.BuildJournalTests",
                Guid.NewGuid().ToString("N"));
            string journalPath = Path.Combine(directory, "oversized.journal.json");
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllBytes(
                    journalPath,
                    new byte[LoggingSettingsBuildOverrideTransaction.MaximumJournalFileBytes + 1]);

                InvalidDataException exception = Assert.Throws<InvalidDataException>(
                    () => LoggingSettingsBuildOverrideTransaction.ReadBoundedJournalForTests(journalPath));
                StringAssert.Contains("byte limit", exception.Message);
                Assert.That(File.Exists(journalPath), Is.True, "Rejected journals must remain available for diagnosis.");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void ExplicitUndefinedEnvironmentEnum_FailsInsteadOfBeingIgnored()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_MINIMUM_SEVERITY"] = "255"
            };
            LoggingSettings settings = CreateSettings();

            Assert.Throws<BuildFailedException>(() =>
                LoggingSettingsBuildProcessor.ApplyOptionsForTests(settings, key => ReadEnvironment(environment, key), Array.Empty<string>()));
        }

        [Test]
        public void ExplicitInvalidEnvironmentInteger_FailsInsteadOfBeingIgnored()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_MAX_QUEUED_MESSAGES"] = "0"
            };
            LoggingSettings settings = CreateSettings();

            Assert.Throws<BuildFailedException>(() =>
                LoggingSettingsBuildProcessor.ApplyOptionsForTests(settings, key => ReadEnvironment(environment, key), Array.Empty<string>()));
        }

        [Test]
        public void CommandLineValue_OverridesEnvironmentValue()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_MINIMUM_SEVERITY"] = "Warning"
            };
            LoggingSettings settings = CreateSettings();

            bool hasOverrides = LoggingSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                new[] { "-loggingMinimumSeverity", "Error" });

            Assert.IsTrue(hasOverrides);
            Assert.AreEqual(LogSeverity.Error, settings.minimumSeverity);
        }

        [Test]
        public void CriticalSeverityEnvironmentOverride_IsApplied()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_CRITICAL_SEVERITY"] = "Fatal"
            };
            LoggingSettings settings = CreateSettings();
            settings.criticalSeverity = LogSeverity.Warning;

            bool hasOverrides = LoggingSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                Array.Empty<string>());

            Assert.IsTrue(hasOverrides);
            Assert.AreEqual(LogSeverity.Fatal, settings.criticalSeverity);
        }

        [Test]
        public void CriticalSeverityCommandLineOverride_WinsOverEnvironment()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_CRITICAL_SEVERITY"] = "Warning"
            };
            LoggingSettings settings = CreateSettings();

            LoggingSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                new[] { "-loggingCriticalSeverity", "Error" });

            Assert.AreEqual(LogSeverity.Error, settings.criticalSeverity);
        }

        [Test]
        public void NoOverrides_StillValidateSettings()
        {
            LoggingSettings settings = CreateSettings();
            settings.maxQueuedMessages = 0;

            Assert.Throws<BuildFailedException>(() =>
                LoggingSettingsBuildProcessor.ApplyOptionsForTests(
                    settings,
                    _ => null,
                    Array.Empty<string>()));
        }

        [Test]
        public void UnityConsoleBlockPolicy_FailsBuildValidation()
        {
            LoggingSettings settings = CreateSettings();
            settings.unityConsoleOverflowPolicy = LogQueueOverflowPolicy.Block;

            Assert.Throws<BuildFailedException>(() => LoggingSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void CriticalSeverityNone_FailsBuildValidation()
        {
            LoggingSettings settings = CreateSettings();
            settings.criticalSeverity = LogSeverity.None;

            Assert.Throws<BuildFailedException>(() => LoggingSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void ConsoleEnvironmentOverride_IsApplied()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_CONSOLE"] = "true"
            };
            LoggingSettings settings = CreateSettings();
            settings.registerConsoleLogSink = false;

            bool hasOverrides = LoggingSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                Array.Empty<string>());

            Assert.IsTrue(hasOverrides);
            Assert.IsTrue(settings.registerConsoleLogSink);
        }

        [Test]
        public void ConsoleCommandLineOverride_WinsOverEnvironment()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_CONSOLE"] = "true"
            };
            LoggingSettings settings = CreateSettings();

            LoggingSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                new[] { "-loggingConsole", "false" });

            Assert.IsFalse(settings.registerConsoleLogSink);
        }

        [TestCase("Off", false, false)]
        [TestCase("Unity", true, false)]
        [TestCase("File", false, true)]
        [TestCase("UnityAndFile", true, true)]
        public void BuildMode_ExplicitlyDisablesConsoleSink(string mode, bool expectedUnity, bool expectedFile)
        {
            LoggingSettings settings = CreateSettings();
            settings.registerUnityConsoleLogSink = true;
            settings.registerConsoleLogSink = true;
            settings.registerFileLogSink = true;

            LoggingSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                _ => null,
                new[] { "-loggingMode", mode });

            Assert.AreEqual(expectedUnity, settings.registerUnityConsoleLogSink);
            Assert.IsFalse(settings.registerConsoleLogSink);
            Assert.AreEqual(expectedFile, settings.registerFileLogSink);
        }

        [Test]
        public void EmptyCustomPathOverride_ClearsInactiveCustomPath()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGING_CUSTOM_FILE_PATH"] = string.Empty
            };
            LoggingSettings settings = CreateSettings();
            settings.customFilePath = "old.log";
            settings.usePersistentDataPath = true;

            LoggingSettingsBuildProcessor.ApplyOptionsForTests(settings, key => ReadEnvironment(environment, key), Array.Empty<string>());

            Assert.AreEqual(string.Empty, settings.customFilePath);
        }

        [Test]
        public void PortableFileNameValidation_RejectsDirectoryTraversal()
        {
            LoggingSettings settings = CreateSettings();
            settings.fileName = "../outside.log";

            Assert.Throws<BuildFailedException>(() => LoggingSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void PortableFileNameValidation_RejectsWindowsReservedName()
        {
            LoggingSettings settings = CreateSettings();
            settings.fileName = "CON.log";

            Assert.Throws<BuildFailedException>(() => LoggingSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void CustomFilePathValidation_RequiresPathWhenActive()
        {
            LoggingSettings settings = CreateSettings();
            settings.registerFileLogSink = true;
            settings.usePersistentDataPath = false;
            settings.customFilePath = string.Empty;

            Assert.Throws<BuildFailedException>(() => LoggingSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void CustomFilePathValidation_RejectsRelativePath()
        {
            LoggingSettings settings = CreateSettings();
            settings.registerFileLogSink = true;
            settings.usePersistentDataPath = false;
            settings.allowCustomFilePath = true;
            settings.customFilePath = Path.Combine("logs", "game.log");

            Assert.Throws<BuildFailedException>(() => LoggingSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void CustomFilePathValidation_AcceptsRootedAbsolutePath()
        {
            LoggingSettings settings = CreateSettings();
            settings.registerFileLogSink = true;
            settings.usePersistentDataPath = false;
            settings.allowCustomFilePath = true;
            settings.customFilePath = Path.Combine(Path.GetTempPath(), "CycloneGames.Logging", "game.log");

            Assert.DoesNotThrow(() => LoggingSettingsBuildProcessor.ValidateSettings(settings));
        }

        private LoggingSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<LoggingSettings>();
            _settings.Add(settings);
            return settings;
        }

        private static string ReadEnvironment(Dictionary<string, string> environment, string key)
        {
            return environment.TryGetValue(key, out string value) ? value : null;
        }

        private string GetTransactionDirectory()
        {
            return Path.Combine(
                _projectRoot,
                LoggingSettingsBuildOverrideTransaction.StateDirectoryRelativePath
                    .Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
