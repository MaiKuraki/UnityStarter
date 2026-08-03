using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildPathPolicyTests
    {
        private string sandboxBaseRoot;
        private string sandboxRunRoot;
        private string projectRoot;
        private string buildRoot;

        [SetUp]
        public void SetUp()
        {
            sandboxBaseRoot = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                "BuildPipelineTests"));
            sandboxRunRoot = Path.Combine(sandboxBaseRoot, "run-" + Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRunRoot, "deep", "nested", "UnityProject");

            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Packages"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Library"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "UserSettings"));

            buildRoot = BuildPathPolicy.ResolveBuildRoot(projectRoot, "Build");
            Directory.CreateDirectory(buildRoot);
        }

        [TearDown]
        public void TearDown()
        {
            DeleteSandboxRunRoot();
        }

        [Test]
        public void ResolveBuildRoot_WithTraversalOutsideProject_Throws()
        {
            string traversal = Path.Combine("..", "outside-build");

            Assert.Throws<InvalidOperationException>(
                () => BuildPathPolicy.ResolveBuildRoot(projectRoot, traversal));
        }

        [Test]
        public void EnsureSafeBuildRoot_WithResolvedProjectChild_ReturnsCanonicalPath()
        {
            Assert.That(
                BuildPathPolicy.EnsureSafeBuildRoot(projectRoot, buildRoot),
                Is.EqualTo(Path.GetFullPath(buildRoot)));
        }

        [TestCase("Library")]
        [TestCase("ProjectSettings")]
        public void EnsureSafeBuildRoot_InsideProtectedProjectDirectory_Throws(
            string protectedDirectory)
        {
            string unsafeRoot = Path.Combine(projectRoot, protectedDirectory, "BuildResults");

            Assert.Throws<InvalidOperationException>(
                () => BuildPathPolicy.EnsureSafeBuildRoot(projectRoot, unsafeRoot));
        }

        [Test]
        public void EnsureLegacyWindowsPathBudget_AtFileBoundary_Succeeds()
        {
            string path = CreateAbsolutePathWithLength(
                BuildPathPolicy.LegacyWindowsMaximumPathCharacters);

            Assert.That(
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(path, "Artifact"),
                Is.EqualTo(Path.GetFullPath(path)));
        }

        [Test]
        public void EnsureLegacyWindowsPathBudget_BeyondFileBoundary_ThrowsActionableError()
        {
            string path = CreateAbsolutePathWithLength(
                BuildPathPolicy.LegacyWindowsMaximumPathCharacters + 1);

            PathTooLongException exception = Assert.Throws<PathTooLongException>(() =>
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(path, "Artifact"));

            StringAssert.Contains("Shorten the repository checkout", exception.Message);
            StringAssert.Contains("maximum=259", exception.Message);
        }

        [Test]
        public void EnsureLegacyWindowsPathBudget_ReservedSuffixIsPartOfBudget()
        {
            const int suffixLength = 8;
            string path = CreateAbsolutePathWithLength(
                BuildPathPolicy.LegacyWindowsMaximumPathCharacters - suffixLength + 1);

            Assert.Throws<PathTooLongException>(() =>
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    path,
                    "Artifact",
                    suffixLength));
        }

        [Test]
        public void EnsureLegacyWindowsDirectoryPathBudget_UsesCreateDirectoryBoundary()
        {
            string accepted = CreateAbsolutePathWithLength(
                BuildPathPolicy.LegacyWindowsMaximumDirectoryPathCharacters);
            string rejected = CreateAbsolutePathWithLength(
                BuildPathPolicy.LegacyWindowsMaximumDirectoryPathCharacters + 1);

            Assert.DoesNotThrow(() =>
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    accepted,
                    "Generated directory"));
            Assert.Throws<PathTooLongException>(() =>
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    rejected,
                    "Generated directory"));
        }

        [Test]
        public void EnsureLegacyWindowsPathBudget_ExtendedNamespace_Throws()
        {
            Assert.Throws<NotSupportedException>(() =>
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    @"\\?\C:\Build\Artifact.bin",
                    "Artifact"));
        }

        [TestCase("CON")]
        [TestCase("con.txt")]
        [TestCase(".Product")]
        [TestCase("Product.")]
        [TestCase(" Product")]
        [TestCase("Product ")]
        [TestCase("Product:Preview")]
        [TestCase("Product/Preview")]
        public void ValidatePortableFileName_WithNonPortableName_Throws(string value)
        {
            Assert.Throws<ArgumentException>(
                () => BuildPathPolicy.ValidatePortableFileName(value, "Product name"));
        }

        [Test]
        public void ValidatePortableFileName_WithUtf8NameAtBudget_Succeeds()
        {
            string value = new string('项', 80);

            Assert.DoesNotThrow(
                () => BuildPathPolicy.ValidatePortableFileName(value, "Product name", 240));
        }

        [Test]
        public void ValidatePortableFileName_WithUtf8NameBeyondBudget_Throws()
        {
            string value = new string('项', 81);

            Assert.Throws<ArgumentException>(
                () => BuildPathPolicy.ValidatePortableFileName(value, "Product name", 240));
        }

        [TestCase("Build/Windows")]
        [TestCase("Build\\Windows")]
        [TestCase("构建/产物")]
        public void ValidatePortableProjectRelativePath_WithPortablePath_Succeeds(string value)
        {
            Assert.DoesNotThrow(
                () => BuildPathPolicy.ValidatePortableProjectRelativePath(value, "Build output root"));
        }

        [TestCase("/Build/Windows")]
        [TestCase("C:\\Build\\Windows")]
        [TestCase("Build//Windows")]
        [TestCase("Build/../Windows")]
        [TestCase("Build/CON")]
        [TestCase("Build/Windows.")]
        public void ValidatePortableProjectRelativePath_WithNonPortablePath_Throws(string value)
        {
            Assert.Throws<ArgumentException>(
                () => BuildPathPolicy.ValidatePortableProjectRelativePath(value, "Build output root"));
        }

        [TestCase("Assets")]
        [TestCase(".git")]
        [TestCase("Packages")]
        [TestCase("ProjectSettings")]
        [TestCase("Library")]
        [TestCase("UserSettings")]
        [TestCase("Temp")]
        [TestCase("Obj")]
        [TestCase("Logs")]
        public void ResolveBuildRoot_InsideProtectedProjectDirectory_Throws(string protectedDirectory)
        {
            string requested = Path.Combine(protectedDirectory, "GeneratedBuild");

            Assert.Throws<InvalidOperationException>(
                () => BuildPathPolicy.ResolveBuildRoot(projectRoot, requested));
        }

        [Test]
        public void ResolveOutputPath_ProjectRelativeOutput_IsNotPrefixedTwice()
        {
            string requested = Path.Combine("Build", "Windows", "Game.exe");

            string resolved = BuildPathPolicy.ResolveOutputPath(
                projectRoot,
                buildRoot,
                requested,
                relativeToBuildRoot: false,
                allowExternalOutput: false);

            Assert.That(
                resolved,
                Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, requested))));
        }

        [Test]
        public void ResolveOutputPath_WithTraversalIntoProtectedDirectory_ThrowsEvenWhenExternalOutputIsAllowed()
        {
            string protectedTarget = Path.Combine(projectRoot, "Assets", "GeneratedBuild");

            Assert.Throws<InvalidOperationException>(() => BuildPathPolicy.ResolveOutputPath(
                projectRoot,
                buildRoot,
                protectedTarget,
                relativeToBuildRoot: false,
                allowExternalOutput: true));
        }

        [Test]
        public void ResolveOutputPath_OutsideBuildRoot_RequiresExplicitGate()
        {
            string externalOutput = Path.Combine(sandboxRunRoot, "external", "deep", "Game.exe");

            Assert.Throws<InvalidOperationException>(() => BuildPathPolicy.ResolveOutputPath(
                projectRoot,
                buildRoot,
                externalOutput,
                relativeToBuildRoot: false,
                allowExternalOutput: false));

            string resolved = BuildPathPolicy.ResolveOutputPath(
                projectRoot,
                buildRoot,
                externalOutput,
                relativeToBuildRoot: false,
                allowExternalOutput: true);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(externalOutput)));
        }

        [TestCase("Build/Windows/../Linux/Game.exe")]
        [TestCase("Build/Windows/CON/Game.exe")]
        [TestCase("Build/Windows/Game:Preview.exe")]
        public void ResolveOutputPath_WithNonPortableRelativePath_Throws(string requested)
        {
            Assert.Catch<Exception>(() => BuildPathPolicy.ResolveOutputPath(
                projectRoot,
                buildRoot,
                requested,
                relativeToBuildRoot: false,
                allowExternalOutput: false));
        }

        [Test]
        public void ResolveOutputPath_WithReservedExternalSegment_Throws()
        {
            string requested = Path.Combine(sandboxRunRoot, "external", "CON", "Game.exe");

            Assert.Throws<ArgumentException>(() => BuildPathPolicy.ResolveOutputPath(
                projectRoot,
                buildRoot,
                requested,
                relativeToBuildRoot: false,
                allowExternalOutput: true));
        }

        [Test]
        public void ResolveOutputDirectory_FileArtifact_ReturnsDedicatedParent()
        {
            string outputPath = Path.Combine(buildRoot, "Windows", "Release", "Game.exe");

            string resolved = BuildPathPolicy.ResolveOutputDirectory(
                projectRoot,
                buildRoot,
                outputPath,
                outputIsFolder: false,
                allowExternalOutput: false);

            Assert.That(resolved, Is.EqualTo(Path.GetDirectoryName(outputPath)));
        }

        [Test]
        public void ResolveOutputDirectory_FolderArtifact_ReturnsArtifactDirectory()
        {
            string outputPath = Path.Combine(buildRoot, "WebGL", "Release", "Game");

            string resolved = BuildPathPolicy.ResolveOutputDirectory(
                projectRoot,
                buildRoot,
                outputPath,
                outputIsFolder: true,
                allowExternalOutput: false);

            Assert.That(resolved, Is.EqualTo(outputPath));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResolveOutputDirectory_SharedBuildRoot_Throws(bool allowExternalOutput)
        {
            string outputPath = Path.Combine(buildRoot, "Game.exe");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildPathPolicy.ResolveOutputDirectory(
                    projectRoot,
                    buildRoot,
                    outputPath,
                    outputIsFolder: false,
                    allowExternalOutput: allowExternalOutput));

            StringAssert.Contains("dedicated directory", exception.Message);
        }

        [Test]
        public void ResolveGeneratedAssetsDirectory_ValidChild_ReturnsCanonicalPath()
        {
            string resolved = BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                projectRoot,
                "Assets/Generated/HybridCLR");

            Assert.That(
                resolved,
                Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, "Assets", "Generated", "HybridCLR"))));
        }

        [TestCase("Assets")]
        [TestCase("Assets/../Packages/Generated")]
        [TestCase("Packages/Generated")]
        [TestCase("AssetsX/Generated")]
        public void ResolveGeneratedAssetsDirectory_UnsafeLocation_Throws(string configuredPath)
        {
            Assert.That(
                () => BuildPathPolicy.ResolveGeneratedAssetsDirectory(projectRoot, configuredPath),
                Throws.InstanceOf<Exception>());
        }

        [Test]
        public void ResolveGeneratedAssetsDirectory_RootedPath_Throws()
        {
            string rootedPath = Path.Combine(projectRoot, "Assets", "Generated");

            Assert.Throws<ArgumentException>(
                () => BuildPathPolicy.ResolveGeneratedAssetsDirectory(projectRoot, rootedPath));
        }

        [Test]
        public void ResolveGeneratedAssetsDirectory_NestedReparsePoint_ThrowsWithoutTouchingTarget()
        {
            string targetDirectory = Path.Combine(sandboxRunRoot, "generated-output-target");
            string linkDirectory = Path.Combine(projectRoot, "Assets", "GeneratedLink");
            string sentinelPath = Path.Combine(targetDirectory, "sentinel.txt");
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(sentinelPath, "preserve");

            try
            {
                CreateDirectoryLink(linkDirectory, targetDirectory);
                Assert.Throws<InvalidOperationException>(() => BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                    projectRoot,
                    "Assets/GeneratedLink/HybridCLR"));
                Assert.That(File.Exists(sentinelPath), Is.True);
            }
            finally
            {
                DeleteDirectoryLink(linkDirectory);
            }
        }

        [Test]
        public void EnsureSafeReadableFile_InsideApprovedRoot_ReturnsCanonicalPath()
        {
            string sourceRoot = Path.Combine(sandboxRunRoot, "source-artifacts");
            string sourceFile = Path.Combine(sourceRoot, "nested", "artifact.bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFile));
            File.WriteAllText(sourceFile, "artifact");

            string resolved = BuildPathPolicy.EnsureSafeReadableFile(sourceRoot, sourceFile);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(sourceFile)));
        }

        [Test]
        public void EnsureSafeReadableFile_OutsideApprovedRoot_ThrowsWithoutTouchingFile()
        {
            string sourceRoot = Path.Combine(sandboxRunRoot, "approved-source");
            string outsideFile = Path.Combine(sandboxRunRoot, "outside-source", "artifact.bundle");
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(outsideFile));
            File.WriteAllText(outsideFile, "preserve");

            Assert.Throws<InvalidOperationException>(
                () => BuildPathPolicy.EnsureSafeReadableFile(sourceRoot, outsideFile));
            Assert.That(File.ReadAllText(outsideFile), Is.EqualTo("preserve"));
        }

        [Test]
        public void ResolvePublicationSourceRoot_ProjectBuildDirectory_ReturnsCanonicalPath()
        {
            string resolved = BuildPathPolicy.ResolvePublicationSourceRoot(
                projectRoot,
                "ServerData/Windows",
                allowExternalSource: false);

            Assert.That(
                resolved,
                Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, "ServerData", "Windows"))));
        }

        [TestCase("Assets/RemoteData")]
        [TestCase("Library/RemoteData")]
        [TestCase("Packages/RemoteData")]
        public void ResolvePublicationSourceRoot_ProtectedProjectDirectory_Throws(string configuredPath)
        {
            Assert.Throws<InvalidOperationException>(() =>
                BuildPathPolicy.ResolvePublicationSourceRoot(
                    projectRoot,
                    configuredPath,
                    allowExternalSource: false));
        }

        [Test]
        public void ResolvePublicationSourceRoot_ExternalDirectory_RequiresExplicitGate()
        {
            string externalRoot = Path.Combine(sandboxRunRoot, "external-source", "Addressables");

            Assert.Throws<InvalidOperationException>(() =>
                BuildPathPolicy.ResolvePublicationSourceRoot(
                    projectRoot,
                    externalRoot,
                    allowExternalSource: false));

            string resolved = BuildPathPolicy.ResolvePublicationSourceRoot(
                projectRoot,
                externalRoot,
                allowExternalSource: true);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(externalRoot)));
        }

        [Test]
        public void AddressablesPublication_SourceCasingAliasOfDestination_ReportsOverlap()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<AddressablesBuildConfig>();
            try
            {
                config.copyToOutputDirectory = true;
                config.buildOutputDirectory = "Build/AddressablesContent";
                config.additionalPublicationRoots.Add(new AddressablesPublicationRoot
                {
                    sourceDirectory = "bUILD/aDDRESSABLEScONTENT/Source",
                    destinationFolder = "AdditionalContent"
                });

                Type builderType = typeof(AddressablesBuildConfig).Assembly.GetType(
                    "Build.Pipeline.Editor.AddressablesBuilder",
                    throwOnError: true);
                MethodInfo validateMethod = builderType.GetMethod(
                    "ValidatePublicationConfiguration",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(validateMethod, Is.Not.Null);

                string error = (string)validateMethod.Invoke(null, new object[] { config, projectRoot });
                StringAssert.Contains("must not overlap", error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ResolvePublicationSourceRoot_NestedReparsePoint_ThrowsWithoutTouchingTarget()
        {
            string targetDirectory = Path.Combine(sandboxRunRoot, "publication-source-target");
            string linkDirectory = Path.Combine(projectRoot, "ServerLink");
            string sentinelPath = Path.Combine(targetDirectory, "sentinel.txt");
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(sentinelPath, "preserve");

            try
            {
                CreateDirectoryLink(linkDirectory, targetDirectory);
                Assert.Throws<InvalidOperationException>(() =>
                    BuildPathPolicy.ResolvePublicationSourceRoot(
                        projectRoot,
                        "ServerLink/Windows",
                        allowExternalSource: false));
                Assert.That(File.Exists(sentinelPath), Is.True);
            }
            finally
            {
                DeleteDirectoryLink(linkDirectory);
            }
        }

        [Test]
        public void EnsureSafeDeleteTarget_OutsideBuildRoot_DoesNotTouchSentinelAndRequiresGate()
        {
            string externalDirectory = Path.Combine(sandboxRunRoot, "external", "deep", "output");
            string sentinelPath = Path.Combine(externalDirectory, "sentinel.txt");
            Directory.CreateDirectory(externalDirectory);
            File.WriteAllText(sentinelPath, "preserve");

            Assert.Throws<InvalidOperationException>(() => BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                externalDirectory,
                buildRoot,
                allowExternalOutput: false));
            Assert.That(File.Exists(sentinelPath), Is.True);

            Assert.DoesNotThrow(() => BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                externalDirectory,
                buildRoot,
                allowExternalOutput: true));
            Assert.That(File.Exists(sentinelPath), Is.True);
        }

        [Test]
        public void EnsureSafeDeleteTarget_ProjectRootAndVolumeRoot_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                projectRoot,
                buildRoot,
                allowExternalOutput: true));

            string volumeRoot = Path.GetPathRoot(projectRoot);
            Assert.Throws<InvalidOperationException>(() => BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                volumeRoot,
                buildRoot,
                allowExternalOutput: true));
        }

        [Test]
        public void EnsureSafeDeleteTarget_ProjectRootCasingAlias_Throws()
        {
            string projectAlias = Path.Combine(
                Path.GetDirectoryName(projectRoot),
                "uNITYpROJECT");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildPathPolicy.EnsureSafeDeleteTarget(
                    projectRoot,
                    projectAlias,
                    buildRoot,
                    allowExternalOutput: true));

            StringAssert.Contains("Unity project root", exception.Message);
        }

        [Test]
        public void EnsureSafeDeleteTarget_ProtectedDirectoryCasingAlias_Throws()
        {
            string protectedAlias = Path.Combine(projectRoot, "aSSETS", "GeneratedBuild");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildPathPolicy.EnsureSafeDeleteTarget(
                    projectRoot,
                    protectedAlias,
                    buildRoot,
                    allowExternalOutput: true));

            StringAssert.Contains("protected Unity project data", exception.Message);
        }

        [Test]
        public void EnsureSafeDeleteTarget_ApprovedBuildRootCasingAlias_Throws()
        {
            string buildRootAlias = Path.Combine(
                Path.GetDirectoryName(buildRoot),
                "bUILD");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildPathPolicy.EnsureSafeDeleteTarget(
                    projectRoot,
                    buildRootAlias,
                    buildRoot,
                    allowExternalOutput: true));

            StringAssert.Contains("dedicated directory", exception.Message);
        }

        [Test]
        public void EnsureSafeDeleteTarget_ProjectAncestor_Throws()
        {
            string projectAncestor = Path.GetDirectoryName(Path.GetDirectoryName(projectRoot));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildPathPolicy.EnsureSafeDeleteTarget(
                    projectRoot,
                    projectAncestor,
                    buildRoot,
                    allowExternalOutput: true));

            StringAssert.Contains("ancestor directories", exception.Message);
        }

        [Test]
        public void EnsureSafeDeleteTarget_TopLevelVolumeEntry_Throws()
        {
            string volumeRoot = Path.GetPathRoot(projectRoot);
            string topLevelEntry = Path.Combine(volumeRoot, "UnityStarterBuildPipelineTopLevelTest");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPathPolicy.EnsureSafeDeleteTarget(
                    projectRoot,
                    topLevelEntry,
                    buildRoot,
                    allowExternalOutput: true));

            StringAssert.Contains("top-level volume entry", exception.Message);
        }

        [Test]
        public void EnsureSafeDeleteTarget_WellKnownSystemDirectory_Throws()
        {
            string protectedDirectory = FindWellKnownSystemDirectoryOutsideProject();
            Assert.That(
                protectedDirectory,
                Is.Not.Empty,
                "The test environment must expose a nested well-known system directory outside the synthetic project.");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BuildPathPolicy.EnsureSafeDeleteTarget(
                    projectRoot,
                    protectedDirectory,
                    buildRoot,
                    allowExternalOutput: true));

            StringAssert.Contains("protected operating-system directory", exception.Message);
        }

        private string FindWellKnownSystemDirectoryOutsideProject()
        {
            Environment.SpecialFolder[] candidates =
            {
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.UserProfile
            };

            foreach (Environment.SpecialFolder candidate in candidates)
            {
                string path = Environment.GetFolderPath(candidate);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(path);
                string volumeRoot = Path.GetPathRoot(fullPath);
                string parent = Path.GetDirectoryName(fullPath);
                bool isProjectOrAncestor = PathsEqual(fullPath, projectRoot)
                    || BuildPathPolicy.IsStrictDescendant(fullPath, projectRoot);
                bool isTopLevelVolumeEntry = string.IsNullOrEmpty(parent)
                    || PathsEqual(parent, volumeRoot);
                if (!isProjectOrAncestor && !isTopLevelVolumeEntry)
                {
                    return fullPath;
                }
            }

            return string.Empty;
        }

        [Test]
        public void EnsureSafeDeleteTarget_ExistingReparsePoint_ThrowsWithoutDeletingTarget()
        {
            string targetDirectory = Path.Combine(sandboxRunRoot, "deep", "reparse-target");
            string reparseDirectory = Path.Combine(sandboxRunRoot, "deep", "reparse-link");
            string sentinelPath = Path.Combine(targetDirectory, "sentinel.txt");
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(sentinelPath, "preserve");

            try
            {
                CreateDirectoryLink(reparseDirectory, targetDirectory);
                Assert.That(
                    File.GetAttributes(reparseDirectory) & FileAttributes.ReparsePoint,
                    Is.EqualTo(FileAttributes.ReparsePoint));

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => BuildPathPolicy.EnsureSafeDeleteTarget(
                        projectRoot,
                        reparseDirectory,
                        buildRoot,
                        allowExternalOutput: true));

                StringAssert.Contains("reparse-point", exception.Message);
                Assert.That(File.Exists(sentinelPath), Is.True);
            }
            finally
            {
                DeleteDirectoryLink(reparseDirectory);
            }
        }

        [Test]
        public void EnsureSafeDeleteDirectoryTree_NestedReparsePoint_ThrowsWithoutTouchingTarget()
        {
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            string externalTarget = Path.Combine(sandboxRunRoot, "external-delete-sentinel");
            string nestedLink = Path.Combine(outputDirectory, "nested-link");
            string sentinelPath = Path.Combine(externalTarget, "sentinel.txt");
            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(externalTarget);
            File.WriteAllText(sentinelPath, "preserve");

            try
            {
                CreateDirectoryLink(nestedLink, externalTarget);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    BuildPathPolicy.EnsureSafeDeleteDirectoryTree(
                        projectRoot,
                        outputDirectory,
                        buildRoot,
                        allowExternalOutput: false));

                StringAssert.Contains("reparse-point entry", exception.Message);
                Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo("preserve"));
            }
            finally
            {
                DeleteDirectoryLink(nestedLink);
            }
        }

        private void DeleteSandboxRunRoot()
        {
            if (string.IsNullOrWhiteSpace(sandboxBaseRoot)
                || string.IsNullOrWhiteSpace(sandboxRunRoot)
                || !Directory.Exists(sandboxRunRoot))
            {
                return;
            }

            string safeBase = Path.GetFullPath(sandboxBaseRoot);
            string candidate = Path.GetFullPath(sandboxRunRoot);
            string candidateParent = Path.GetDirectoryName(candidate);
            string candidateName = Path.GetFileName(candidate);
            if (!PathsEqual(safeBase, candidateParent)
                || !candidateName.StartsWith("run-", StringComparison.Ordinal)
                || candidateName.Length != "run-".Length + 32)
            {
                throw new InvalidOperationException(
                    $"Refusing to clean an unexpected test directory: '{candidate}'.");
            }

            Directory.Delete(candidate, true);
            TryDeleteEmptyDirectory(safeBase);
        }

        private static bool PathsEqual(string left, string right)
        {
            StringComparison comparison = Environment.OSVersion.Platform == PlatformID.Unix
                || Environment.OSVersion.Platform == PlatformID.MacOSX
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;

            return string.Equals(
                TrimTrailingDirectorySeparators(left),
                TrimTrailingDirectorySeparators(right),
                comparison);
        }

        private string CreateAbsolutePathWithLength(int characterCount)
        {
            string prefix = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            int remaining = characterCount - prefix.Length;
            if (remaining <= 0)
            {
                throw new InvalidOperationException(
                    $"The test sandbox prefix is too long for a {characterCount}-character fixture.");
            }

            return prefix + new string('p', remaining);
        }

        private static string TrimTrailingDirectorySeparators(string path)
        {
            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                {
                    Directory.Delete(path, false);
                }
            }
            catch (IOException)
            {
                // Another parallel fixture may have created its own sandbox.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup remains best-effort after the uniquely owned run directory is removed.
            }
        }

        private static void CreateDirectoryLink(string linkPath, string targetPath)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            var startInfo = new ProcessStartInfo
            {
                FileName = windows ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : "/bin/ln",
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
                Directory.Delete(linkPath, false);
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
