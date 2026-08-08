using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Build.Pipeline.Editor.Tests
{
    public sealed class ProjectSettingsStateGuardTests
    {
        private string projectRoot;
        private string projectSettingsRoot;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "ProjectSettingsStateGuardTests-" + Guid.NewGuid().ToString("N"));
            projectSettingsRoot = Path.Combine(projectRoot, "ProjectSettings");
            Directory.CreateDirectory(projectSettingsRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void CaptureAndVerify_UnchangedFiles_IsCleanAndDoesNotWrite()
        {
            string file = WriteProjectSettingsFile(
                "ProjectSettings.asset",
                "original");
            DateTime timestamp = new DateTime(638700000000000000L, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, timestamp);
            DateTime persistedTimestamp = File.GetLastWriteTimeUtc(file);

            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);
            ProjectSettingsStateVerificationResult result = guard.Verify();

            Assert.That(result.IsClean, Is.True);
            Assert.That(result.Changes, Is.Empty);
            Assert.That(File.ReadAllText(file), Is.EqualTo("original"));
            Assert.That(File.GetLastWriteTimeUtc(file), Is.EqualTo(persistedTimestamp));
        }

        [Test]
        public void Verify_AddedDeletedAndModifiedFiles_ReturnsStablePathOrder()
        {
            string deleted = WriteProjectSettingsFile("A.asset", "delete-me");
            string modified = WriteProjectSettingsFile("Nested/B.asset", "before");
            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);

            File.Delete(deleted);
            File.WriteAllText(modified, "after");
            WriteProjectSettingsFile("Z.asset", "added");

            ProjectSettingsStateVerificationResult result = guard.Verify();

            Assert.That(result.IsClean, Is.False);
            Assert.That(
                result.Changes.Select(change => change.ProjectRelativePath),
                Is.EqualTo(new[]
                {
                    "ProjectSettings/A.asset",
                    "ProjectSettings/Nested/B.asset",
                    "ProjectSettings/Z.asset"
                }));
            Assert.That(
                result.Changes.Select(change => change.Kind),
                Is.EqualTo(new[]
                {
                    ProjectSettingsStateChangeKind.Deleted,
                    ProjectSettingsStateChangeKind.Modified,
                    ProjectSettingsStateChangeKind.Added
                }));
            Assert.That(result.Changes[0].BaselineSha256, Is.Not.Empty);
            Assert.That(result.Changes[0].CurrentSha256, Is.Null);
            Assert.That(result.Changes[2].BaselineSha256, Is.Null);
            Assert.That(result.Changes[2].CurrentSha256, Is.Not.Empty);
        }

        [Test]
        public void VerifyOrThrow_ReportsEveryChangeKindAndDoesNotWrite()
        {
            string deleted = WriteProjectSettingsFile("Deleted.asset", "before");
            string modified = WriteProjectSettingsFile("Modified.asset", "before");
            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);

            File.Delete(deleted);
            File.WriteAllText(modified, "after");
            string added = WriteProjectSettingsFile("Added.asset", "new");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => guard.VerifyOrThrow("Player publication gate"));

            Assert.That(exception.Message, Does.Contain("Player publication gate"));
            Assert.That(exception.Message, Does.Contain("Added"));
            Assert.That(exception.Message, Does.Contain("Deleted"));
            Assert.That(exception.Message, Does.Contain("Modified"));
            Assert.That(File.ReadAllText(modified), Is.EqualTo("after"));
            Assert.That(File.ReadAllText(added), Is.EqualTo("new"));
            Assert.That(File.Exists(deleted), Is.False);
        }

        [Test]
        public void AuthorizationWindow_OnlyAllowedFileChanges_RefreshesItsBaseline()
        {
            string playerSettings = WriteProjectSettingsFile(
                "ProjectSettings.asset",
                "before");
            WriteProjectSettingsFile("GraphicsSettings.asset", "stable");
            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);

            ProjectSettingsStateVerificationResult authorizedChanges;
            using (ProjectSettingsStateGuard.AuthorizationWindow window =
                   guard.BeginAuthorization(
                       "ProjectSettings/ProjectSettings.asset"))
            {
                File.WriteAllText(playerSettings, "applied");
                authorizedChanges = window.Commit();
            }

            Assert.That(authorizedChanges.Changes.Count, Is.EqualTo(1));
            Assert.That(
                authorizedChanges.Changes[0].ProjectRelativePath,
                Is.EqualTo("ProjectSettings/ProjectSettings.asset"));
            Assert.That(guard.Verify().IsClean, Is.True);
        }

        [Test]
        public void AuthorizationWindow_AllowedDeletion_RefreshesAbsenceBaseline()
        {
            string transientSettings = WriteProjectSettingsFile(
                "Transient.asset",
                "temporary");
            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);

            using (ProjectSettingsStateGuard.AuthorizationWindow window =
                   guard.BeginAuthorization("ProjectSettings/Transient.asset"))
            {
                File.Delete(transientSettings);
                ProjectSettingsStateVerificationResult changes = window.Commit();
                Assert.That(changes.Changes.Single().Kind,
                    Is.EqualTo(ProjectSettingsStateChangeKind.Deleted));
            }

            Assert.That(guard.Verify().IsClean, Is.True);
        }

        [Test]
        public void AuthorizationWindow_UnlistedFileChanges_FailsWithoutRebaselining()
        {
            string playerSettings = WriteProjectSettingsFile(
                "ProjectSettings.asset",
                "before");
            string graphicsSettings = WriteProjectSettingsFile(
                "GraphicsSettings.asset",
                "before");
            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);

            InvalidOperationException exception;
            using (ProjectSettingsStateGuard.AuthorizationWindow window =
                   guard.BeginAuthorization(
                       "ProjectSettings/ProjectSettings.asset"))
            {
                File.WriteAllText(playerSettings, "applied");
                File.WriteAllText(graphicsSettings, "unexpected");
                exception = Assert.Throws<InvalidOperationException>(
                    () => window.Commit());
            }

            Assert.That(exception.Message,
                Does.Contain("ProjectSettings/GraphicsSettings.asset"));
            ProjectSettingsStateVerificationResult remaining = guard.Verify();
            Assert.That(remaining.Changes.Count, Is.EqualTo(2));
            Assert.That(
                remaining.Changes.Select(change => change.ProjectRelativePath),
                Does.Contain("ProjectSettings/ProjectSettings.asset"));
            Assert.That(
                remaining.Changes.Select(change => change.ProjectRelativePath),
                Does.Contain("ProjectSettings/GraphicsSettings.asset"));
        }

        [TestCase("../outside.asset")]
        [TestCase("ProjectSettings/../outside.asset")]
        [TestCase("Assets/not-settings.asset")]
        [TestCase("ProjectSettings")]
        public void BeginAuthorization_OutsideOrAmbiguousPath_Throws(
            string path)
        {
            WriteProjectSettingsFile("ProjectSettings.asset", "stable");
            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);

            Assert.Throws<ArgumentException>(() => guard.BeginAuthorization(path));
            Assert.That(guard.Verify().IsClean, Is.True);
        }

        [Test]
        public void BeginAuthorization_DuplicatePath_ThrowsWithoutOpeningWindow()
        {
            WriteProjectSettingsFile("ProjectSettings.asset", "stable");
            ProjectSettingsStateGuard guard =
                ProjectSettingsStateGuard.Capture(projectRoot);

            Assert.Throws<ArgumentException>(() => guard.BeginAuthorization(
                "ProjectSettings/ProjectSettings.asset",
                "ProjectSettings/ProjectSettings.asset"));

            Assert.That(guard.Verify().IsClean, Is.True);
        }

        [Test]
        public void Capture_MissingProjectSettingsDirectory_ThrowsWithoutCreatingIt()
        {
            Directory.Delete(projectSettingsRoot);

            Assert.Throws<DirectoryNotFoundException>(
                () => ProjectSettingsStateGuard.Capture(projectRoot));
            Assert.That(Directory.Exists(projectSettingsRoot), Is.False);
        }

        [Test]
        public void Capture_NestedReparsePoint_ThrowsWithoutReadingOrWritingTarget()
        {
            string externalTarget = Path.Combine(
                Path.GetTempPath(),
                "ProjectSettingsStateGuardTarget-" + Guid.NewGuid().ToString("N"));
            string link = Path.Combine(projectSettingsRoot, "ExternalLink");
            string sentinel = Path.Combine(externalTarget, "sentinel.asset");
            Directory.CreateDirectory(externalTarget);
            File.WriteAllText(sentinel, "preserve");

            try
            {
                CreateDirectoryLink(link, externalTarget);

                InvalidOperationException exception =
                    Assert.Throws<InvalidOperationException>(
                        () => ProjectSettingsStateGuard.Capture(projectRoot));

                Assert.That(exception.Message,
                    Does.Contain("symbolic link or reparse point"));
                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("preserve"));
            }
            finally
            {
                DeleteDirectoryLink(link);
                if (Directory.Exists(externalTarget))
                {
                    Directory.Delete(externalTarget, recursive: true);
                }
            }
        }

        private string WriteProjectSettingsFile(
            string relativePath,
            string content)
        {
            string path = Path.Combine(
                projectSettingsRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        private static void CreateDirectoryLink(string linkPath, string targetPath)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            var startInfo = new ProcessStartInfo
            {
                FileName = windows
                    ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
                    : "/bin/ln",
                Arguments = windows
                    ? $"/d /c mklink /J {QuoteArgument(linkPath)} {QuoteArgument(targetPath)}"
                    : $"-s {QuoteArgument(targetPath)} {QuoteArgument(linkPath)}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    $"Failed to create a test reparse point. Output: {standardOutput} Error: {standardError}");
            }
        }

        private static void DeleteDirectoryLink(string linkPath)
        {
            if (!Directory.Exists(linkPath) && !File.Exists(linkPath))
            {
                return;
            }

            try
            {
                Directory.Delete(linkPath, recursive: false);
            }
            catch (IOException)
            {
                File.Delete(linkPath);
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
