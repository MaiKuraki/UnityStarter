using System;
using System.Collections.Generic;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class PortableAssetPathAuditTests
    {
        /// <summary>
        /// A CJK name written with escapes so this file stays pure ASCII and the test
        /// states the exact code points it rejects.
        /// </summary>
        private const string CjkName = "\u6D4B\u8BD5.wav";

        private const string CjkNameSecond = "b\u6D4B\u8BD5.txt";

        private const string CjkNameFirst = "a\u6D4B\u8BD5.txt";

        private string sandboxRoot;
        private string projectRoot;
        private string assetsRoot;
        private string streamingAssetsRoot;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                "PortablePathAuditTests",
                "run-" + Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRoot, "UnityProject");
            assetsRoot = Path.Combine(projectRoot, "Assets");
            streamingAssetsRoot = Path.Combine(assetsRoot, "StreamingAssets");

            Directory.CreateDirectory(streamingAssetsRoot);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(sandboxRoot))
                {
                    Directory.Delete(sandboxRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // Leaving a temp sandbox behind must never fail the run.
            }
        }

        // ---------- Segment rules (pure, no file system) ----------

        [TestCase("audio_01.wav")]
        [TestCase("Audio - Copy (2).ogg")]
        [TestCase("a.b.c")]
        public void ValidateSegment_AcceptsPortableAsciiNames(string segment)
        {
            Assert.That(
                PortableAssetPathPolicy.ValidateSegment(segment, rejectNonAscii: true).HasViolation,
                Is.False);
        }

        [Test]
        public void ValidateSegment_RejectsNonAsciiWhenStrict()
        {
            PortablePathSegmentViolation violation =
                PortableAssetPathPolicy.ValidateSegment(CjkName, rejectNonAscii: true);

            Assert.That(violation.Kind, Is.EqualTo(PortablePathViolationKind.NonAsciiCharacter));
            Assert.That(violation.OffendingText, Is.Not.Empty);
        }

        [Test]
        public void ValidateSegment_AllowsNonAsciiWhenPolicyPermitsIt()
        {
            Assert.That(
                PortableAssetPathPolicy.ValidateSegment(CjkName, rejectNonAscii: false).HasViolation,
                Is.False);
        }

        [TestCase("<")]
        [TestCase(">")]
        [TestCase(":")]
        [TestCase("\"")]
        [TestCase("|")]
        [TestCase("?")]
        [TestCase("*")]
        public void ValidateSegment_RejectsInvalidCharacters(string invalid)
        {
            PortablePathSegmentViolation violation =
                PortableAssetPathPolicy.ValidateSegment(
                    "name" + invalid + "part",
                    rejectNonAscii: true);

            Assert.That(violation.Kind, Is.EqualTo(PortablePathViolationKind.InvalidCharacter));
        }

        [TestCase("CON")]
        [TestCase("con.txt")]
        [TestCase("NUL.bin")]
        [TestCase("COM1.resource")]
        [TestCase("LPT9")]
        public void ValidateSegment_RejectsReservedDeviceNames(string reserved)
        {
            PortablePathSegmentViolation violation =
                PortableAssetPathPolicy.ValidateSegment(reserved, rejectNonAscii: true);

            Assert.That(violation.Kind, Is.EqualTo(PortablePathViolationKind.ReservedDeviceName));
        }

        [TestCase("name.")]
        [TestCase("name ")]
        public void ValidateSegment_RejectsTrailingDotOrSpace(string segment)
        {
            PortablePathSegmentViolation violation =
                PortableAssetPathPolicy.ValidateSegment(segment, rejectNonAscii: true);

            Assert.That(violation.Kind, Is.EqualTo(PortablePathViolationKind.TrailingDotOrSpace));
        }

        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        public void ValidateSegment_RejectsEmptyAndNavigationSegments(string segment)
        {
            Assert.That(
                PortableAssetPathPolicy.ValidateSegment(segment, rejectNonAscii: true).Kind,
                Is.EqualTo(PortablePathViolationKind.EmptySegment));
        }

        [Test]
        public void ValidateSegment_RejectsControlCharacter()
        {
            Assert.That(
                PortableAssetPathPolicy.ValidateSegment(
                    "name" + (char)7 + ".txt",
                    rejectNonAscii: true).Kind,
                Is.EqualTo(PortablePathViolationKind.ControlCharacter));
        }

        [Test]
        public void ValidateSegment_RejectsUnpairedSurrogate()
        {
            Assert.That(
                PortableAssetPathPolicy.ValidateSegment(
                    "name" + '\uD83D' + ".txt",
                    rejectNonAscii: false).Kind,
                Is.EqualTo(PortablePathViolationKind.InvalidUnicode));
        }

        [Test]
        public void ValidateSegment_RejectsOversizedUtf8Segment()
        {
            string segment = new string('n', 260);

            PortablePathSegmentViolation violation =
                PortableAssetPathPolicy.ValidateSegment(segment, rejectNonAscii: true);

            Assert.That(violation.Kind, Is.EqualTo(PortablePathViolationKind.SegmentTooLong));
        }

        [Test]
        public void IsPureAscii_RejectsNonAsciiAndAcceptsControlFreeAscii()
        {
            Assert.That(PortableAssetPathPolicy.IsPureAscii("Audio_01.wav"), Is.True);
            Assert.That(PortableAssetPathPolicy.IsPureAscii(CjkName), Is.False);
        }

        // ---------- Bounded audit over a real tree ----------

        [Test]
        public void AuditStreamingAssets_ReportsNonAsciiFileAsError()
        {
            CreateEmptyFile(Path.Combine(streamingAssetsRoot, CjkName));

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.AuditStreamingAssets(projectRoot);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.ErrorMessages[0], Does.Contain("StreamingAssets"));
            Assert.That(result.ErrorMessages[0], Does.Contain("non-ASCII"));
            Assert.That(
                result.Findings[0].Kind,
                Is.EqualTo(PortablePathViolationKind.NonAsciiCharacter));
        }

        [Test]
        public void AuditStreamingAssets_ReportsNonAsciiDirectoryName()
        {
            string directory = Path.Combine(streamingAssetsRoot, CjkName);
            Directory.CreateDirectory(directory);
            CreateEmptyFile(Path.Combine(directory, "inside.wav"));

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.AuditStreamingAssets(projectRoot);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.Findings[0].Segment, Is.EqualTo(CjkName));
        }

        [Test]
        public void AuditStreamingAssets_SkipsNamesUnityIgnores()
        {
            CreateEmptyFile(Path.Combine(streamingAssetsRoot, ".hidden-" + CjkName));
            Directory.CreateDirectory(Path.Combine(streamingAssetsRoot, "Samples~"));
            CreateEmptyFile(Path.Combine(
                streamingAssetsRoot,
                "Samples~",
                CjkName));

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.AuditStreamingAssets(projectRoot);

            Assert.That(result.ErrorCount, Is.Zero);
            Assert.That(result.IgnoredEntryCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void AuditStreamingAssets_MissingRoot_ReturnsEmptyResult()
        {
            Directory.Delete(streamingAssetsRoot);

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.AuditStreamingAssets(projectRoot);

            Assert.That(result.ErrorCount, Is.Zero);
            Assert.That(result.ScannedEntryCount, Is.Zero);
        }

        [Test]
        public void Audit_ExcludedPrefix_SkipsSubtree()
        {
            Directory.CreateDirectory(Path.Combine(assetsRoot, "Skip"));
            Directory.CreateDirectory(Path.Combine(assetsRoot, "Keep"));
            CreateEmptyFile(Path.Combine(assetsRoot, "Skip", CjkName));
            CreateEmptyFile(Path.Combine(assetsRoot, "Keep", CjkName));

            var scopes = new[]
            {
                new PortableAssetPathAuditScope(
                    "Assets",
                    assetsRoot,
                    rejectNonAscii: true,
                    enforceWin32PathBudget: false,
                    isError: true,
                    nonAsciiIsError: true,
                    excludedRelativePrefixes: new[] { "Skip" })
            };

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.Findings[0].RelativePath, Is.EqualTo("Keep/" + CjkName));
        }

        [Test]
        public void Audit_DeclaredScopeIsAlwaysScanned()
        {
            CreateEmptyFile(Path.Combine(streamingAssetsRoot, CjkName));

            var scopes = new[]
            {
                new PortableAssetPathAuditScope(
                    "StreamingAssets",
                    streamingAssetsRoot,
                    rejectNonAscii: true,
                    enforceWin32PathBudget: false,
                    isError: true,
                    nonAsciiIsError: true)
            };

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ScannedEntryCount, Is.EqualTo(1));
            Assert.That(result.ErrorCount, Is.EqualTo(1));
        }

        [Test]
        public void Audit_NonAsciiSeverityDowngrade_ReportsWarningOnly()
        {
            CreateEmptyFile(Path.Combine(assetsRoot, CjkName));

            var scopes = new[]
            {
                new PortableAssetPathAuditScope(
                    "Assets",
                    assetsRoot,
                    rejectNonAscii: true,
                    enforceWin32PathBudget: false,
                    isError: true,
                    nonAsciiIsError: false)
            };

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.Zero);
            Assert.That(result.WarningMessages.Count, Is.EqualTo(1));
            Assert.That(result.Findings[0].IsError, Is.False);
        }

        [Test]
        public void Audit_FindingBudget_CapsDetailsAndCountsTheRest()
        {
            Directory.CreateDirectory(assetsRoot);
            for (int index = 0; index < 4; index++)
            {
                CreateEmptyFile(Path.Combine(assetsRoot, index + "-" + CjkName));
            }

            var scopes = new[] { CreateAssetsScope() };

            PortableAssetPathAuditResult result = PortableAssetPathAudit.Audit(
                projectRoot,
                scopes,
                maximumEntryCount: PortableAssetPathAudit.DefaultMaximumEntryCount,
                maximumFindingCount: 2);

            Assert.That(result.ErrorCount, Is.EqualTo(2));
            Assert.That(result.OmittedFindingCount, Is.EqualTo(2));
            Assert.That(result.DescribeSummary(), Does.Contain("not reported"));
        }

        [Test]
        public void Audit_EntryBudget_StopsAndFlagsPartialResult()
        {
            Directory.CreateDirectory(assetsRoot);
            for (int index = 0; index < 6; index++)
            {
                CreateEmptyFile(Path.Combine(assetsRoot, "file-" + index + ".txt"));
            }

            var scopes = new[] { CreateAssetsScope() };

            PortableAssetPathAuditResult result = PortableAssetPathAudit.Audit(
                projectRoot,
                scopes,
                maximumEntryCount: 3);

            Assert.That(result.EntryBudgetExceeded, Is.True);
            Assert.That(result.ScannedEntryCount, Is.EqualTo(3));
            Assert.That(result.DescribeSummary(), Does.Contain("partial"));
        }

        /// <summary>
        /// A partial scan must be distinguishable from a clean one. A gate that cannot
        /// tell them apart passes a build on content it never looked at.
        /// </summary>
        [Test]
        public void Audit_CompleteScan_ReportsCompleteCoverage()
        {
            Directory.CreateDirectory(assetsRoot);
            CreateEmptyFile(Path.Combine(assetsRoot, "file.txt"));

            PortableAssetPathAuditResult result = PortableAssetPathAudit.Audit(
                projectRoot,
                new[] { CreateAssetsScope() });

            Assert.That(result.IsCoverageIncomplete, Is.False);
            Assert.That(
                result.DescribeIncompleteCoverage("maximumEntryCount"),
                Is.Null);
        }

        [Test]
        public void Audit_ExhaustedEntryBudget_ReportsActionableIncompleteCoverage()
        {
            Directory.CreateDirectory(assetsRoot);
            for (int index = 0; index < 6; index++)
            {
                CreateEmptyFile(Path.Combine(assetsRoot, "file-" + index + ".txt"));
            }

            PortableAssetPathAuditResult result = PortableAssetPathAudit.Audit(
                projectRoot,
                new[] { CreateAssetsScope() },
                maximumEntryCount: 2);

            Assert.That(result.IsCoverageIncomplete, Is.True);

            string coverageError = result.DescribeIncompleteCoverage("maximumEntryCount");
            Assert.That(coverageError, Is.Not.Null);
            Assert.That(coverageError, Does.Contain("coverage is incomplete"));
            Assert.That(
                coverageError,
                Does.Contain("maximumEntryCount"),
                "the error must name the setting that resolves it");
        }

        /// <summary>
        /// The OS-level read failure cannot be forced portably, so the decision and the
        /// message are locked through the result the walker produces for it.
        /// </summary>
        [Test]
        public void Audit_UnreadableDirectory_ReportsIncompleteCoverage()
        {
            var result = new PortableAssetPathAuditResult(
                Array.Empty<PortableAssetPathFinding>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                scannedEntryCount: 5,
                ignoredEntryCount: 0,
                reparsePointCount: 0,
                unreadableDirectoryCount: 1,
                firstUnreadableDirectory: "Assets/Locked",
                omittedFindingCount: 0,
                entryBudgetExceeded: false);

            Assert.That(result.IsCoverageIncomplete, Is.True);
            Assert.That(result.DescribeSummary(), Does.Contain("could not be read"));

            string coverageError = result.DescribeIncompleteCoverage("maximumEntryCount");
            Assert.That(coverageError, Is.Not.Null);
            Assert.That(coverageError, Does.Contain("Assets/Locked"));
        }

        [Test]
        public void PlanRequest_NegativeEntryBudget_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetPathAuditPlanRequest(projectRoot, maximumEntryCount: -1));

            Assert.That(
                new AssetPathAuditPlanRequest(projectRoot, maximumEntryCount: 500000)
                    .MaximumEntryCount,
                Is.EqualTo(500000));
        }

        [Test]
        public void Audit_ReportsFindingsInDeterministicOrder()
        {
            Directory.CreateDirectory(assetsRoot);
            CreateEmptyFile(Path.Combine(assetsRoot, CjkNameSecond));
            CreateEmptyFile(Path.Combine(assetsRoot, CjkNameFirst));

            PortableAssetPathAuditResult first =
                PortableAssetPathAudit.Audit(projectRoot, new[] { CreateAssetsScope() });
            PortableAssetPathAuditResult second =
                PortableAssetPathAudit.Audit(projectRoot, new[] { CreateAssetsScope() });

            Assert.That(first.ErrorCount, Is.EqualTo(2));
            Assert.That(first.Findings[0].RelativePath, Is.EqualTo(CjkNameFirst));
            Assert.That(first.Findings[1].RelativePath, Is.EqualTo(CjkNameSecond));
            Assert.That(
                second.Findings[0].RelativePath,
                Is.EqualTo(first.Findings[0].RelativePath));
        }

        [Test]
        public void Audit_ScopeOutsideProject_Throws()
        {
            string outside = Path.Combine(sandboxRoot, "outside");
            Directory.CreateDirectory(outside);

            var scopes = new[]
            {
                new PortableAssetPathAuditScope(
                    "Outside",
                    outside,
                    rejectNonAscii: true,
                    enforceWin32PathBudget: false,
                    isError: true)
            };

            Assert.Throws<ArgumentException>(() =>
                PortableAssetPathAudit.Audit(projectRoot, scopes));
        }

        [Test]
        public void Audit_PathBeyondWin32Budget_ReportsPathTooLong()
        {
            // Creating a >259-character path depends on the host's long-path support.
            // Where the file system refuses, the rule cannot be exercised end to end.
            string deep = Path.Combine(assetsRoot, new string('d', 180));
            string file = Path.Combine(deep, new string('f', 80) + ".txt");
            if (file.Length <= BuildPathPolicy.Win32MaxPathCharacters)
            {
                Assert.Ignore("The sandbox root is too short to exceed the Win32 path budget.");
            }

            try
            {
                Directory.CreateDirectory(deep);
                CreateEmptyFile(file);
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is ArgumentException
                || exception is NotSupportedException)
            {
                Assert.Ignore("This host does not create paths beyond MAX_PATH.");
            }

            var scopes = new[] { CreateAssetsScope() };

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(
                HasPathTooLongFinding(result),
                Is.True);
        }

        // ---------- Scope plan ----------

        [Test]
        public void Plan_DefaultRequest_ScansTheAssetTreeAndStreamingAssetsStrictly()
        {
            CreateEmptyFile(Path.Combine(streamingAssetsRoot, CjkName));

            var errors = new List<string>();
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(
                    new AssetPathAuditPlanRequest(projectRoot),
                    errors);

            Assert.That(errors, Is.Empty);
            Assert.That(scopes.Count, Is.EqualTo(2));

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(
                result.Findings[0].ScopeName,
                Is.EqualTo(AssetPathAuditPlan.StreamingAssetsScopeName));
        }

        /// <summary>
        /// Regression: the asset-tree scope must skip StreamingAssets, and the strict
        /// StreamingAssets scope must keep scanning. Sharing one project-relative
        /// exclusion list between the two scopes used to drop the strict scope
        /// entirely, which silently removed the check instead of reporting it.
        /// </summary>
        [Test]
        public void Plan_AssetTreeSkipsStreamingAssetsWithoutDisablingTheStrictScope()
        {
            CreateEmptyFile(Path.Combine(streamingAssetsRoot, CjkName));

            var errors = new List<string>();
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(
                    new AssetPathAuditPlanRequest(projectRoot),
                    errors);

            PortableAssetPathAuditScope treeScope = null;
            PortableAssetPathAuditScope streamingScope = null;
            for (int index = 0; index < scopes.Count; index++)
            {
                if (string.Equals(
                        scopes[index].DisplayName,
                        AssetPathAuditPlan.ProjectAssetTreeScopeName,
                        StringComparison.Ordinal))
                {
                    treeScope = scopes[index];
                }
                else if (string.Equals(
                             scopes[index].DisplayName,
                             AssetPathAuditPlan.StreamingAssetsScopeName,
                             StringComparison.Ordinal))
                {
                    streamingScope = scopes[index];
                }
            }

            Assert.That(treeScope, Is.Not.Null);
            Assert.That(streamingScope, Is.Not.Null);
            Assert.That(treeScope.ExcludedRelativePrefixes, Does.Contain("StreamingAssets"));
            Assert.That(streamingScope.ExcludedRelativePrefixes, Is.Empty);
            Assert.That(streamingScope.NonAsciiIsError, Is.True);
        }

        [Test]
        public void Plan_ExcludingTheStreamingAssetsRoot_IsRejectedAndTheScopeStillScans()
        {
            CreateEmptyFile(Path.Combine(streamingAssetsRoot, CjkName));

            var errors = new List<string>();
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(
                    new AssetPathAuditPlanRequest(
                        projectRoot,
                        excludedRelativePrefixes: new[] { "Assets/StreamingAssets" }),
                    errors);

            Assert.That(errors, Has.Some.Contains("cannot be excluded"));

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
        }

        [Test]
        public void Plan_ExcludingAStreamingAssetsSubdirectory_AppliesOnlyToThatScope()
        {
            string generated = Path.Combine(streamingAssetsRoot, "Generated");
            Directory.CreateDirectory(generated);
            CreateEmptyFile(Path.Combine(generated, CjkName));
            CreateEmptyFile(Path.Combine(assetsRoot, CjkName));

            var errors = new List<string>();
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(
                    new AssetPathAuditPlanRequest(
                        projectRoot,
                        excludedRelativePrefixes: new[] { "Assets/StreamingAssets/Generated" }),
                    errors);

            Assert.That(errors, Is.Empty);

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.Zero);
            Assert.That(result.WarningMessages.Count, Is.EqualTo(1));
            Assert.That(result.WarningMessages[0], Does.Contain(CjkName));
        }

        [Test]
        public void Plan_ExcludingAnAssetTreeSubdirectory_IsTranslatedToThatScope()
        {
            string vendored = Path.Combine(assetsRoot, "Vendored");
            Directory.CreateDirectory(vendored);
            CreateEmptyFile(Path.Combine(vendored, CjkName));

            var errors = new List<string>();
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(
                    new AssetPathAuditPlanRequest(
                        projectRoot,
                        excludedRelativePrefixes: new[] { "Assets/Vendored" }),
                    errors);

            Assert.That(errors, Is.Empty);

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.Zero);
            Assert.That(result.WarningMessages, Is.Empty);
        }

        [Test]
        public void Plan_AdditionalRootExcludedWholesale_IsNotScanned()
        {
            string extra = Path.Combine(assetsRoot, "Extra");
            Directory.CreateDirectory(extra);
            CreateEmptyFile(Path.Combine(extra, CjkName));

            var errors = new List<string>();
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(
                    new AssetPathAuditPlanRequest(
                        projectRoot,
                        auditProjectAssetTree: false,
                        additionalRelativeRoots: new[] { "Assets/Extra" },
                        excludedRelativePrefixes: new[] { "Assets/Extra" }),
                    errors);

            Assert.That(scopes.Count, Is.EqualTo(1));
            Assert.That(
                scopes[0].DisplayName,
                Is.EqualTo(AssetPathAuditPlan.StreamingAssetsScopeName));

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.Zero);
        }

        [Test]
        public void Plan_AdditionalRoot_IsScannedStrictly()
        {
            string extra = Path.Combine(assetsRoot, "Extra");
            Directory.CreateDirectory(extra);
            CreateEmptyFile(Path.Combine(extra, CjkName));

            var errors = new List<string>();
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(
                    new AssetPathAuditPlanRequest(
                        projectRoot,
                        auditProjectAssetTree: false,
                        additionalRelativeRoots: new[] { "Assets\\Extra" }),
                    errors);

            Assert.That(errors, Is.Empty);

            PortableAssetPathAuditResult result =
                PortableAssetPathAudit.Audit(projectRoot, scopes);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.Findings[0].ScopeName, Is.EqualTo("Assets/Extra"));
        }

        // ---------- Step wiring ----------

        [Test]
        public void StepRunAudit_StreamingAssetsNonAsciiIsReportedAsError()
        {
            CreateEmptyFile(Path.Combine(streamingAssetsRoot, CjkName));

            var errors = new List<string>();
            PortableAssetPathAuditResult result = AssetPathAuditBuildStep.RunAudit(
                projectRoot,
                configuration: null,
                errors);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.ErrorMessages[0], Does.Contain("StreamingAssets"));
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void StepRunAudit_ProjectAssetNonAsciiIsWarningByDefault()
        {
            Directory.CreateDirectory(assetsRoot);
            CreateEmptyFile(Path.Combine(assetsRoot, CjkName));

            var errors = new List<string>();
            PortableAssetPathAuditResult result = AssetPathAuditBuildStep.RunAudit(
                projectRoot,
                configuration: null,
                errors);

            Assert.That(result.ErrorCount, Is.Zero);
            Assert.That(result.WarningMessages.Count, Is.EqualTo(1));
        }

        [Test]
        public void StepRunAudit_ExplicitConfiguration_ReportsInvalidRootAsConfigurationError()
        {
            var configuration = ScriptableObject.CreateInstance<AssetPathAuditConfiguration>();
            try
            {
                configuration.auditProjectAssetTree = false;
                configuration.additionalRelativeRoots = new[] { "../outside" };

                var errors = new List<string>();
                PortableAssetPathAuditResult result = AssetPathAuditBuildStep.RunAudit(
                    projectRoot,
                    configuration,
                    errors);

                Assert.That(
                    errors,
                    Has.Some.Contains("Asset path audit root"));
                Assert.That(result.ErrorCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        private static bool HasPathTooLongFinding(PortableAssetPathAuditResult result)
        {
            for (int index = 0; index < result.Findings.Count; index++)
            {
                if (result.Findings[index].Kind == PortablePathViolationKind.PathTooLong)
                {
                    return true;
                }
            }

            return false;
        }

        private PortableAssetPathAuditScope CreateAssetsScope(bool isError = true)
        {
            return new PortableAssetPathAuditScope(
                "Assets",
                assetsRoot,
                rejectNonAscii: true,
                enforceWin32PathBudget: true,
                isError: isError);
        }

        private static void CreateEmptyFile(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (File.Create(path))
            {
            }
        }
    }
}
