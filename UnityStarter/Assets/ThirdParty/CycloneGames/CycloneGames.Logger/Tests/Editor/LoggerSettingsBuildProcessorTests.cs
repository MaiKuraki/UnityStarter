using System;
using System.Collections.Generic;
using System.IO;
using CycloneGames.Logger.Editor;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEngine;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class LoggerSettingsBuildProcessorTests
    {
        private readonly List<LoggerSettings> _settings = new List<LoggerSettings>();

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
            string projectIdentity = LoggerSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, guid);

            bool valid = LoggerSettingsBuildProcessor.ValidateMarkerForTests(json, projectIdentity, guid, out string error);

            Assert.IsTrue(valid, error);
        }

        [Test]
        public void MarkerValidation_RejectsProjectIdentityMismatch()
        {
            const string guid = "0123456789abcdef0123456789abcdef";
            string projectIdentity = LoggerSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson("different-project", guid);

            bool valid = LoggerSettingsBuildProcessor.ValidateMarkerForTests(json, projectIdentity, guid, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("project identity", error);
        }

        [Test]
        public void MarkerValidation_RejectsAssetGuidMismatch()
        {
            const string markerGuid = "0123456789abcdef0123456789abcdef";
            const string actualGuid = "fedcba9876543210fedcba9876543210";
            string projectIdentity = LoggerSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, markerGuid);

            bool valid = LoggerSettingsBuildProcessor.ValidateMarkerForTests(json, projectIdentity, actualGuid, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("GUID", error);
        }

        [Test]
        public void PreparedMarkerCleanup_AcceptsMatchingMarkerWhenGeneratedAssetIsMissing()
        {
            string projectIdentity = LoggerSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, string.Empty, "Prepared");

            bool canCleanup = LoggerSettingsBuildProcessor.CanCleanupPreparedMarkerForTests(
                json,
                projectIdentity,
                generatedAssetExists: false,
                out string error);

            Assert.IsTrue(canCleanup, error);
        }

        [Test]
        public void PreparedMarkerCleanup_RejectsCleanupWhenGeneratedAssetStillExists()
        {
            string projectIdentity = LoggerSettingsBuildProcessor.ComputeProjectIdentityForTests(Application.dataPath);
            string json = CreateMarkerJson(projectIdentity, string.Empty, "Prepared");

            bool canCleanup = LoggerSettingsBuildProcessor.CanCleanupPreparedMarkerForTests(
                json,
                projectIdentity,
                generatedAssetExists: true,
                out string error);

            Assert.IsFalse(canCleanup);
            StringAssert.Contains("still exists", error);
        }

        [Test]
        public void ExplicitUndefinedEnvironmentEnum_FailsInsteadOfBeingIgnored()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGER_LEVEL"] = "255"
            };
            LoggerSettings settings = CreateSettings();

            Assert.Throws<BuildFailedException>(() =>
                LoggerSettingsBuildProcessor.ApplyOptionsForTests(settings, key => ReadEnvironment(environment, key), Array.Empty<string>()));
        }

        [Test]
        public void ExplicitInvalidEnvironmentInteger_FailsInsteadOfBeingIgnored()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGER_MAX_QUEUED_MESSAGES"] = "0"
            };
            LoggerSettings settings = CreateSettings();

            Assert.Throws<BuildFailedException>(() =>
                LoggerSettingsBuildProcessor.ApplyOptionsForTests(settings, key => ReadEnvironment(environment, key), Array.Empty<string>()));
        }

        [Test]
        public void CommandLineValue_OverridesEnvironmentValue()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGER_LEVEL"] = "Warning"
            };
            LoggerSettings settings = CreateSettings();

            bool hasOverrides = LoggerSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                new[] { "-loggerLevel", "Error" });

            Assert.IsTrue(hasOverrides);
            Assert.AreEqual(LogLevel.Error, settings.defaultLevel);
        }

        [Test]
        public void NoOverrides_StillValidateSettings()
        {
            LoggerSettings settings = CreateSettings();
            settings.maxQueuedMessages = 0;

            Assert.Throws<BuildFailedException>(() =>
                LoggerSettingsBuildProcessor.ApplyOptionsForTests(
                    settings,
                    _ => null,
                    Array.Empty<string>()));
        }

        [Test]
        public void UnityConsoleBlockPolicy_FailsBuildValidation()
        {
            LoggerSettings settings = CreateSettings();
            settings.unityConsoleOverflowPolicy = LogQueueOverflowPolicy.Block;

            Assert.Throws<BuildFailedException>(() => LoggerSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void ConsoleEnvironmentOverride_IsApplied()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGER_CONSOLE"] = "true"
            };
            LoggerSettings settings = CreateSettings();
            settings.registerConsoleLogger = false;

            bool hasOverrides = LoggerSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                Array.Empty<string>());

            Assert.IsTrue(hasOverrides);
            Assert.IsTrue(settings.registerConsoleLogger);
        }

        [Test]
        public void ConsoleCommandLineOverride_WinsOverEnvironment()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGER_CONSOLE"] = "true"
            };
            LoggerSettings settings = CreateSettings();

            LoggerSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                key => ReadEnvironment(environment, key),
                new[] { "-loggerConsole", "false" });

            Assert.IsFalse(settings.registerConsoleLogger);
        }

        [TestCase("Off", false, false)]
        [TestCase("Unity", true, false)]
        [TestCase("File", false, true)]
        [TestCase("UnityAndFile", true, true)]
        public void BuildMode_ExplicitlyDisablesConsoleSink(string mode, bool expectedUnity, bool expectedFile)
        {
            LoggerSettings settings = CreateSettings();
            settings.registerUnityLogger = true;
            settings.registerConsoleLogger = true;
            settings.registerFileLogger = true;

            LoggerSettingsBuildProcessor.ApplyOptionsForTests(
                settings,
                _ => null,
                new[] { "-loggerMode", mode });

            Assert.AreEqual(expectedUnity, settings.registerUnityLogger);
            Assert.IsFalse(settings.registerConsoleLogger);
            Assert.AreEqual(expectedFile, settings.registerFileLogger);
        }

        [Test]
        public void EmptyCustomPathOverride_ClearsInactiveCustomPath()
        {
            var environment = new Dictionary<string, string>
            {
                ["CG_LOGGER_CUSTOM_FILE_PATH"] = string.Empty
            };
            LoggerSettings settings = CreateSettings();
            settings.customFilePath = "old.log";
            settings.usePersistentDataPath = true;

            LoggerSettingsBuildProcessor.ApplyOptionsForTests(settings, key => ReadEnvironment(environment, key), Array.Empty<string>());

            Assert.AreEqual(string.Empty, settings.customFilePath);
        }

        [Test]
        public void PortableFileNameValidation_RejectsDirectoryTraversal()
        {
            LoggerSettings settings = CreateSettings();
            settings.fileName = "../outside.log";

            Assert.Throws<BuildFailedException>(() => LoggerSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void PortableFileNameValidation_RejectsWindowsReservedName()
        {
            LoggerSettings settings = CreateSettings();
            settings.fileName = "CON.log";

            Assert.Throws<BuildFailedException>(() => LoggerSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void CustomFilePathValidation_RequiresPathWhenActive()
        {
            LoggerSettings settings = CreateSettings();
            settings.registerFileLogger = true;
            settings.usePersistentDataPath = false;
            settings.customFilePath = string.Empty;

            Assert.Throws<BuildFailedException>(() => LoggerSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void CustomFilePathValidation_RejectsRelativePath()
        {
            LoggerSettings settings = CreateSettings();
            settings.registerFileLogger = true;
            settings.usePersistentDataPath = false;
            settings.allowCustomFilePath = true;
            settings.customFilePath = Path.Combine("logs", "game.log");

            Assert.Throws<BuildFailedException>(() => LoggerSettingsBuildProcessor.ValidateSettings(settings));
        }

        [Test]
        public void CustomFilePathValidation_AcceptsRootedAbsolutePath()
        {
            LoggerSettings settings = CreateSettings();
            settings.registerFileLogger = true;
            settings.usePersistentDataPath = false;
            settings.allowCustomFilePath = true;
            settings.customFilePath = Path.Combine(Path.GetTempPath(), "CycloneGames.Logger", "game.log");

            Assert.DoesNotThrow(() => LoggerSettingsBuildProcessor.ValidateSettings(settings));
        }

        private LoggerSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<LoggerSettings>();
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
                   "\"assetPath\":\"" + LoggerSettingsBuildProcessor.GeneratedSettingsAssetPath + "\"," +
                   "\"phase\":\"" + phase + "\"" +
                   "}";
        }
    }
}
