using System;
using System.Globalization;
using System.IO;
using CycloneGames.Logging;
using CycloneGames.Logging.Unity.Editor;
using NUnit.Framework;

namespace CycloneGames.Logging.Unity.Tests.Editor
{
    public sealed class UnityConsoleEditorTests
    {
        [Test]
        public void FormatMessage_UsesHrefPathAndLineForConsoleNavigation()
        {
            LoggingEditorLinkRegistry.Reset();
            LoggingEditorPathResolver.Configure(
                UnityEngine.Application.dataPath,
                UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor);
            string sourcePath = Path.Combine(UnityEngine.Application.dataPath, "Game", "Foo.cs");
            string expectedFullPath = Path.GetFullPath(sourcePath).Replace('\\', '/');
            try
            {
                string formatted = FormatThroughPublicWriter(
                    LogSeverity.Info,
                    "Gameplay",
                    "hello",
                    sourcePath,
                    42,
                    nameof(FormatMessage_UsesHrefPathAndLineForConsoleNavigation));
                string linkPath = LoggingEditorLinkRegistry.Register(
                    "Assets/Game/Foo.cs",
                    42,
                    expectedFullPath);

                StringAssert.Contains("[Gameplay] hello", formatted);
                StringAssert.Contains("href=\"" + linkPath + ":42\"", formatted);
                StringAssert.Contains("(at Assets/Game/Foo.cs:42)", formatted);
                Assert.IsTrue(LoggingEditorLinkRegistry.TryGetFullPath(linkPath, 42, out var fullPath));
                Assert.AreEqual(expectedFullPath, fullPath);
            }
            finally
            {
                LoggingEditorLinkRegistry.Reset();
            }
        }

        [Test]
        public void DoubleClickBridge_IsAvailableForCurrentEditor()
        {
            Assert.IsTrue(
                LoggingUnityConsoleBridge.IsAvailable,
                "The current Unity Editor no longer exposes the Console entry callback contract used for caller navigation.");
        }

        [Test]
        public void LinkIdentityAndLineParsing_AreIndependentOfCurrentCulture()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            var registrationCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            registrationCulture.NumberFormat.NegativeSign = new string('!', 1024);
            var lookupCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            lookupCulture.NumberFormat.NegativeSign = "?";
            try
            {
                LoggingEditorLinkRegistry.Reset();
                CultureInfo.CurrentCulture = registrationCulture;
                string linkPath = LoggingEditorLinkRegistry.Register(
                    "Assets/Game/Foo.cs",
                    -123,
                    "C:/Project/Assets/Game/Foo.cs");

                CultureInfo.CurrentCulture = lookupCulture;
                Assert.IsTrue(LoggingEditorLinkRegistry.TryGetFullPath(linkPath, -123, out string fullPath));
                Assert.AreEqual("C:/Project/Assets/Game/Foo.cs", fullPath);

                var parseMethod = typeof(LoggingHyperlinkHandler).GetMethod(
                    "ParseLineNumber",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.IsNotNull(parseMethod);
                int parsed = (int)parseMethod.Invoke(null, new object[] { null, null, null, "-123" });
                Assert.AreEqual(-123, parsed);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                LoggingEditorLinkRegistry.Reset();
            }
        }

        [Test]
        public void LinkRegistry_DistinguishesSameDisplayPathAndLine()
        {
            LoggingEditorLinkRegistry.Reset();
            try
            {
                string firstPath = "C:/Packages/First/Runtime/Foo.cs";
                string secondPath = "C:/Packages/Second/Runtime/Foo.cs";
                string firstLink = LoggingEditorLinkRegistry.Register("Foo.cs", 42, firstPath);
                string secondLink = LoggingEditorLinkRegistry.Register("Foo.cs", 42, secondPath);

                Assert.AreNotEqual(firstLink, secondLink);
                Assert.IsTrue(LoggingEditorLinkRegistry.TryGetFullPath(firstLink, 42, out string firstResolved));
                Assert.IsTrue(LoggingEditorLinkRegistry.TryGetFullPath(secondLink, 42, out string secondResolved));
                Assert.AreEqual(firstPath, firstResolved);
                Assert.AreEqual(secondPath, secondResolved);
            }
            finally
            {
                LoggingEditorLinkRegistry.Reset();
            }
        }

        [Test]
        public void PackageRootCache_ReusesSnapshotUntilInvalidated()
        {
            LoggingHyperlinkHandler.InvalidatePackageRootCache();
            int refreshCount = LoggingHyperlinkHandler.PackageRootRefreshCountForTests;
            string externalProbePath = Path.GetFullPath(Path.Combine(
                    UnityEngine.Application.dataPath,
                    "..",
                    "..",
                    "CycloneGames.Logging.CacheProbe",
                    "Probe.cs"))
                .Replace('\\', '/');
            try
            {
                LoggingHyperlinkHandler.IsAllowedLoggingSourcePath(externalProbePath);
                LoggingHyperlinkHandler.IsAllowedLoggingSourcePath(externalProbePath);

                Assert.AreEqual(
                    refreshCount + 1,
                    LoggingHyperlinkHandler.PackageRootRefreshCountForTests);

                LoggingHyperlinkHandler.InvalidatePackageRootCache();
                LoggingHyperlinkHandler.IsAllowedLoggingSourcePath(externalProbePath);

                Assert.AreEqual(
                    refreshCount + 2,
                    LoggingHyperlinkHandler.PackageRootRefreshCountForTests);
            }
            finally
            {
                LoggingHyperlinkHandler.InvalidatePackageRootCache();
            }
        }

        private static string FormatThroughPublicWriter(
            LogSeverity severity,
            string category,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded();
            var sink = new FormattingSink();
            try
            {
                Assert.IsTrue(pipeline.AddSink(sink));
                ((ILogWriter)pipeline).Write(
                    severity,
                    category,
                    message,
                    filePath,
                    lineNumber,
                    memberName);
                pipeline.Pump(1);
                Assert.IsNotNull(sink.FormattedMessage);
                return sink.FormattedMessage;
            }
            finally
            {
                pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            }
        }

        private sealed class FormattingSink : ILogSink
        {
            internal string FormattedMessage;

            public void Emit(LogEvent logEvent)
            {
                FormattedMessage = UnityConsoleLogSink.FormatMessage(logEvent);
            }

            public void Dispose()
            {
            }
        }
    }
}
