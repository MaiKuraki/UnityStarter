using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Build.Pipeline.Editor.Tests
{
    public sealed class BuildWorkspaceLeaseTests
    {
        private string projectRoot;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "BuildWorkspaceLeaseTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
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
        public void Acquire_WhenSameProcessAlreadyOwnsLease_FailsFastWithoutReplacingMetadata()
        {
            using (BuildWorkspaceLease first = BuildWorkspaceLease.Acquire(
                       projectRoot,
                       "first-run",
                       BuildWorkspaceOperation.Build))
            {
                string firstMetadata = File.ReadAllText(first.MetadataFilePath, Encoding.UTF8);

                BuildWorkspaceBusyException exception = Assert.Throws<BuildWorkspaceBusyException>(() =>
                    BuildWorkspaceLease.Acquire(
                        projectRoot,
                        "second-run",
                        BuildWorkspaceOperation.Recovery));

                Assert.That(exception.LeaseFilePath, Is.EqualTo(first.LeaseFilePath));
                Assert.That(
                    exception.AttemptedOperation,
                    Is.EqualTo(BuildWorkspaceOperation.Recovery));
                Assert.That(
                    File.ReadAllText(first.MetadataFilePath, Encoding.UTF8),
                    Is.EqualTo(firstMetadata));
            }
        }

        [Test]
        public void Dispose_ReleasesLeaseAndLeavesReusableMetadataFile()
        {
            string leaseFilePath;
            string metadataFilePath;
            using (BuildWorkspaceLease first = BuildWorkspaceLease.Acquire(
                       projectRoot,
                       "first-run",
                       BuildWorkspaceOperation.Build))
            {
                leaseFilePath = first.LeaseFilePath;
                metadataFilePath = first.MetadataFilePath;
            }

            Assert.That(File.Exists(leaseFilePath), Is.True);
            Assert.That(File.Exists(metadataFilePath), Is.True);

            using (BuildWorkspaceLease second = BuildWorkspaceLease.Acquire(
                       projectRoot,
                       "second-run",
                       BuildWorkspaceOperation.Recovery))
            {
                Assert.That(second.LeaseFilePath, Is.EqualTo(leaseFilePath));
                Assert.That(second.MetadataFilePath, Is.EqualTo(metadataFilePath));
                Assert.That(File.ReadAllText(metadataFilePath), Does.Contain("\"runId\":\"second-run\""));
            }
        }

        [Test]
        public void Acquire_WithUnlockedStaleFile_OverwritesMetadataWithoutDeletingFile()
        {
            string metadataFilePath = Path.Combine(
                projectRoot,
                "Temp",
                "BuildPipeline",
                "Workspace",
                "lease.json");
            Directory.CreateDirectory(Path.GetDirectoryName(metadataFilePath));
            File.WriteAllText(metadataFilePath, new string('s', 8192), Encoding.UTF8);

            using (BuildWorkspaceLease lease = BuildWorkspaceLease.Acquire(
                       projectRoot,
                       "current-run",
                       BuildWorkspaceOperation.Build))
            {
                byte[] metadata = File.ReadAllBytes(metadataFilePath);
                string json = new UTF8Encoding(false, true).GetString(metadata);

                Assert.That(lease.MetadataFilePath, Is.EqualTo(Path.GetFullPath(metadataFilePath)));
                Assert.That(metadata.Length, Is.LessThanOrEqualTo(BuildWorkspaceLease.MaximumMetadataUtf8Bytes));
                Assert.That(json, Does.Contain("\"runId\":\"current-run\""));
                Assert.That(json, Does.Not.Contain("sss"));
            }

            Assert.That(File.Exists(metadataFilePath), Is.True);
        }

        [Test]
        public void Acquire_WritesBoundedBomlessDeterministicMetadata()
        {
            var startedUtc = new DateTimeOffset(
                2026,
                8,
                7,
                1,
                2,
                3,
                TimeSpan.Zero);

            using (BuildWorkspaceLease lease = BuildWorkspaceLease.Acquire(
                       projectRoot,
                       "release-42",
                       BuildWorkspaceOperation.Recovery,
                       startedUtc,
                       processId: 4242))
            {
                byte[] metadata = File.ReadAllBytes(lease.MetadataFilePath);
                string json = new UTF8Encoding(false, true).GetString(metadata);

                Assert.That(metadata.Length, Is.LessThanOrEqualTo(BuildWorkspaceLease.MaximumMetadataUtf8Bytes));
                Assert.That(
                    metadata.Length < 3
                    || metadata[0] != 0xef
                    || metadata[1] != 0xbb
                    || metadata[2] != 0xbf,
                    Is.True,
                    "Lease metadata must not contain a UTF-8 BOM.");
                Assert.That(
                    json,
                    Is.EqualTo(
                        "{\"documentType\":\"build-workspace-lease\",\"runId\":\"release-42\",\"operation\":\"recovery\",\"pid\":4242," +
                        "\"startedUtc\":\"2026-08-07T01:02:03.0000000Z\"}"));
                Assert.That(lease.StartedUtc, Is.EqualTo(startedUtc));
            }
        }

        [Test]
        public void Acquire_WithMissingProjectRoot_ThrowsWithoutCreatingWorkspace()
        {
            string missingRoot = Path.Combine(projectRoot, "MissingProject");

            Assert.Throws<DirectoryNotFoundException>(() =>
                BuildWorkspaceLease.Acquire(
                    missingRoot,
                    "run",
                    BuildWorkspaceOperation.Build));

            Assert.That(Directory.Exists(Path.Combine(missingRoot, "Temp")), Is.False);
        }

        [TestCase("Assets")]
        [TestCase("ProjectSettings")]
        public void Acquire_WithMissingUnityProjectMarker_ThrowsWithoutCreatingWorkspace(
            string missingDirectory)
        {
            Directory.Delete(Path.Combine(projectRoot, missingDirectory));

            Assert.Throws<InvalidOperationException>(() =>
                BuildWorkspaceLease.Acquire(
                    projectRoot,
                    "run",
                    BuildWorkspaceOperation.Build));

            Assert.That(Directory.Exists(Path.Combine(projectRoot, "Temp")), Is.False);
        }

        [Test]
        public void Acquire_WithUnsupportedOperation_ThrowsWithoutCreatingWorkspace()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BuildWorkspaceLease.Acquire(
                    projectRoot,
                    "run",
                    (BuildWorkspaceOperation)999));

            Assert.That(Directory.Exists(Path.Combine(projectRoot, "Temp")), Is.False);
        }

        [Test]
        public void Acquire_WithReparsePointInWorkspaceChain_FailsClosed()
        {
            string externalRoot = Path.Combine(
                Path.GetTempPath(),
                "BuildWorkspaceLeaseTarget-" + Guid.NewGuid().ToString("N"));
            string tempLink = Path.Combine(projectRoot, "Temp");
            Directory.CreateDirectory(externalRoot);

            try
            {
                CreateDirectoryLink(tempLink, externalRoot);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    BuildWorkspaceLease.Acquire(
                        projectRoot,
                        "run",
                        BuildWorkspaceOperation.Build));

                StringAssert.Contains("reparse-point", exception.Message);
                Assert.That(
                    File.Exists(Path.Combine(externalRoot, "BuildPipeline", "Workspace", "lease.lock")),
                    Is.False);
            }
            finally
            {
                DeleteDirectoryLink(tempLink);
                if (Directory.Exists(externalRoot))
                {
                    Directory.Delete(externalRoot, recursive: true);
                }
            }
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
