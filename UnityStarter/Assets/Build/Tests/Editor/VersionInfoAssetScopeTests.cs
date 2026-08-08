using System;
using System.IO;
using System.Runtime.ExceptionServices;
using Build.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor.Tests
{
    public sealed class VersionInfoAssetScopeTests
    {
        private string projectRoot;
        private string testRootAssetPath;
        private string testRootAbsolutePath;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            testRootAssetPath =
                "Assets/Build/Tests/Editor/__VersionInfoAssetScopeTests_" +
                Guid.NewGuid().ToString("N");
            testRootAbsolutePath = Path.Combine(
                projectRoot,
                testRootAssetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.IsValidFolder(testRootAssetPath))
            {
                Assert.That(
                    AssetDatabase.DeleteAsset(testRootAssetPath),
                    Is.True,
                    "Failed to remove the fixture-owned VersionInfoAssetScope test folder.");
            }

            Assert.That(Directory.Exists(testRootAbsolutePath), Is.False);
            Assert.That(File.Exists(testRootAbsolutePath + ".meta"), Is.False);
        }

        [Test]
        public void Dispose_WhenTargetAndFolderChainWereAbsent_RemovesEveryOwnedAssetAndFolder()
        {
            string nestedAssetPath = testRootAssetPath + "/Nested";
            string resourcesAssetPath = nestedAssetPath + "/Resources";
            string versionInfoAssetPath = resourcesAssetPath + "/VersionInfoData.asset";

            RunInLiveGlobalStateTransaction(() =>
            {
                VersionInfoAssetScope scope = VersionInfoAssetScope.Create(
                    versionInfoAssetPath,
                    CreateVersionContext("transient-success"));
                try
                {
                    Assert.That(File.Exists(ToAbsolutePath(versionInfoAssetPath)), Is.True);
                    Assert.That(File.Exists(ToAbsolutePath(versionInfoAssetPath) + ".meta"), Is.True);
                    Assert.That(Directory.Exists(testRootAbsolutePath), Is.True);
                    Assert.That(File.Exists(testRootAbsolutePath + ".meta"), Is.True);
                    Assert.That(Directory.Exists(ToAbsolutePath(nestedAssetPath)), Is.True);
                    Assert.That(File.Exists(ToAbsolutePath(nestedAssetPath) + ".meta"), Is.True);
                    Assert.That(Directory.Exists(ToAbsolutePath(resourcesAssetPath)), Is.True);
                    Assert.That(File.Exists(ToAbsolutePath(resourcesAssetPath) + ".meta"), Is.True);
                    Assert.That(
                        AssetDatabase.LoadAssetAtPath<VersionInfoData>(versionInfoAssetPath),
                        Is.Not.Null);
                }
                finally
                {
                    scope.Dispose();
                }

                AssertOwnedGeneratedPathsAreAbsent(
                    nestedAssetPath,
                    resourcesAssetPath,
                    versionInfoAssetPath);
            });

            AssertOwnedGeneratedPathsAreAbsent(
                nestedAssetPath,
                resourcesAssetPath,
                versionInfoAssetPath);
        }

        [Test]
        public void Dispose_WhenBuildBodyThrows_RestoresExistingAssetMetaAndDirectoriesExactly()
        {
            string resourcesAssetPath = testRootAssetPath + "/Resources";
            string versionInfoAssetPath = resourcesAssetPath + "/VersionInfoData.asset";
            CreateFolder("Assets/Build/Tests/Editor", Path.GetFileName(testRootAssetPath));
            CreateFolder(testRootAssetPath, "Resources");

            var originalAsset = ScriptableObject.CreateInstance<VersionInfoData>();
            originalAsset.commitHash = "original-commit";
            originalAsset.commitCount = "17";
            originalAsset.commitBranch = "original-branch";
            originalAsset.commitDate = "2026-01-02T03:04:05Z";
            originalAsset.buildDate = "2026-01-02T03:04:06Z";
            AssetDatabase.CreateAsset(originalAsset, versionInfoAssetPath);
            AssetDatabase.SaveAssetIfDirty(originalAsset);
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            EditorUtility.ClearDirty(originalAsset);

            FileSnapshot originalAssetSnapshot = CaptureSnapshot(versionInfoAssetPath);
            FileSnapshot originalAssetMetaSnapshot = CaptureSnapshot(versionInfoAssetPath + ".meta");
            FileSnapshot originalRootMetaSnapshot = CaptureSnapshot(testRootAssetPath + ".meta");
            FileSnapshot originalResourcesMetaSnapshot = CaptureSnapshot(resourcesAssetPath + ".meta");

            RunInLiveGlobalStateTransaction(() =>
            {
                InvalidOperationException bodyFailure = Assert.Throws<InvalidOperationException>(() =>
                {
                    using (VersionInfoAssetScope.Create(
                               versionInfoAssetPath,
                               CreateVersionContext("transient-failure")))
                    {
                        VersionInfoData installed =
                            AssetDatabase.LoadAssetAtPath<VersionInfoData>(versionInfoAssetPath);
                        Assert.That(installed, Is.Not.Null);
                        Assert.That(installed.commitHash, Is.EqualTo("transient-failure"));
                        throw new InvalidOperationException("Simulated build body failure.");
                    }
                });
                Assert.That(bodyFailure.Message, Is.EqualTo("Simulated build body failure."));

                AssertSnapshot(versionInfoAssetPath, originalAssetSnapshot);
                AssertSnapshot(versionInfoAssetPath + ".meta", originalAssetMetaSnapshot);
                AssertSnapshot(testRootAssetPath + ".meta", originalRootMetaSnapshot);
                AssertSnapshot(resourcesAssetPath + ".meta", originalResourcesMetaSnapshot);
                Assert.That(Directory.Exists(testRootAbsolutePath), Is.True);
                Assert.That(Directory.Exists(ToAbsolutePath(resourcesAssetPath)), Is.True);

                VersionInfoData restored =
                    AssetDatabase.LoadAssetAtPath<VersionInfoData>(versionInfoAssetPath);
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.commitHash, Is.EqualTo("original-commit"));
                Assert.That(EditorUtility.IsDirty(restored), Is.False);
            });
        }

        private void RunInLiveGlobalStateTransaction(Action body)
        {
            GlobalBuildStateTransaction transaction = BeginLiveGlobalStateTransaction();
            Exception bodyFailure = null;
            Exception cleanupFailure = null;
            try
            {
                body();
            }
            catch (Exception exception)
            {
                bodyFailure = exception;
            }

            try
            {
                transaction.RestoreVersionInfoFiles();
                transaction.ConfirmVersionInfoRestored();
                transaction.RestoreGlobalSettingsFiles();
                transaction.Complete();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            Exception releaseFailure = transaction.Release();
            if (bodyFailure != null && cleanupFailure != null)
            {
                throw new AggregateException(
                    "The test body and live global-state cleanup both failed.",
                    bodyFailure,
                    cleanupFailure);
            }

            if (bodyFailure != null && releaseFailure != null)
            {
                throw new AggregateException(
                    "The test body and global-state lock release both failed.",
                    bodyFailure,
                    releaseFailure);
            }

            if (cleanupFailure != null && releaseFailure != null)
            {
                throw new AggregateException(
                    "Live global-state cleanup and lock release both failed.",
                    cleanupFailure,
                    releaseFailure);
            }

            if (bodyFailure != null)
            {
                ExceptionDispatchInfo.Capture(bodyFailure).Throw();
            }

            if (cleanupFailure != null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }

            if (releaseFailure != null)
            {
                ExceptionDispatchInfo.Capture(releaseFailure).Throw();
            }
        }

        private GlobalBuildStateTransaction BeginLiveGlobalStateTransaction()
        {
            GlobalBuildStateTransaction transaction =
                GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
                transaction.Begin(
                    "ProjectSettings/ProjectSettings.asset",
                    (int)activeTarget,
                    (int)activeTarget,
                    CreateOwnedPlayerSettingsState());
                transaction.BeginGlobalMutation();
                transaction.MarkEditorBuildSettingsApplied();
                GlobalBuildStateTransaction.PlayerSettingsPersistenceToken token =
                    transaction.CapturePlayerSettingsPersistenceToken();
                transaction.MarkGlobalMutationApplied(
                    token,
                    CreateOwnedPlayerSettingsState());
                return transaction;
            }
            catch (Exception operationException)
            {
                Exception releaseException = transaction.Release();
                if (releaseException != null)
                {
                    throw new AggregateException(
                        "Failed to begin the live global-state test transaction and release its lock.",
                        operationException,
                        releaseException);
                }

                throw;
            }
        }

        private static PlayerSettingsOwnedState CreateOwnedPlayerSettingsState()
        {
            EditorBuildSettingsScene[] scenes =
                EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            var sceneStates = new EditorBuildSceneState[scenes.Length];
            for (int index = 0; index < scenes.Length; index++)
            {
                sceneStates[index] = new EditorBuildSceneState(
                    scenes[index]?.path,
                    scenes[index] != null && scenes[index].enabled);
            }

            return new PlayerSettingsOwnedState(
                (int)ScriptingImplementation.Mono2x,
                "VersionInfoAssetScopeTests",
                "VersionInfoAssetScopeTests",
                "1.0.0",
                "com.example.versioninfoscope.tests",
                1,
                "1",
                EditorUserBuildSettings.exportAsGoogleAndroidProject,
                EditorUserBuildSettings.development,
                sceneStates,
                new PlayerSettingsSplashState(true, true),
                Array.Empty<string>());
        }

        private static BuildVersionContext CreateVersionContext(string commitHash)
        {
            return new BuildVersionContext(
                "1.2.3",
                "1.2.3.42",
                42,
                commitHash,
                "42",
                "test-branch",
                "2026-01-02T03:04:05Z",
                "test");
        }

        private static void CreateFolder(string parentAssetPath, string folderName)
        {
            string guid = AssetDatabase.CreateFolder(parentAssetPath, folderName);
            Assert.That(guid, Is.Not.Empty);
        }

        private string ToAbsolutePath(string assetPath)
        {
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private void AssertOwnedGeneratedPathsAreAbsent(
            string nestedAssetPath,
            string resourcesAssetPath,
            string versionInfoAssetPath)
        {
            Assert.That(File.Exists(ToAbsolutePath(versionInfoAssetPath)), Is.False);
            Assert.That(File.Exists(ToAbsolutePath(versionInfoAssetPath) + ".meta"), Is.False);
            Assert.That(Directory.Exists(ToAbsolutePath(resourcesAssetPath)), Is.False);
            Assert.That(File.Exists(ToAbsolutePath(resourcesAssetPath) + ".meta"), Is.False);
            Assert.That(Directory.Exists(ToAbsolutePath(nestedAssetPath)), Is.False);
            Assert.That(File.Exists(ToAbsolutePath(nestedAssetPath) + ".meta"), Is.False);
            Assert.That(Directory.Exists(testRootAbsolutePath), Is.False);
            Assert.That(File.Exists(testRootAbsolutePath + ".meta"), Is.False);
            Assert.That(
                AssetDatabase.LoadMainAssetAtPath(versionInfoAssetPath),
                Is.Null);
        }

        private FileSnapshot CaptureSnapshot(string assetPath)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            return new FileSnapshot(
                File.ReadAllBytes(absolutePath),
                File.GetLastWriteTimeUtc(absolutePath),
                File.GetAttributes(absolutePath));
        }

        private void AssertSnapshot(string assetPath, FileSnapshot expected)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            Assert.That(File.Exists(absolutePath), Is.True);
            Assert.That(File.ReadAllBytes(absolutePath), Is.EqualTo(expected.Bytes));
            Assert.That(File.GetLastWriteTimeUtc(absolutePath), Is.EqualTo(expected.LastWriteTimeUtc));
            Assert.That(File.GetAttributes(absolutePath), Is.EqualTo(expected.Attributes));
        }

        private sealed class FileSnapshot
        {
            public FileSnapshot(
                byte[] bytes,
                DateTime lastWriteTimeUtc,
                FileAttributes attributes)
            {
                Bytes = bytes;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Attributes = attributes;
            }

            public byte[] Bytes { get; }
            public DateTime LastWriteTimeUtc { get; }
            public FileAttributes Attributes { get; }
        }
    }
}
