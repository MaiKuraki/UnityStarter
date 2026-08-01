using System;
using System.Collections.Generic;
using System.IO;
using CycloneGames.Logging.Unity.Editor;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Tests.Editor
{
    public sealed class LoggingSettingsBuildProcessorTests
    {
        private readonly List<LoggingSettings> _settings = new List<LoggingSettings>();

        [TearDown]
        public void TearDown()
        {
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
        public void MarkerValidation_AcceptsCompleteMatchingIdentity()
        {
            const string guid = "0123456789abcdef0123456789abcdef";
            string projectIdentity = LoggingSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, guid);

            bool valid = LoggingSettingsBuildProcessor.ValidateMarkerForTests(json, projectIdentity, guid, out string error);

            Assert.IsTrue(valid, error);
        }

        [Test]
        public void MarkerValidation_RejectsProjectIdentityMismatch()
        {
            const string guid = "0123456789abcdef0123456789abcdef";
            string projectIdentity = LoggingSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson("different-project", guid);

            bool valid = LoggingSettingsBuildProcessor.ValidateMarkerForTests(json, projectIdentity, guid, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("project identity", error);
        }

        [Test]
        public void MarkerValidation_RejectsAssetGuidMismatch()
        {
            const string markerGuid = "0123456789abcdef0123456789abcdef";
            const string actualGuid = "fedcba9876543210fedcba9876543210";
            string projectIdentity = LoggingSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, markerGuid);

            bool valid = LoggingSettingsBuildProcessor.ValidateMarkerForTests(json, projectIdentity, actualGuid, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("GUID", error);
        }

        [Test]
        public void PreparedMarkerCleanup_AcceptsMatchingMarkerWhenGeneratedAssetIsMissing()
        {
            string projectIdentity = LoggingSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, string.Empty, "Prepared");

            bool canCleanup = LoggingSettingsBuildProcessor.CanCleanupPreparedMarkerForTests(
                json,
                projectIdentity,
                generatedAssetExists: false,
                out string error);

            Assert.IsTrue(canCleanup, error);
        }

        [Test]
        public void PreparedMarkerCleanup_RejectsCleanupWhenGeneratedAssetStillExists()
        {
            string projectIdentity = LoggingSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, string.Empty, "Prepared");

            bool canCleanup = LoggingSettingsBuildProcessor.CanCleanupPreparedMarkerForTests(
                json,
                projectIdentity,
                generatedAssetExists: true,
                out string error);

            Assert.IsFalse(canCleanup);
            StringAssert.Contains("still exists", error);
        }

        [Test]
        public void MarkerRead_RejectsOversizedInputBeforeJsonParsing()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.Logging.BuildMarkerTests",
                Guid.NewGuid().ToString("N"));
            string markerPath = Path.Combine(directory, "oversized.marker.json");
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllBytes(
                    markerPath,
                    new byte[LoggingSettingsBuildProcessor.MaximumMarkerFileBytes + 1]);

                InvalidDataException exception = Assert.Throws<InvalidDataException>(
                    () => LoggingSettingsBuildProcessor.ReadMarkerJsonForTests(markerPath));
                StringAssert.Contains("byte limit", exception.Message);
                Assert.That(File.Exists(markerPath), Is.True, "Rejected markers must remain available for diagnosis.");
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

        private static string CreateMarkerJson(string projectIdentity, string guid, string phase = "Active")
        {
            return "{" +
                   "\"schemaVersion\":1," +
                   "\"transactionId\":\"0123456789abcdef0123456789abcdef\"," +
                   "\"projectIdentity\":\"" + projectIdentity + "\"," +
                   "\"generatedAssetGuid\":\"" + guid + "\"," +
                   "\"assetPath\":\"" + LoggingSettingsBuildProcessor.GeneratedSettingsAssetPath + "\"," +
                   "\"phase\":\"" + phase + "\"" +
                   "}";
        }
    }
}
