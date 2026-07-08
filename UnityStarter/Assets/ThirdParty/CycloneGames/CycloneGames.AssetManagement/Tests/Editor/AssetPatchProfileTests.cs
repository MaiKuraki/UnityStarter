using System;
using System.Collections.Generic;
using System.IO;

using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Trust;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetPatchProfileTests
    {
        [Test]
        public void RuntimeProfileBuilder_Creates_RunOptions_With_Trust_Policy()
        {
            string root = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.ProfileTests");
            var trustPolicy = new AssetPatchTrustPolicy(
                enabled: true,
                root,
                PatchTrustFailurePolicy.ClearUnusedCacheThenFail,
                rollbackVersionOverride: "manifest-rollback",
                clearUnusedCacheAfterRollback: true);

            AssetPatchRuntimeProfile profile = new AssetPatchRuntimeProfileBuilder()
                .WithPackageName("Main")
                .WithPlatform(AssetPatchPlatform.Android)
                .WithAutoDownloadOnFoundNewVersion(false)
                .WithAppendTimeTicks(false)
                .WithDownloadPolicy(new AssetPatchDownloadPolicy(4, 2, 30))
                .WithTrustPolicy(trustPolicy)
                .Build();

            var manifest = new ContentTrustManifest(
                "manifest-current",
                Array.Empty<ContentTrustFileEntry>(),
                rollbackVersion: "manifest-previous");
            PatchRunOptions options = profile.CreateRunOptions(manifest, failureBuffer: new List<ContentTrustVerificationResult>(4));

            Assert.AreEqual("Main", profile.PackageName);
            Assert.AreEqual(AssetPatchPlatform.Android, profile.Platform);
            Assert.IsFalse(options.AutoDownloadOnFoundNewVersion);
            Assert.IsFalse(options.AppendTimeTicks);
            Assert.AreEqual(4, options.DownloadOptions.MaxConcurrentDownloads);
            Assert.IsTrue(options.TrustOptions.Enabled);
            Assert.AreEqual(root, options.TrustOptions.RootDirectory);
            Assert.AreEqual(PatchTrustFailurePolicy.ClearUnusedCacheThenFail, options.TrustOptions.FailurePolicy);
        }

        [Test]
        public void RuntimeProfileBuilder_Rejects_Empty_Package_Name()
        {
            Assert.Throws<InvalidOperationException>(() => new AssetPatchRuntimeProfileBuilder()
                .WithPackageName(string.Empty)
                .Build());
        }

        [Test]
        public void ProfileAsset_Builds_Default_Profile_With_Explicit_Trust_Root()
        {
            string root = CreateTempDirectory();
            AssetPatchProfileAsset asset = ScriptableObject.CreateInstance<AssetPatchProfileAsset>();

            try
            {
                SerializedObject serialized = new SerializedObject(asset);
                serialized.FindProperty("PackageName").stringValue = "Main";
                SerializedProperty settings = serialized.FindProperty("DefaultSettings");
                settings.FindPropertyRelative("AutoDownloadOnFoundNewVersion").boolValue = true;
                settings.FindPropertyRelative("AppendTimeTicks").boolValue = false;
                settings.FindPropertyRelative("MaxConcurrentDownloads").intValue = 6;
                settings.FindPropertyRelative("FailedRetryCount").intValue = 4;
                settings.FindPropertyRelative("RequestTimeoutSeconds").intValue = 45;
                settings.FindPropertyRelative("ContentTrustEnabled").boolValue = true;
                settings.FindPropertyRelative("ContentTrustRootSource").enumValueIndex = (int)AssetPatchRootDirectorySource.ExplicitPath;
                settings.FindPropertyRelative("ContentTrustRootPath").stringValue = root;
                settings.FindPropertyRelative("TrustFailurePolicy").enumValueIndex = (int)PatchTrustFailurePolicy.RollbackManifestThenFail;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(asset.TryBuildRuntimeProfile(AssetPatchPlatform.Windows, out AssetPatchRuntimeProfile profile, out string error), error);
                PatchRunOptions options = profile.CreateRunOptions(new ContentTrustManifest("manifest-current", Array.Empty<ContentTrustFileEntry>()));

                Assert.AreEqual("Main", profile.PackageName);
                Assert.AreEqual(AssetPatchPlatform.Windows, profile.Platform);
                Assert.AreEqual(6, options.DownloadOptions.MaxConcurrentDownloads);
                Assert.IsFalse(options.AppendTimeTicks);
                Assert.IsTrue(options.TrustOptions.Enabled);
                Assert.AreEqual(Path.GetFullPath(root), options.TrustOptions.RootDirectory);
                Assert.AreEqual(PatchTrustFailurePolicy.RollbackManifestThenFail, options.TrustOptions.FailurePolicy);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void ProfileAsset_Uses_Platform_Override_When_Matched()
        {
            AssetPatchProfileAsset asset = ScriptableObject.CreateInstance<AssetPatchProfileAsset>();

            try
            {
                SerializedObject serialized = new SerializedObject(asset);
                serialized.FindProperty("PackageName").stringValue = "Main";
                SerializedProperty defaultSettings = serialized.FindProperty("DefaultSettings");
                defaultSettings.FindPropertyRelative("AutoDownloadOnFoundNewVersion").boolValue = true;
                defaultSettings.FindPropertyRelative("MaxConcurrentDownloads").intValue = 10;

                SerializedProperty overrides = serialized.FindProperty("PlatformOverrides");
                overrides.arraySize = 1;
                SerializedProperty androidOverride = overrides.GetArrayElementAtIndex(0);
                androidOverride.FindPropertyRelative("Enabled").boolValue = true;
                androidOverride.FindPropertyRelative("Platform").enumValueIndex = (int)AssetPatchPlatform.Android;
                SerializedProperty overrideSettings = androidOverride.FindPropertyRelative("Settings");
                overrideSettings.FindPropertyRelative("AutoDownloadOnFoundNewVersion").boolValue = false;
                overrideSettings.FindPropertyRelative("MaxConcurrentDownloads").intValue = 3;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                AssetPatchRuntimeProfile android = asset.BuildRuntimeProfile(AssetPatchPlatform.Android);
                AssetPatchRuntimeProfile windows = asset.BuildRuntimeProfile(AssetPatchPlatform.Windows);

                Assert.IsFalse(android.AutoDownloadOnFoundNewVersion);
                Assert.AreEqual(3, android.DownloadPolicy.MaxConcurrentDownloads);
                Assert.IsTrue(windows.AutoDownloadOnFoundNewVersion);
                Assert.AreEqual(10, windows.DownloadPolicy.MaxConcurrentDownloads);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ProfileAsset_Prefers_Exact_Platform_Override_Before_Any_Fallback()
        {
            AssetPatchProfileAsset asset = ScriptableObject.CreateInstance<AssetPatchProfileAsset>();

            try
            {
                SerializedObject serialized = new SerializedObject(asset);
                serialized.FindProperty("PackageName").stringValue = "Main";

                SerializedProperty overrides = serialized.FindProperty("PlatformOverrides");
                overrides.arraySize = 2;

                SerializedProperty anyOverride = overrides.GetArrayElementAtIndex(0);
                anyOverride.FindPropertyRelative("Enabled").boolValue = true;
                anyOverride.FindPropertyRelative("Platform").enumValueIndex = (int)AssetPatchPlatform.Any;
                anyOverride.FindPropertyRelative("Settings").FindPropertyRelative("MaxConcurrentDownloads").intValue = 2;

                SerializedProperty androidOverride = overrides.GetArrayElementAtIndex(1);
                androidOverride.FindPropertyRelative("Enabled").boolValue = true;
                androidOverride.FindPropertyRelative("Platform").enumValueIndex = (int)AssetPatchPlatform.Android;
                androidOverride.FindPropertyRelative("Settings").FindPropertyRelative("MaxConcurrentDownloads").intValue = 7;

                serialized.ApplyModifiedPropertiesWithoutUndo();

                AssetPatchRuntimeProfile android = asset.BuildRuntimeProfile(AssetPatchPlatform.Android);
                AssetPatchRuntimeProfile windows = asset.BuildRuntimeProfile(AssetPatchPlatform.Windows);

                Assert.AreEqual(7, android.DownloadPolicy.MaxConcurrentDownloads);
                Assert.AreEqual(2, windows.DownloadPolicy.MaxConcurrentDownloads);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.ProfileTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
