using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Build.VersionControl.Editor;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private static readonly GUIContent RefreshSourceWorkspaceContent = new GUIContent(
            "Refresh Source Status",
            "Capture tracked, untracked, submodule, and Git LFS source-workspace evidence again.");
        private static readonly GUIContent CheckingSourceWorkspaceContent = new GUIContent(
            "Checking Source Status...",
            "A bounded source-workspace inspection is already running.");
        private static readonly BuildInspectorCommand[] RefreshSourceWorkspaceCommands =
        {
            new BuildInspectorCommand(
                0,
                RefreshSourceWorkspaceContent,
                role: BuildInspectorActionRole.Primary)
        };
        private static readonly BuildInspectorCommand[] CheckingSourceWorkspaceCommands =
        {
            new BuildInspectorCommand(
                0,
                CheckingSourceWorkspaceContent,
                enabled: false,
                role: BuildInspectorActionRole.Primary)
        };

        private Task<SourceWorkspaceCaptureResult> sourceWorkspaceCaptureTask;
        private VersionControlWorkspaceEvidence sourceWorkspaceEvidence;
        private string sourceWorkspaceProviderLabel = "Not detected";
        private string sourceWorkspaceInspectionError;
        private string sourceWorkspaceCapturedAtLabel = "Not checked";
        private int sourceWorkspaceCaptureGeneration;
        private bool sourceWorkspaceMonitorEnabled;
        private bool sourceWorkspaceRefreshPending;
        private bool sourceWorkspacePreviewCanGate = true;
        private CancellationTokenSource sourceWorkspaceCaptureCancellation;
        private bool sourceWorkspaceDecisionsValid;
        private int sourceWorkspaceDecisionPolicyValue = int.MinValue;
        private VersionControlWorkspaceEvidence sourceWorkspaceDecisionEvidence;
        private BuildSourceWorkspaceDecision releaseSourceWorkspaceDecision;
        private BuildSourceWorkspaceDecision developmentSourceWorkspaceDecision;
        private BuildSourceWorkspaceDecision localPreviewSourceWorkspaceDecision;

        private void InitializeSourceWorkspaceMonitor()
        {
            sourceWorkspaceMonitorEnabled = true;
            sourceWorkspaceCaptureTask = null;
            sourceWorkspaceCaptureCancellation = null;
            sourceWorkspaceRefreshPending = false;
            sourceWorkspaceEvidence = null;
            sourceWorkspaceInspectionError = null;
            sourceWorkspaceCapturedAtLabel = "Not checked";
            sourceWorkspaceDecisionsValid = false;
            EditorApplication.projectChanged -= HandleSourceWorkspaceProjectChanged;
            EditorApplication.projectChanged += HandleSourceWorkspaceProjectChanged;
            EditorApplication.focusChanged -= HandleSourceWorkspaceFocusChanged;
            EditorApplication.focusChanged += HandleSourceWorkspaceFocusChanged;
            RequestSourceWorkspaceRefresh();
        }

        private void OnDisable()
        {
            sourceWorkspaceMonitorEnabled = false;
            sourceWorkspaceCaptureGeneration++;
            sourceWorkspaceRefreshPending = false;
            CancelSourceWorkspaceCapture();
            EditorApplication.update -= PollSourceWorkspaceCapture;
            EditorApplication.projectChanged -= HandleSourceWorkspaceProjectChanged;
            EditorApplication.focusChanged -= HandleSourceWorkspaceFocusChanged;
            sourceWorkspaceCaptureTask = null;
        }

        private void HandleSourceWorkspaceProjectChanged()
        {
            if (!sourceWorkspaceMonitorEnabled)
            {
                return;
            }

            QueueSourceWorkspaceRefresh();
        }

        private void HandleSourceWorkspaceFocusChanged(bool hasFocus)
        {
            if (!hasFocus || !sourceWorkspaceMonitorEnabled)
            {
                return;
            }

            QueueSourceWorkspaceRefresh();
        }

        private void QueueSourceWorkspaceRefresh()
        {
            InvalidateSourceWorkspacePreview();
            if (sourceWorkspaceCaptureTask != null)
            {
                sourceWorkspaceRefreshPending = true;
                sourceWorkspaceCaptureGeneration++;
                sourceWorkspaceCaptureCancellation?.Cancel();
                return;
            }

            RequestSourceWorkspaceRefresh();
        }

        private void InvalidateSourceWorkspacePreview()
        {
            sourceWorkspaceEvidence = null;
            sourceWorkspaceInspectionError = null;
            sourceWorkspaceCapturedAtLabel = "Refresh pending";
            sourceWorkspaceDecisionsValid = false;
            Repaint();
        }

        private void RequestSourceWorkspaceRefresh()
        {
            if (!sourceWorkspaceMonitorEnabled || sourceWorkspaceCaptureTask != null)
            {
                return;
            }

            sourceWorkspacePreviewCanGate = true;
            IVersionControlProvider provider;
            try
            {
                provider = VersionControlFactory.CreateDetectedProvider();
            }
            catch (Exception exception)
            {
                PublishSourceWorkspaceFailure(
                    "Detection failed",
                    VersionControlWorkspaceEvidence.CommandFailed,
                    "Source-control provider detection failed (" +
                    exception.GetType().Name + ").");
                return;
            }

            if (provider == null)
            {
                PublishSourceWorkspaceFailure(
                    "Not detected",
                    VersionControlWorkspaceEvidence.MetadataUnavailable,
                    "No supported source-control provider was detected for this project.");
                return;
            }

            sourceWorkspaceProviderLabel = GetSourceWorkspaceProviderLabel(provider);
            if (!(provider is IVersionControlWorkspaceProvider workspaceProvider))
            {
                sourceWorkspacePreviewCanGate = false;
                PublishSourceWorkspaceFailure(
                    sourceWorkspaceProviderLabel,
                    VersionControlWorkspaceEvidence.MetadataUnavailable,
                    "This provider does not expose the optional thread-safe preview capability. " +
                    "The build runner will perform the authoritative validation.");
                return;
            }

            sourceWorkspacePreviewCanGate = true;
            int generation = ++sourceWorkspaceCaptureGeneration;
            sourceWorkspaceRefreshPending = false;
            sourceWorkspaceInspectionError = null;
            sourceWorkspaceCaptureCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken =
                sourceWorkspaceCaptureCancellation.Token;
            sourceWorkspaceDecisionsValid = false;
            sourceWorkspaceCaptureTask = Task.Factory.StartNew(
                () => CaptureSourceWorkspace(
                    workspaceProvider,
                    generation,
                    cancellationToken),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
            EditorApplication.update -= PollSourceWorkspaceCapture;
            EditorApplication.update += PollSourceWorkspaceCapture;
            Repaint();
        }

        private void PollSourceWorkspaceCapture()
        {
            Task<SourceWorkspaceCaptureResult> capture = sourceWorkspaceCaptureTask;
            if (!sourceWorkspaceMonitorEnabled || capture == null)
            {
                EditorApplication.update -= PollSourceWorkspaceCapture;
                return;
            }

            if (!capture.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollSourceWorkspaceCapture;
            sourceWorkspaceCaptureTask = null;
            DisposeSourceWorkspaceCancellation();
            SourceWorkspaceCaptureResult result = capture.GetAwaiter().GetResult();
            if (!sourceWorkspaceMonitorEnabled
                || result.Generation != sourceWorkspaceCaptureGeneration)
            {
                if (sourceWorkspaceMonitorEnabled && sourceWorkspaceRefreshPending)
                {
                    sourceWorkspaceRefreshPending = false;
                    RequestSourceWorkspaceRefresh();
                }

                return;
            }

            sourceWorkspaceEvidence = result.Evidence;
            sourceWorkspaceInspectionError = result.Error;
            sourceWorkspaceCapturedAtLabel = result.CapturedAtUtc
                .ToLocalTime()
                .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            sourceWorkspaceDecisionsValid = false;
            Repaint();
        }

        private static SourceWorkspaceCaptureResult CaptureSourceWorkspace(
            IVersionControlWorkspaceProvider provider,
            int generation,
            CancellationToken cancellationToken)
        {
            try
            {
                VersionControlWorkspaceEvidence evidence = provider.CaptureWorkspace(
                    cancellationToken)
                    ?? VersionControlWorkspaceEvidence.Unknown(
                        VersionControlWorkspaceEvidence.MetadataUnavailable);
                return new SourceWorkspaceCaptureResult(
                    generation,
                    evidence,
                    error: null,
                    DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                return new SourceWorkspaceCaptureResult(
                    generation,
                    VersionControlWorkspaceEvidence.Unknown(
                        VersionControlWorkspaceEvidence.CommandFailed),
                    "Source workspace capture was cancelled.",
                    DateTimeOffset.UtcNow);
            }
            catch (Exception exception)
            {
                return new SourceWorkspaceCaptureResult(
                    generation,
                    VersionControlWorkspaceEvidence.Unknown(
                        VersionControlWorkspaceEvidence.CommandFailed),
                    "Source workspace capture failed (" +
                    exception.GetType().Name + ").",
                    DateTimeOffset.UtcNow);
            }
        }

        private void PublishSourceWorkspaceFailure(
            string providerLabel,
            string failureCode,
            string error)
        {
            sourceWorkspaceProviderLabel = providerLabel;
            sourceWorkspaceEvidence = VersionControlWorkspaceEvidence.Unknown(failureCode);
            sourceWorkspaceInspectionError = error;
            sourceWorkspaceCapturedAtLabel = DateTimeOffset.Now.ToString(
                "HH:mm:ss",
                CultureInfo.InvariantCulture);
            sourceWorkspaceDecisionsValid = false;
            Repaint();
        }

        private void CancelSourceWorkspaceCapture()
        {
            sourceWorkspaceCaptureCancellation?.Cancel();
            DisposeSourceWorkspaceCancellation();
        }

        private void DisposeSourceWorkspaceCancellation()
        {
            sourceWorkspaceCaptureCancellation?.Dispose();
            sourceWorkspaceCaptureCancellation = null;
        }

        private void DrawSourceQualification()
        {
            showSourceQualification = BuildInspectorUi.DrawFoldoutHeader(
                "Source Qualification",
                showSourceQualification,
                BuildInspectorUi.SafetyColor,
                GetSourceQualificationInspectorStatus(),
                "Read-only source-control evidence and the effective Release, Development, and Local Optimized Preview policies.");
            if (!showSourceQualification)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawResponsivePropertyField(
                sourceCleanlinessPolicy,
                SourceCleanlinessPolicyLabel);
            RefreshSourceWorkspaceDecisionsIfRequired();

            BuildInspectorUi.DrawStatusRow(
                "Provider",
                sourceWorkspaceProviderLabel,
                sourceWorkspaceEvidence == null
                    ? BuildInspectorTone.Neutral
                    : BuildInspectorTone.Info);
            BuildInspectorUi.DrawStatusRow(
                "Overall",
                GetSourceWorkspaceStatusLabel(),
                GetSourceWorkspaceTone());
            DrawSourceWorkspaceComponent(
                "Tracked Changes",
                sourceWorkspaceEvidence?.TrackedChanges);
            DrawSourceWorkspaceComponent(
                "Untracked Changes",
                sourceWorkspaceEvidence?.UntrackedChanges);
            DrawSourceWorkspaceComponent(
                "Submodules",
                sourceWorkspaceEvidence?.Submodules);
            DrawSourceWorkspaceComponent(
                "Git LFS",
                sourceWorkspaceEvidence?.GitLfs);
            BuildInspectorUi.DrawStatusRow(
                "Last Checked",
                sourceWorkspaceCapturedAtLabel,
                BuildInspectorTone.Neutral);

            BuildInspectorUi.DrawSubsectionLabel("Effective Build Policy");
            if (sourceWorkspacePreviewCanGate)
            {
                if (IsDirtyLocalReleasePolicy())
                {
                    DrawSourceWorkspaceDecision(
                        "Qualified Release",
                        releaseSourceWorkspaceDecision);
                    DrawSourceWorkspaceDecision(
                        "Local Dirty Release",
                        localPreviewSourceWorkspaceDecision);
                }
                else
                {
                    DrawSourceWorkspaceDecision("Release", releaseSourceWorkspaceDecision);
                }

                DrawSourceWorkspaceDecision("Development", developmentSourceWorkspaceDecision);
                if (!IsDirtyLocalReleasePolicy())
                {
                    DrawSourceWorkspaceDecision(
                        "Local Optimized Preview",
                        localPreviewSourceWorkspaceDecision);
                }
            }
            else
            {
                BuildInspectorUi.DrawStatusRow(
                    IsDirtyLocalReleasePolicy() ? "Qualified Release" : "Release",
                    "Runner validation",
                    BuildInspectorTone.Warning);
                BuildInspectorUi.DrawStatusRow(
                    "Development",
                    "Runner validation",
                    BuildInspectorTone.Warning);
                BuildInspectorUi.DrawStatusRow(
                    IsDirtyLocalReleasePolicy()
                        ? "Local Dirty Release"
                        : "Local Optimized Preview",
                    "Local only",
                    BuildInspectorTone.Warning);
            }

            if (!string.IsNullOrWhiteSpace(sourceWorkspaceInspectionError))
            {
                BuildInspectorUi.DrawNotice(
                    sourceWorkspaceInspectionError,
                    BuildInspectorTone.Error);
            }

            IReadOnlyList<BuildInspectorCommand> refreshCommands =
                sourceWorkspaceCaptureTask == null
                    ? RefreshSourceWorkspaceCommands
                    : CheckingSourceWorkspaceCommands;
            if (BuildInspectorUi.DrawCommandGrid(refreshCommands, maximumColumns: 1) == 0)
            {
                RequestSourceWorkspaceRefresh();
            }

            BuildInspectorUi.DrawMutedText(
                "Qualified Release, focused non-Development actions, and batch-mode builds require a fresh " +
                "verified-clean source workspace. Allow Dirty Development is an explicit local exception. " +
                "Allow Dirty Local Release routes the interactive Release action to an isolated, " +
                "non-distributable Clean Player. The build runner captures authoritative " +
                "evidence at preflight and requalifies protected builds again before publication.");
            BuildInspectorUi.EndPanel();
        }

        private bool IsDirtyLocalReleasePolicy()
        {
            return sourceCleanlinessPolicy.enumValueIndex ==
                   (int)BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease;
        }

        private BuildInteractiveReleaseRoute GetInteractiveReleaseRoute(
            bool localPreviewAvailable)
        {
            RefreshSourceWorkspaceDecisionsIfRequired();
            bool qualifiedReleaseAllowed = sourceWorkspacePreviewCanGate
                ? releaseSourceWorkspaceDecision.Allowed
                : !IsDirtyLocalReleasePolicy();
            bool effectiveLocalPreviewAvailable = localPreviewAvailable
                && (!sourceWorkspacePreviewCanGate
                    || localPreviewSourceWorkspaceDecision.Allowed);
            return BuildSourceWorkspacePolicy.ResolveInteractiveReleaseRoute(
                (BuildSourceCleanlinessPolicy)sourceCleanlinessPolicy.enumValueIndex,
                qualifiedReleaseAllowed,
                effectiveLocalPreviewAvailable);
        }

        private BuildSourceWorkspaceDecision GetSourceWorkspaceDecision(bool debugBuild)
        {
            RefreshSourceWorkspaceDecisionsIfRequired();
            return debugBuild
                ? developmentSourceWorkspaceDecision
                : releaseSourceWorkspaceDecision;
        }

        private bool IsSourceWorkspacePreviewAllowed(bool debugBuild)
        {
            return !sourceWorkspacePreviewCanGate
                   || GetSourceWorkspaceDecision(debugBuild).Allowed;
        }

        private string GetSourceWorkspaceBlockedReason(bool debugBuild)
        {
            BuildSourceWorkspaceDecision decision = GetSourceWorkspaceDecision(debugBuild);
            return !sourceWorkspacePreviewCanGate || decision.Allowed
                ? string.Empty
                : "Source qualification blocks this build. " + decision.Summary;
        }

        private void RefreshSourceWorkspaceDecisionsIfRequired()
        {
            int policyValue = sourceCleanlinessPolicy.enumValueIndex;
            VersionControlWorkspaceEvidence decisionEvidence =
                sourceWorkspaceCaptureTask == null
                    ? sourceWorkspaceEvidence
                    : null;
            if (sourceWorkspaceDecisionsValid
                && sourceWorkspaceDecisionPolicyValue == policyValue
                && ReferenceEquals(sourceWorkspaceDecisionEvidence, decisionEvidence))
            {
                return;
            }

            var policy = (BuildSourceCleanlinessPolicy)policyValue;
            releaseSourceWorkspaceDecision = BuildSourceWorkspacePolicy.Evaluate(
                batchMode: false,
                debugBuild: false,
                policy,
                decisionEvidence);
            developmentSourceWorkspaceDecision = BuildSourceWorkspacePolicy.Evaluate(
                batchMode: false,
                debugBuild: true,
                policy,
                decisionEvidence);
            localPreviewSourceWorkspaceDecision = BuildSourceWorkspacePolicy.Evaluate(
                batchMode: false,
                BuildPurpose.LocalReleasePreview,
                policy,
                decisionEvidence);
            sourceWorkspaceDecisionPolicyValue = policyValue;
            sourceWorkspaceDecisionEvidence = decisionEvidence;
            sourceWorkspaceDecisionsValid = true;
        }

        private BuildInspectorStatus GetSourceQualificationInspectorStatus()
        {
            if (sourceWorkspaceCaptureTask != null)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Busy, "CHECKING");
            }

            RefreshSourceWorkspaceDecisionsIfRequired();
            if (!sourceWorkspacePreviewCanGate)
            {
                return new BuildInspectorStatus(
                    BuildInspectorTone.Warning,
                    "RUNNER CHECK");
            }

            if (releaseSourceWorkspaceDecision.Allowed)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Ready, "VERIFIED CLEAN");
            }

            if (IsDirtyLocalReleasePolicy()
                && localPreviewSourceWorkspaceDecision.Allowed)
            {
                return new BuildInspectorStatus(
                    BuildInspectorTone.Warning,
                    "LOCAL RELEASE");
            }

            if (developmentSourceWorkspaceDecision.Allowed)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Warning, "DEV ONLY");
            }

            if (localPreviewSourceWorkspaceDecision.Allowed)
            {
                return new BuildInspectorStatus(
                    BuildInspectorTone.Warning,
                    "LOCAL PREVIEW");
            }

            return new BuildInspectorStatus(BuildInspectorTone.Error, "BLOCKED");
        }

        private string GetSourceWorkspaceStatusLabel()
        {
            if (sourceWorkspaceCaptureTask != null)
            {
                return "Checking";
            }

            if (sourceWorkspaceEvidence == null)
            {
                return "Not checked";
            }

            if (!sourceWorkspacePreviewCanGate)
            {
                return "Precheck unavailable";
            }

            if (!string.Equals(
                    sourceWorkspaceEvidence.FailureCode,
                    VersionControlWorkspaceEvidence.NoFailure,
                    StringComparison.Ordinal))
            {
                return "Unknown (" + sourceWorkspaceEvidence.FailureCode + ")";
            }

            return sourceWorkspaceEvidence.IsVerifiedClean
                ? "Verified clean"
                : sourceWorkspaceEvidence.OverallStatus.ToString();
        }

        private BuildInspectorTone GetSourceWorkspaceTone()
        {
            if (sourceWorkspaceCaptureTask != null)
            {
                return BuildInspectorTone.Busy;
            }

            if (sourceWorkspaceEvidence == null
                || !string.Equals(
                    sourceWorkspaceEvidence.FailureCode,
                    VersionControlWorkspaceEvidence.NoFailure,
                    StringComparison.Ordinal))
            {
                return sourceWorkspacePreviewCanGate
                    ? BuildInspectorTone.Error
                    : BuildInspectorTone.Warning;
            }

            return sourceWorkspaceEvidence.IsVerifiedClean
                ? BuildInspectorTone.Ready
                : sourceWorkspaceEvidence.OverallStatus ==
                  VersionControlWorkspaceComponentStatus.Dirty
                    ? BuildInspectorTone.Warning
                    : BuildInspectorTone.Error;
        }

        private static void DrawSourceWorkspaceComponent(
            string label,
            VersionControlWorkspaceComponentEvidence component)
        {
            if (component == null)
            {
                BuildInspectorUi.DrawStatusRow(
                    label,
                    "Unknown",
                    BuildInspectorTone.Error);
                return;
            }

            string value = component.ChangeCount.HasValue
                ? component.Status + " (" + component.ChangeCount.Value + ")"
                : component.Status.ToString();
            BuildInspectorUi.DrawStatusRow(
                label,
                value,
                GetSourceWorkspaceComponentTone(component.Status));
        }

        private static BuildInspectorTone GetSourceWorkspaceComponentTone(
            VersionControlWorkspaceComponentStatus status)
        {
            switch (status)
            {
                case VersionControlWorkspaceComponentStatus.Clean:
                    return BuildInspectorTone.Ready;
                case VersionControlWorkspaceComponentStatus.Dirty:
                    return BuildInspectorTone.Warning;
                case VersionControlWorkspaceComponentStatus.NotApplicable:
                    return BuildInspectorTone.Neutral;
                default:
                    return BuildInspectorTone.Error;
            }
        }

        private static void DrawSourceWorkspaceDecision(
            string label,
            BuildSourceWorkspaceDecision decision)
        {
            BuildInspectorUi.DrawStatusRow(
                label,
                decision.Allowed ? "Allowed" : "Blocked",
                decision.Allowed
                    ? decision.RequiresVerifiedClean
                        ? BuildInspectorTone.Ready
                        : BuildInspectorTone.Warning
                    : BuildInspectorTone.Error,
                decision.Summary);
        }

        private static string GetSourceWorkspaceProviderLabel(
            IVersionControlProvider provider)
        {
            string typeName = provider.GetType().Name;
            if (typeName.IndexOf("Git", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Git";
            }

            if (typeName.IndexOf("Perforce", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Perforce";
            }

            return typeName;
        }

        private sealed class SourceWorkspaceCaptureResult
        {
            internal SourceWorkspaceCaptureResult(
                int generation,
                VersionControlWorkspaceEvidence evidence,
                string error,
                DateTimeOffset capturedAtUtc)
            {
                Generation = generation;
                Evidence = evidence;
                Error = error;
                CapturedAtUtc = capturedAtUtc;
            }

            internal int Generation { get; }
            internal VersionControlWorkspaceEvidence Evidence { get; }
            internal string Error { get; }
            internal DateTimeOffset CapturedAtUtc { get; }
        }
    }
}
