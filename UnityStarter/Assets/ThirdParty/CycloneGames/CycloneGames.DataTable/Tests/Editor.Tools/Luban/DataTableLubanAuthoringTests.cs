using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CycloneGames.DataTable.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Tests.Editor.Tools.Luban
{
    public sealed class DataTableLubanAuthoringTests
    {
        private const string BusyInspectionJson =
            "{" +
            "\"schema\":\"CycloneGames.DataTable.PipelineInspection\"," +
            "\"schemaVersion\":1," +
            "\"status\":\"busy\"," +
            "\"canGenerate\":false,\"canCheck\":false,\"canRecover\":false," +
            "\"configurationPath\":\"C:/repo/DataTable/Luban/build_config.ini\"," +
            "\"configurationSha256\":\"configuration-sha\"," +
            "\"sourceRoot\":\"C:/repo/DataTable/Luban\"," +
            "\"selectedProfileName\":\"client\"," +
            "\"processTimeoutSeconds\":600," +
            "\"issues\":[{" +
            "\"code\":\"OUTPUT_VALIDATION_DEFERRED\",\"severity\":\"info\"," +
            "\"scope\":\"output\",\"message\":\"Deferred while writer is active.\"," +
            "\"path\":\"C:/repo/generated/receipt.json\"}]," +
            "\"profiles\":[{" +
            "\"name\":\"client\",\"selected\":true," +
            "\"codeOutputPath\":\"C:/repo/generated/code\"," +
            "\"dataOutputPath\":\"C:/repo/generated/data\"," +
            "\"codeTarget\":\"cs-bin\",\"dataTarget\":\"bin\",\"lineEnding\":\"lf\"}]," +
            "\"selectedProfile\":{" +
            "\"name\":\"client\",\"selected\":true," +
            "\"codeOutputPath\":\"C:/repo/generated/code\"," +
            "\"dataOutputPath\":\"C:/repo/generated/data\"," +
            "\"codeTarget\":\"cs-bin\",\"dataTarget\":\"bin\",\"lineEnding\":\"lf\"}," +
            "\"toolchain\":{" +
            "\"state\":\"ready\",\"codegenProjectPath\":\"C:/repo/CodeGen.csproj\"," +
            "\"codegenProjectExists\":true," +
            "\"lubanConfigurationPath\":\"C:/repo/luban.conf\"," +
            "\"lubanConfigurationExists\":true," +
            "\"lubanExecutablePath\":\"C:/repo/Luban.dll\"," +
            "\"lubanExecutableExists\":true,\"useDotNetHost\":true," +
            "\"configuredVersion\":\"3.0.0\",\"configuredSha256\":\"configured-sha\"," +
            "\"actualSha256\":\"\",\"lubanIdentityStatus\":\"approved\"," +
            "\"configuredSourceFingerprint\":\"configured-source\"," +
            "\"actualSourceFingerprint\":\"\"," +
            "\"sourceFingerprintStatus\":\"unavailable\",\"schemaSha256\":\"schema-sha\"}," +
            "\"output\":{" +
            "\"state\":\"unavailable\",\"receiptPath\":\"C:/repo/generated/receipt.json\"," +
            "\"receiptExists\":true,\"receiptValid\":false,\"generation\":\"\"}," +
            "\"transaction\":{" +
            "\"state\":\"active\",\"lockPath\":\"C:/repo/.writer.lock\"," +
            "\"lockExists\":true,\"runId\":\"0123456789abcdef0123456789abcdef\"," +
            "\"writerProcessId\":4242,\"writerProcessAlive\":true," +
            "\"cancelRequested\":false,\"activeLubanEvidence\":true," +
            "\"transactionPath\":\"C:/repo/.transaction\",\"journalExists\":true," +
            "\"journalState\":\"publishing\",\"recoveryRequired\":false}" +
            "}";

        [Test]
        public void FrozenBusyInspectionContract_ParsesBooleanAndDeferredState()
        {
            bool parsed = DataTableLubanInspectionProtocol.TryParse(
                BusyInspectionJson,
                out DataTableLubanInspectionDocument document,
                out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(document.toolchain.codegenProjectExists, Is.True);
            Assert.That(document.toolchain.lubanConfigurationExists, Is.True);
            Assert.That(document.toolchain.lubanExecutableExists, Is.True);
            Assert.That(document.toolchain.useDotNetHost, Is.True);
            Assert.That(document.output.receiptExists, Is.True);
            Assert.That(document.output.receiptValid, Is.False);
            Assert.That(document.transaction.lockExists, Is.True);
            Assert.That(document.transaction.writerProcessAlive, Is.True);
            Assert.That(document.transaction.cancelRequested, Is.False);
            Assert.That(document.transaction.activeLubanEvidence, Is.True);
            Assert.That(document.transaction.journalExists, Is.True);
            Assert.That(document.transaction.recoveryRequired, Is.False);

            DataTableLubanAuthoringSnapshot snapshot = DataTableLubanInspectionProtocol.Project(
                document,
                "Assets/Editor/DataTable/DataTableLubanSettings.asset",
                settingsAssetCount: 1,
                settingsDirty: false);
            Assert.That(snapshot.State, Is.EqualTo(DataTableLubanAuthoringState.Busy));
            Assert.That(snapshot.Output.State, Is.EqualTo("unavailable"));
            Assert.That(snapshot.Output.ReceiptExists, Is.True);
            Assert.That(snapshot.Output.ReceiptValid, Is.False);
            Assert.That(snapshot.Transaction.ActiveLubanEvidence, Is.True);
        }

        [Test]
        public void DirtyOrDuplicateSettings_BlockEveryOperation()
        {
            Assert.That(
                DataTableLubanInspectionProtocol.TryParse(
                    BusyInspectionJson.Replace("\"status\":\"busy\"", "\"status\":\"ready\"")
                        .Replace("\"canGenerate\":false", "\"canGenerate\":true")
                        .Replace("\"canCheck\":false", "\"canCheck\":true")
                        .Replace("\"canRecover\":false", "\"canRecover\":true"),
                    out DataTableLubanInspectionDocument document,
                    out string error),
                Is.True,
                error);

            DataTableLubanAuthoringSnapshot snapshot = DataTableLubanInspectionProtocol.Project(
                document,
                "Assets/Editor/DataTable/DataTableLubanSettings.asset",
                settingsAssetCount: 2,
                settingsDirty: true);

            Assert.That(snapshot.CanGenerate, Is.False);
            Assert.That(snapshot.CanCheck, Is.False);
            Assert.That(snapshot.CanRecover, Is.False);
            Assert.That(snapshot.State, Is.EqualTo(DataTableLubanAuthoringState.Blocked));
            Assert.That(snapshot.Issues.Select(static issue => issue.Code),
                Does.Contain("SETTINGS_NOT_UNIQUE"));
            Assert.That(snapshot.Issues.Select(static issue => issue.Code),
                Does.Contain("SETTINGS_UNSAVED"));
        }

        [Test]
        public void Settings_SerializedContractContainsOnlyAuthoringInputs()
        {
            string[] fields = typeof(DataTableLubanSettings)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(static field => field.GetCustomAttribute<SerializeField>() != null)
                .Select(static field => field.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(fields, Is.EqualTo(new[]
            {
                "buildConfigurationPath",
                "defaultProfileName",
                "maximumCapturedOutputCharacters",
                "refreshAssetsAfterSuccess",
                "schemaVersion",
            }));
        }

        [Test]
        public void SettingsEditor_DoesNotDeclareUnityStartMessage()
        {
            MethodInfo[] methods = typeof(DataTableLubanSettingsEditor).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            Assert.That(methods.Any(static method => method.Name == "Start"), Is.False);
        }

        [Test]
        public void ToolProjectLocator_ResolvesFromOwningPackage()
        {
            var settings = ScriptableObject.CreateInstance<DataTableLubanSettings>();
            try
            {
                string projectPath = DataTableLubanToolProjectLocator.ResolveToolProjectPath(settings);
                Assert.That(File.Exists(projectPath), Is.True, projectPath);
                Assert.That(
                    Path.GetFileName(projectPath),
                    Is.EqualTo("CycloneGames.DataTable.CodeGen.csproj"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void InspectionProtocol_RejectsSchemaDrift()
        {
            string drifted = BusyInspectionJson.Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":2");

            Assert.That(
                DataTableLubanInspectionProtocol.TryParse(
                    drifted,
                    out _,
                    out string error),
                Is.False);
            Assert.That(error, Does.Contain("Unsupported pipeline inspection schema"));
        }

        [Test]
        public void InspectionProcessRegistry_ShutdownTerminatesOwnedProcessesAndRejectsLateStarts()
        {
            var starts = 0;
            var terminations = 0;
            var disposals = 0;
            var registry = new DataTableLubanInspectionProcessRegistry(
                process =>
                {
                    starts++;
                    return true;
                },
                (System.Diagnostics.Process process, int timeoutMilliseconds, out string error) =>
                {
                    terminations++;
                    error = string.Empty;
                    return true;
                },
                process =>
                {
                    disposals++;
                    process.Dispose();
                });

            var first = new System.Diagnostics.Process();
            using var late = new System.Diagnostics.Process();
            Assert.That(
                registry.TryStartAndRegister(first),
                Is.EqualTo(DataTableLubanInspectionProcessStartOutcome.Started));

            bool confirmed = registry.ShutdownAndTerminateActiveProcesses(
                2_000,
                out int activeProcessCount,
                out string error);

            Assert.That(confirmed, Is.True, error);
            Assert.That(activeProcessCount, Is.EqualTo(1));
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(terminations, Is.EqualTo(1));
            Assert.That(disposals, Is.EqualTo(1));
            Assert.That(
                registry.TryStartAndRegister(late),
                Is.EqualTo(DataTableLubanInspectionProcessStartOutcome.RejectedForShutdown));
            Assert.That(starts, Is.EqualTo(1), "A late process must never cross the shutdown gate.");
        }

        [Test]
        public void InspectionProcessRegistry_UnconfirmedReleaseRetainsOwnershipForLifecycleRetry()
        {
            var terminationAttempts = 0;
            var disposals = 0;
            var registry = new DataTableLubanInspectionProcessRegistry(
                process => true,
                (System.Diagnostics.Process process, int timeoutMilliseconds, out string error) =>
                {
                    terminationAttempts++;
                    bool confirmed = terminationAttempts >= 2;
                    error = confirmed ? string.Empty : "descendant termination unconfirmed";
                    return confirmed;
                },
                process =>
                {
                    disposals++;
                    process.Dispose();
                });

            var process = new System.Diagnostics.Process();
            Assert.That(
                registry.TryStartAndRegister(process),
                Is.EqualTo(DataTableLubanInspectionProcessStartOutcome.Started));
            Assert.That(registry.ReleaseIfConfirmed(process, confirmed: false), Is.False);

            bool firstConfirmed = registry.ShutdownAndTerminateActiveProcesses(
                2_000,
                out int firstActiveProcessCount,
                out string firstError);

            Assert.That(firstConfirmed, Is.False);
            Assert.That(firstActiveProcessCount, Is.EqualTo(1));
            Assert.That(firstError, Does.Contain("descendant termination unconfirmed"));
            Assert.That(disposals, Is.EqualTo(0));

            bool secondConfirmed = registry.ShutdownAndTerminateActiveProcesses(
                2_000,
                out int secondActiveProcessCount,
                out string secondError);

            Assert.That(secondConfirmed, Is.True, secondError);
            Assert.That(secondActiveProcessCount, Is.EqualTo(1));
            Assert.That(terminationAttempts, Is.EqualTo(2));
            Assert.That(disposals, Is.EqualTo(1));
        }

        [Test]
        public void InspectionProcessRegistry_ConfirmedCompletionReleasesAndDisposesExactlyOnce()
        {
            var terminationAttempts = 0;
            var disposals = 0;
            var registry = new DataTableLubanInspectionProcessRegistry(
                process => true,
                (System.Diagnostics.Process process, int timeoutMilliseconds, out string error) =>
                {
                    terminationAttempts++;
                    error = string.Empty;
                    return true;
                },
                process =>
                {
                    disposals++;
                    process.Dispose();
                });
            var process = new System.Diagnostics.Process();
            Assert.That(
                registry.TryStartAndRegister(process),
                Is.EqualTo(DataTableLubanInspectionProcessStartOutcome.Started));

            Assert.That(registry.ReleaseIfConfirmed(process, confirmed: true), Is.True);
            Assert.That(
                registry.ShutdownAndTerminateActiveProcesses(
                    2_000,
                    out int activeProcessCount,
                    out string error),
                Is.True,
                error);
            Assert.That(activeProcessCount, Is.EqualTo(0));
            Assert.That(terminationAttempts, Is.EqualTo(0));
            Assert.That(disposals, Is.EqualTo(1));
        }

        [Test]
        public void InspectionOwnershipPolicy_ReaderCompletionCannotReleaseUnconfirmedCancellation()
        {
            Assert.That(
                DataTableLubanInspectionOwnership.CanRelease(
                    cancelledOrTimedOut: true,
                    readersCompleted: true,
                    treeTerminationConfirmed: false),
                Is.False);
            Assert.That(
                DataTableLubanInspectionOwnership.CanRelease(
                    cancelledOrTimedOut: false,
                    readersCompleted: true,
                    treeTerminationConfirmed: false),
                Is.True);
            Assert.That(
                DataTableLubanInspectionOwnership.CanRelease(
                    cancelledOrTimedOut: true,
                    readersCompleted: false,
                    treeTerminationConfirmed: true),
                Is.True);
        }

        [Test]
        public void CancellationPolicy_DoesNotAuthorizeRunnerCompletingPhase()
        {
            var completing = new DataTableLubanRunnerState(
                revision: 1,
                phase: DataTableLubanRunnerPhase.Completing,
                isActive: true,
                operation: DataTableLubanOperation.Generate,
                profileName: "client",
                buildConfigurationPath: "DataTable/Luban/build_config.ini",
                processId: 42,
                startedUtcTicks: DateTime.UtcNow.Ticks,
                updatedUtcTicks: DateTime.UtcNow.Ticks,
                hasLastResult: false,
                lastResult: default);

            Assert.That(
                DataTableLubanAuthoringCoordinator.GetCancellationTarget(
                    completing,
                    operationInProgress: true,
                    operationCancellationAvailable: true,
                    runnerStartedForOperation: true),
                Is.EqualTo(DataTableLubanAuthoringCancellationTarget.None));
        }

        [Test]
        public void OperationProjection_PreflightHidesRetainedRunnerResult()
        {
            var retainedRunnerState = new DataTableLubanRunnerState(
                revision: 7,
                phase: DataTableLubanRunnerPhase.Succeeded,
                isActive: false,
                operation: DataTableLubanOperation.Check,
                profileName: "previous",
                buildConfigurationPath: "previous.ini",
                processId: 0,
                startedUtcTicks: 111,
                updatedUtcTicks: 112,
                hasLastResult: true,
                lastResult: default);

            DataTableLubanAuthoringOperationProjection projection =
                DataTableLubanAuthoringCoordinator.ProjectOperationState(
                    retainedRunnerState,
                    operationInProgress: true,
                    runnerStartedForOperation: false,
                    authoringOperation: DataTableLubanOperation.Generate,
                    authoringStartedUtcTicks: 222,
                    hasAuthoringResult: false,
                    authoringResult: default);

            Assert.That(projection.Operation, Is.EqualTo(DataTableLubanOperation.Generate));
            Assert.That(projection.StartedUtcTicks, Is.EqualTo(222));
            Assert.That(projection.HasLastResult, Is.False);
            Assert.That(projection.LastResult, Is.EqualTo(default(DataTableLubanRunResult)));
        }

        [Test]
        public void SettingsAssetIndex_ReusesCountUntilProjectChangeInvalidatesIt()
        {
            var reads = 0;
            var index = new DataTableLubanSettingsAssetIndex(() =>
            {
                reads++;
                return 1;
            });

            Assert.That(index.Count, Is.EqualTo(1));
            Assert.That(index.Count, Is.EqualTo(1));
            Assert.That(reads, Is.EqualTo(1));

            index.Invalidate();

            Assert.That(index.Count, Is.EqualTo(1));
            Assert.That(reads, Is.EqualTo(2));
        }

        [Test]
        public void SettingsObserverRegistry_KeepsFallbackUntilLastInspectorStopsObserving()
        {
            var registry = new DataTableLubanSettingsObserverRegistry();
            var firstOwner = new object();
            var secondOwner = new object();
            var firstSettings = ScriptableObject.CreateInstance<DataTableLubanSettings>();
            var secondSettings = ScriptableObject.CreateInstance<DataTableLubanSettings>();
            try
            {
                registry.Observe(firstOwner, firstSettings);
                registry.Observe(secondOwner, secondSettings);

                Assert.That(registry.Count, Is.EqualTo(2));
                Assert.That(registry.Current, Is.SameAs(secondSettings));

                Assert.That(registry.StopObserving(secondOwner), Is.True);
                Assert.That(registry.Count, Is.EqualTo(1));
                Assert.That(registry.Current, Is.SameAs(firstSettings));

                Assert.That(registry.StopObserving(firstOwner), Is.True);
                Assert.That(registry.Count, Is.EqualTo(0));
                Assert.That(registry.Current, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstSettings);
                UnityEngine.Object.DestroyImmediate(secondSettings);
            }
        }

        [Test]
        public void InspectionArbitration_PassiveRequestCannotPreemptExplicitInspection()
        {
            Assert.That(
                DataTableLubanAuthoringCoordinator.ShouldDeferPassiveInspectionRequest(
                    requestIsPassive: true,
                    hasActiveInspection: true,
                    activeInspectionIsPassive: false),
                Is.True);
            Assert.That(
                DataTableLubanAuthoringCoordinator.ShouldDeferPassiveInspectionRequest(
                    requestIsPassive: true,
                    hasActiveInspection: true,
                    activeInspectionIsPassive: true),
                Is.False,
                "A newer passive observer request may replace stale passive work.");
            Assert.That(
                DataTableLubanAuthoringCoordinator.ShouldDeferPassiveInspectionRequest(
                    requestIsPassive: false,
                    hasActiveInspection: true,
                    activeInspectionIsPassive: false),
                Is.False,
                "An explicit request remains authoritative and may replace earlier work.");
        }

        [Test]
        public void PostOperationInspectionTarget_PrefersCurrentObserver()
        {
            var operationSettings = ScriptableObject.CreateInstance<DataTableLubanSettings>();
            var observedSettings = ScriptableObject.CreateInstance<DataTableLubanSettings>();
            try
            {
                Assert.That(
                    DataTableLubanAuthoringCoordinator.SelectPostOperationInspectionTarget(
                        operationSettings,
                        observedSettings),
                    Is.SameAs(observedSettings));
                Assert.That(
                    DataTableLubanAuthoringCoordinator.SelectPostOperationInspectionTarget(
                        operationSettings,
                        observedSettings: null),
                    Is.SameAs(operationSettings));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(observedSettings);
                UnityEngine.Object.DestroyImmediate(operationSettings);
            }
        }

        [Test]
        public void SaveSettings_ReportsWhenUnityLeavesTheAssetDirty()
        {
            var settings = ScriptableObject.CreateInstance<DataTableLubanSettings>();
            try
            {
                EditorUtility.SetDirty(settings);

                DataTableLubanAuthoringCoordinator.SaveSettings(settings);

                Assert.That(EditorUtility.IsDirty(settings), Is.True);
                Assert.That(
                    DataTableLubanAuthoringCoordinator.AuthoringError,
                    Does.Contain("remains unsaved"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SectionHeaderLayout_ContainsArrowLabelAndBadgeWithinHeader()
        {
            var header = new Rect(20f, 10f, 400f, 23f);

            DataTableLubanSectionHeaderLayout layout =
                DataTableLubanInspectorUi.CalculateSectionHeaderLayout(header, "READY");

            Assert.That(layout.HeaderRect, Is.EqualTo(header));
            Assert.That(layout.ArrowRect, Is.EqualTo(new Rect(26f, 12f, 13f, 19f)));
            Assert.That(layout.LabelRect, Is.EqualTo(new Rect(41f, 10f, 314f, 23f)));
            Assert.That(layout.BadgeRect, Is.EqualTo(new Rect(359f, 13f, 56f, 17f)));
            Assert.That(layout.HasBadge, Is.True);
            Assert.That(
                layout.Mode,
                Is.EqualTo(DataTableLubanSectionHeaderLayoutMode.Inline));
            Assert.That(layout.ArrowRect.xMin, Is.GreaterThanOrEqualTo(header.xMin));
            Assert.That(layout.ArrowRect.xMax, Is.LessThanOrEqualTo(layout.LabelRect.xMin));
            Assert.That(layout.LabelRect.xMax, Is.LessThanOrEqualTo(layout.BadgeRect.xMin));
            Assert.That(layout.BadgeRect.xMax, Is.LessThanOrEqualTo(header.xMax));
        }

        [TestCase(400f, (int)DataTableLubanHeroLayoutMode.Inline, true)]
        [TestCase(240f, (int)DataTableLubanHeroLayoutMode.Stacked, true)]
        [TestCase(180f, (int)DataTableLubanHeroLayoutMode.Stacked, true)]
        [TestCase(179f, (int)DataTableLubanHeroLayoutMode.Compact, false)]
        [TestCase(140f, (int)DataTableLubanHeroLayoutMode.Compact, false)]
        [TestCase(40f, (int)DataTableLubanHeroLayoutMode.Compact, false)]
        public void HeroLayout_ContainsIdentityAndStatusAtEveryWidth(
            float width,
            int expectedModeValue,
            bool expectedSubtitle)
        {
            var mode = (DataTableLubanHeroLayoutMode)expectedModeValue;
            var bounds = new Rect(
                20f,
                10f,
                width,
                DataTableLubanInspectorUi.GetHeroHeight(mode));

            DataTableLubanHeroLayout layout =
                DataTableLubanInspectorUi.CalculateHeroLayout(
                    bounds,
                    "SETUP REQUIRED",
                    mode);

            Assert.That(layout.Bounds, Is.EqualTo(bounds));
            Assert.That(layout.Mode, Is.EqualTo(mode));
            Assert.That(layout.HasSubtitle, Is.EqualTo(expectedSubtitle));
            Assert.That(
                DataTableLubanInspectorUi.GetHeroLayoutMode(width),
                Is.EqualTo(mode));
            AssertRectIsContained(bounds, layout.AccentRect);
            AssertRectIsContained(bounds, layout.TitleRect);
            AssertRectIsContained(bounds, layout.BadgeRect);
            if (mode == DataTableLubanHeroLayoutMode.Inline)
            {
                Assert.That(layout.TitleRect.xMax, Is.LessThanOrEqualTo(layout.BadgeRect.xMin));
            }
            else if (mode == DataTableLubanHeroLayoutMode.Stacked)
            {
                AssertRectIsContained(bounds, layout.SubtitleRect);
                Assert.That(layout.TitleRect.yMax, Is.LessThanOrEqualTo(layout.SubtitleRect.yMin));
                Assert.That(layout.SubtitleRect.yMax, Is.LessThanOrEqualTo(layout.BadgeRect.yMin));
            }
            else
            {
                Assert.That(layout.SubtitleRect, Is.EqualTo(default(Rect)));
                Assert.That(layout.TitleRect.yMax, Is.LessThanOrEqualTo(layout.BadgeRect.yMin));
            }
        }

        [TestCase(400f, (int)DataTableLubanSectionHeaderLayoutMode.Inline)]
        [TestCase(236f, (int)DataTableLubanSectionHeaderLayoutMode.Inline)]
        [TestCase(235f, (int)DataTableLubanSectionHeaderLayoutMode.Stacked)]
        [TestCase(140f, (int)DataTableLubanSectionHeaderLayoutMode.Stacked)]
        [TestCase(40f, (int)DataTableLubanSectionHeaderLayoutMode.Stacked)]
        public void SectionHeaderLayout_StacksBadgeWithoutHidingTitle(
            float width,
            int expectedModeValue)
        {
            var mode = (DataTableLubanSectionHeaderLayoutMode)expectedModeValue;
            float height = mode == DataTableLubanSectionHeaderLayoutMode.Inline ? 23f : 45f;
            var bounds = new Rect(20f, 10f, width, height);

            DataTableLubanSectionHeaderLayout layout =
                DataTableLubanInspectorUi.CalculateSectionHeaderLayout(
                    bounds,
                    "SETUP REQUIRED",
                    mode);

            Assert.That(layout.Mode, Is.EqualTo(mode));
            Assert.That(layout.HasBadge, Is.True);
            Assert.That(
                DataTableLubanInspectorUi.GetSectionHeaderLayoutMode(
                    width,
                    "SETUP REQUIRED"),
                Is.EqualTo(mode));
            AssertRectIsContained(bounds, layout.ArrowRect);
            AssertRectIsContained(bounds, layout.LabelRect);
            AssertRectIsContained(bounds, layout.BadgeRect);
            if (mode == DataTableLubanSectionHeaderLayoutMode.Inline)
            {
                Assert.That(layout.LabelRect.xMax, Is.LessThanOrEqualTo(layout.BadgeRect.xMin));
            }
            else
            {
                Assert.That(layout.LabelRect.yMax, Is.LessThanOrEqualTo(layout.BadgeRect.yMin));
            }
        }

        [TestCase(300f, false)]
        [TestCase(216f, false)]
        [TestCase(215f, true)]
        [TestCase(140f, true)]
        [TestCase(40f, true)]
        public void StatusRowLayout_PreservesLabelAndValueAtNarrowWidths(
            float width,
            bool stacked)
        {
            float height = stacked ? 40f : 19f;
            var bounds = new Rect(20f, 10f, width, height);

            DataTableLubanStatusRowLayout layout =
                DataTableLubanInspectorUi.CalculateStatusRowLayout(bounds, stacked);

            Assert.That(layout.IsStacked, Is.EqualTo(stacked));
            AssertRectIsContained(bounds, layout.MarkerRect);
            AssertRectIsContained(bounds, layout.LabelRect);
            AssertRectIsContained(bounds, layout.ValueRect);
            if (stacked)
            {
                Assert.That(layout.LabelRect.yMax, Is.LessThanOrEqualTo(layout.ValueRect.yMin));
            }
            else
            {
                Assert.That(layout.LabelRect.xMax, Is.LessThanOrEqualTo(layout.ValueRect.xMin));
            }
        }

        [Test]
        public void StatusRowLayout_UsesLongValueAsAStackingSignal()
        {
            Assert.That(
                DataTableLubanInspectorUi.ShouldStackStatusRow(216f, "Profile", "client"),
                Is.False);
            Assert.That(
                DataTableLubanInspectorUi.ShouldStackStatusRow(
                    220f,
                    "Published Output",
                    "Deferred while writer is active"),
                Is.True);
        }

        [TestCase(400f, true, (int)DataTableLubanReadOnlyPathLayoutMode.Inline)]
        [TestCase(356f, true, (int)DataTableLubanReadOnlyPathLayoutMode.Inline)]
        [TestCase(355f, true, (int)DataTableLubanReadOnlyPathLayoutMode.Stacked)]
        [TestCase(320f, true, (int)DataTableLubanReadOnlyPathLayoutMode.Stacked)]
        [TestCase(320f, false, (int)DataTableLubanReadOnlyPathLayoutMode.Inline)]
        [TestCase(224f, true, (int)DataTableLubanReadOnlyPathLayoutMode.Stacked)]
        [TestCase(223f, true, (int)DataTableLubanReadOnlyPathLayoutMode.Vertical)]
        [TestCase(140f, true, (int)DataTableLubanReadOnlyPathLayoutMode.Vertical)]
        public void ReadOnlyPathLayout_ContainsNonOverlappingFactsAndActions(
            float width,
            bool showReveal,
            int expectedModeValue)
        {
            var expectedMode = (DataTableLubanReadOnlyPathLayoutMode)expectedModeValue;
            int lineCount = expectedMode == DataTableLubanReadOnlyPathLayoutMode.Inline
                ? 1
                : expectedMode == DataTableLubanReadOnlyPathLayoutMode.Stacked
                    ? 2
                    : 3;
            float rowHeight = EditorGUIUtility.singleLineHeight * lineCount +
                              2f * (lineCount - 1);
            var row = new Rect(20f, 10f, width, rowHeight);

            DataTableLubanReadOnlyPathLayout layout =
                DataTableLubanInspectorUi.CalculateReadOnlyPathLayout(
                    row,
                    showReveal,
                    expectedMode);

            Assert.That(layout.RowRect, Is.EqualTo(row));
            Assert.That(layout.Mode, Is.EqualTo(expectedMode));
            Assert.That(
                DataTableLubanInspectorUi.GetReadOnlyPathLayoutMode(width, showReveal),
                Is.EqualTo(expectedMode));
            AssertRectIsContained(row, layout.LabelRect);
            AssertRectIsContained(row, layout.ValueRect);
            AssertRectIsContained(row, layout.CopyRect);
            if (expectedMode == DataTableLubanReadOnlyPathLayoutMode.Inline)
            {
                Assert.That(layout.LabelRect.xMax, Is.LessThanOrEqualTo(layout.ValueRect.xMin));
            }
            else
            {
                Assert.That(layout.LabelRect.yMax, Is.LessThanOrEqualTo(layout.ValueRect.yMin));
            }

            if (expectedMode == DataTableLubanReadOnlyPathLayoutMode.Vertical)
            {
                Assert.That(layout.ValueRect.yMax, Is.LessThanOrEqualTo(layout.CopyRect.yMin));
            }
            else
            {
                Assert.That(layout.ValueRect.xMax, Is.LessThanOrEqualTo(layout.CopyRect.xMin));
            }

            Assert.That(layout.HasReveal, Is.EqualTo(showReveal));
            if (showReveal)
            {
                AssertRectIsContained(row, layout.RevealRect);
                Assert.That(layout.CopyRect.xMax, Is.LessThanOrEqualTo(layout.RevealRect.xMin));
                Assert.That(layout.CopyRect.width, Is.EqualTo(layout.RevealRect.width));
            }
        }

        [TestCase(400f, false)]
        [TestCase(300f, true)]
        public void DualButtonLayout_UsesEqualCellsAndResponsiveRows(
            float width,
            bool stacked)
        {
            float height = stacked ? 44f : 20f;
            var bounds = new Rect(10f, 5f, width, height);

            DataTableLubanDualButtonLayout layout =
                DataTableLubanInspectorUi.CalculateDualButtonLayout(bounds, stacked);

            Assert.That(layout.IsStacked, Is.EqualTo(stacked));
            Assert.That(layout.FirstRect.width, Is.EqualTo(layout.SecondRect.width));
            Assert.That(layout.FirstRect.height, Is.EqualTo(layout.SecondRect.height));
            AssertRectIsContained(bounds, layout.FirstRect);
            AssertRectIsContained(bounds, layout.SecondRect);
            if (stacked)
            {
                Assert.That(layout.FirstRect.width, Is.EqualTo(bounds.width));
                Assert.That(layout.FirstRect.yMax + 4f, Is.EqualTo(layout.SecondRect.yMin));
            }
            else
            {
                Assert.That(layout.FirstRect.xMax + 4f, Is.EqualTo(layout.SecondRect.xMin));
            }
        }

        [TestCase(400f, false)]
        [TestCase(300f, true)]
        public void FieldActionLayout_PreservesFieldAndEqualActionCells(
            float width,
            bool stacked)
        {
            float height = stacked ? 44f : 20f;
            var bounds = new Rect(10f, 5f, width, height);

            DataTableLubanFieldActionLayout layout =
                DataTableLubanInspectorUi.CalculateFieldActionLayout(bounds, stacked);

            Assert.That(layout.IsStacked, Is.EqualTo(stacked));
            Assert.That(layout.FirstActionRect.width, Is.EqualTo(layout.SecondActionRect.width));
            AssertRectIsContained(bounds, layout.FieldRect);
            AssertRectIsContained(bounds, layout.FirstActionRect);
            AssertRectIsContained(bounds, layout.SecondActionRect);
            if (stacked)
            {
                Assert.That(layout.FieldRect.width, Is.EqualTo(bounds.width));
                Assert.That(layout.FieldRect.yMax + 4f, Is.EqualTo(layout.FirstActionRect.yMin));
                Assert.That(
                    layout.FirstActionRect.xMax + 4f,
                    Is.EqualTo(layout.SecondActionRect.xMin));
            }
            else
            {
                Assert.That(layout.FieldRect.width, Is.GreaterThanOrEqualTo(180f));
                Assert.That(layout.FirstActionRect.width, Is.EqualTo(64f));
                Assert.That(layout.SecondActionRect.width, Is.EqualTo(64f));
            }
        }

        [Test]
        public void ResponsiveActionThresholds_AreDeterministic()
        {
            Assert.That(DataTableLubanInspectorUi.ShouldStackDualButtons(332f), Is.False);
            Assert.That(DataTableLubanInspectorUi.ShouldStackDualButtons(331f), Is.True);
            Assert.That(DataTableLubanInspectorUi.ShouldStackFieldActions(316f), Is.False);
            Assert.That(DataTableLubanInspectorUi.ShouldStackFieldActions(315f), Is.True);
        }

        [Test]
        public void ReadOnlyPresentation_InheritsLabelChromeAndBoundedTextBehavior()
        {
            GUIStyle pathStyle = DataTableLubanInspectorUi.GetReadOnlyPathStyleForTests();
            GUIStyle outputStyle = DataTableLubanInspectorUi.GetReadOnlyOutputStyleForTests();

            Assert.That(
                pathStyle.normal.background == EditorStyles.miniLabel.normal.background,
                Is.True);
            AssertRectOffsetEquals(EditorStyles.miniLabel.border, pathStyle.border);
            AssertRectOffsetEquals(EditorStyles.miniLabel.padding, pathStyle.padding);
            Assert.That(pathStyle.clipping, Is.EqualTo(TextClipping.Clip));
            Assert.That(pathStyle.wordWrap, Is.False);
            Assert.That(
                outputStyle.normal.background ==
                EditorStyles.wordWrappedMiniLabel.normal.background,
                Is.True);
            AssertRectOffsetEquals(EditorStyles.wordWrappedMiniLabel.border, outputStyle.border);
            AssertRectOffsetEquals(EditorStyles.wordWrappedMiniLabel.padding, outputStyle.padding);
            Assert.That(outputStyle.clipping, Is.EqualTo(TextClipping.Clip));
            Assert.That(outputStyle.wordWrap, Is.True);
        }

        private static void AssertRectIsContained(Rect outer, Rect inner)
        {
            Assert.That(inner.width, Is.GreaterThanOrEqualTo(0f));
            Assert.That(inner.height, Is.GreaterThanOrEqualTo(0f));
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin));
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax));
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin));
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax));
        }

        private static void AssertRectOffsetEquals(RectOffset expected, RectOffset actual)
        {
            Assert.That(actual.left, Is.EqualTo(expected.left));
            Assert.That(actual.right, Is.EqualTo(expected.right));
            Assert.That(actual.top, Is.EqualTo(expected.top));
            Assert.That(actual.bottom, Is.EqualTo(expected.bottom));
        }
    }
}
