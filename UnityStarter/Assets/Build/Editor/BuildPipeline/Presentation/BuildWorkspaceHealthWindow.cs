using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Project-level diagnostics and explicit recovery UI for durable build workspace state.
    /// </summary>
    public sealed class BuildWorkspaceHealthWindow : EditorWindow
    {
        private const int MaximumDisplayedIssues = 256;

        private BuildWorkspaceSnapshot snapshot;
        private Vector2 issueScrollPosition;
        private string inspectionError;
        private string recoveryError;
        private bool snapshotIsStale;
        private bool recoveryIsRunning;

        private GUIStyle titleStyle;
        private GUIStyle sectionHeaderStyle;
        private GUIStyle issueTitleStyle;
        private GUIStyle metadataLabelStyle;

        [MenuItem("Build/Pipeline/Workspace Health", priority = 5)]
        public static void ShowWindow()
        {
            BuildWorkspaceHealthWindow window = GetWindow<BuildWorkspaceHealthWindow>();
            window.titleContent = new GUIContent(
                "Build Workspace",
                "Inspect and recover durable build pipeline state.");
            window.minSize = new Vector2(520f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(
                "Build Workspace",
                "Inspect and recover durable build pipeline state.");
            minSize = new Vector2(520f, 360f);
            CreateStyles();
            EditorApplication.projectChanged += HandleProjectChanged;
            RefreshSnapshot();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= HandleProjectChanged;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();
            EditorGUILayout.Space(6f);
            DrawSnapshot();
            EditorGUILayout.Space(8f);
            DrawIssues();
            EditorGUILayout.Space(8f);
            DrawActions();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Build Workspace Health", titleStyle);
            EditorGUILayout.LabelField(
                "Inspect project-level transaction evidence before starting another build. "
                + "Recovery is explicit and never force-deletes unknown state.",
                EditorStyles.wordWrappedLabel);
        }

        private void DrawSnapshot()
        {
            EditorGUILayout.LabelField("Status", sectionHeaderStyle);
            if (snapshot == null)
            {
                EditorGUILayout.HelpBox(
                    GetUnavailableSnapshotMessage(),
                    MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField(
                "Health",
                ObjectNames.NicifyVariableName(snapshot.Status.ToString()));
            EditorGUILayout.HelpBox(
                GetSnapshotSummary(snapshot),
                GetMessageType(snapshot.Status));

            if (snapshotIsStale)
            {
                EditorGUILayout.HelpBox(
                    "Project state changed after this snapshot was captured. Refresh before recovery.",
                    MessageType.Warning);
            }

            DrawReadOnlyValue("Snapshot Token", snapshot.Token);
            EditorGUILayout.LabelField("Can Recover", snapshot.CanRecover ? "Yes" : "No");

            if (!string.IsNullOrWhiteSpace(inspectionError))
            {
                EditorGUILayout.HelpBox(inspectionError, MessageType.Error);
            }

            if (!string.IsNullOrWhiteSpace(recoveryError))
            {
                EditorGUILayout.HelpBox(recoveryError, MessageType.Error);
            }
        }

        private void DrawIssues()
        {
            EditorGUILayout.LabelField("Issues", sectionHeaderStyle);
            IReadOnlyList<BuildWorkspaceIssue> issues = snapshot?.Issues;
            if (issues == null || issues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    snapshot == null
                        ? "Refresh the workspace snapshot to inspect transaction evidence."
                        : "No workspace issues were reported.",
                    snapshot == null ? MessageType.None : MessageType.Info);
                return;
            }

            issueScrollPosition = EditorGUILayout.BeginScrollView(
                issueScrollPosition,
                GUILayout.ExpandHeight(true));

            int displayCount = Math.Min(issues.Count, MaximumDisplayedIssues);
            for (int index = 0; index < displayCount; index++)
            {
                BuildWorkspaceIssue issue = issues[index];
                if (issue == null)
                {
                    continue;
                }

                DrawIssue(issue);
                if (index + 1 < displayCount)
                {
                    EditorGUILayout.Space(4f);
                }
            }

            if (issues.Count > displayCount)
            {
                EditorGUILayout.HelpBox(
                    $"Only the first {MaximumDisplayedIssues} of {issues.Count} issues are displayed. "
                    + "Resolve the visible issues or inspect the build log for the complete bounded report.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawIssue(BuildWorkspaceIssue issue)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            string participant = string.IsNullOrWhiteSpace(issue.ParticipantId)
                ? "Workspace"
                : issue.ParticipantId;
            string title = string.IsNullOrWhiteSpace(issue.Title)
                ? "Build workspace issue"
                : issue.Title;
            EditorGUILayout.LabelField(
                $"{GetSeverityLabel(issue.Severity)}  {title}",
                issueTitleStyle);
            EditorGUILayout.LabelField("Participant", participant, metadataLabelStyle);

            if (!string.IsNullOrWhiteSpace(issue.Message))
            {
                EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);
            }

            DrawOptionalMetadata("Transaction", issue.TransactionId);
            DrawOptionalMetadata("Phase", issue.Phase);
            DrawOptionalMetadata("Required Target", issue.RequiredBuildTarget);
            if (!string.IsNullOrWhiteSpace(issue.EvidencePath))
            {
                DrawReadOnlyValue("Evidence", issue.EvidencePath);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("Actions", sectionHeaderStyle);
            bool editorIsBusy = IsEditorBusy();
            if (editorIsBusy)
            {
                EditorGUILayout.HelpBox(
                    "Recovery is disabled while Unity is compiling, updating assets, entering Play Mode, "
                    + "or building a Player.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(recoveryIsRunning))
            {
                if (GUILayout.Button("Refresh", GUILayout.MinHeight(26f)))
                {
                    RefreshSnapshot();
                }
            }

            bool canRecover = snapshot != null
                && snapshot.Status == BuildWorkspaceHealthStatus.RecoveryRequired
                && snapshot.CanRecover
                && !snapshotIsStale
                && !editorIsBusy
                && !recoveryIsRunning
                && !string.IsNullOrWhiteSpace(snapshot.Token);
            using (new EditorGUI.DisabledScope(!canRecover))
            {
                if (GUILayout.Button("Recover", GUILayout.MinHeight(26f)))
                {
                    ConfirmAndRecover();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (snapshot != null
                && snapshot.Status == BuildWorkspaceHealthStatus.RecoveryRequired
                && !snapshot.CanRecover)
            {
                EditorGUILayout.HelpBox(
                    "Automatic recovery is not authorized for this snapshot. Preserve the evidence and resolve "
                    + "the reported blocking issue; this window does not provide force recovery.",
                    MessageType.Error);
            }
        }

        private void ConfirmAndRecover()
        {
            if (snapshot == null || snapshotIsStale || string.IsNullOrWhiteSpace(snapshot.Token))
            {
                RefreshSnapshot();
                return;
            }

            string expectedToken = snapshot.Token;
            bool confirmed = EditorUtility.DisplayDialog(
                "Recover Build Workspace",
                "Recovery will use the durable transaction journals shown in this snapshot to restore or "
                + "finalize build-owned state. Unknown or changed evidence will remain untouched and cause "
                + "recovery to fail closed.\n\n"
                + $"Expected snapshot token:\n{expectedToken}\n\n"
                + "Continue with recovery?",
                "Recover",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            recoveryIsRunning = true;
            recoveryError = null;
            try
            {
                snapshot = BuildWorkspaceService.Recover(expectedToken);
                snapshotIsStale = false;
                inspectionError = null;
                if (snapshot == null)
                {
                    inspectionError =
                        "Recovery completed without returning a workspace health snapshot. Refresh before building.";
                }
            }
            catch (Exception exception)
            {
                recoveryError = "Build workspace recovery failed:\n" + exception.Message;
                Debug.LogException(exception);
                RefreshAfterRecoveryFailure();
            }
            finally
            {
                recoveryIsRunning = false;
                Repaint();
            }
        }

        private void RefreshSnapshot()
        {
            inspectionError = null;
            recoveryError = null;
            try
            {
                snapshot = BuildWorkspaceService.Inspect();
                snapshotIsStale = false;
                if (snapshot == null)
                {
                    inspectionError = "Workspace inspection returned no snapshot.";
                }
            }
            catch (Exception exception)
            {
                snapshot = null;
                snapshotIsStale = false;
                inspectionError = "Build workspace inspection failed:\n" + exception.Message;
                Debug.LogException(exception);
            }

            Repaint();
        }

        private void RefreshAfterRecoveryFailure()
        {
            try
            {
                snapshot = BuildWorkspaceService.Inspect();
                snapshotIsStale = false;
                if (snapshot == null)
                {
                    inspectionError = "Workspace inspection returned no snapshot after recovery failed.";
                }
            }
            catch (Exception exception)
            {
                snapshot = null;
                snapshotIsStale = false;
                inspectionError =
                    "Workspace inspection also failed after recovery failed:\n" + exception.Message;
                Debug.LogException(exception);
            }
        }

        private void HandleProjectChanged()
        {
            if (snapshot != null && !recoveryIsRunning)
            {
                snapshotIsStale = true;
                Repaint();
            }
        }

        private static string GetSnapshotSummary(BuildWorkspaceSnapshot value)
        {
            if (!string.IsNullOrWhiteSpace(value.Summary))
            {
                return value.Summary;
            }

            switch (value.Status)
            {
                case BuildWorkspaceHealthStatus.Clean:
                    return "The build workspace is clean.";
                case BuildWorkspaceHealthStatus.RecoveryRequired:
                    return "Durable build transaction state requires recovery before another build.";
                case BuildWorkspaceHealthStatus.Blocked:
                    return "The build workspace is blocked. Preserve the reported evidence and resolve the issue.";
                case BuildWorkspaceHealthStatus.Busy:
                    return "The build workspace is currently busy.";
                default:
                    return "The build workspace returned an unknown health status.";
            }
        }

        private string GetUnavailableSnapshotMessage()
        {
            if (!string.IsNullOrWhiteSpace(recoveryError)
                && !string.IsNullOrWhiteSpace(inspectionError))
            {
                return recoveryError + "\n\n" + inspectionError;
            }

            if (!string.IsNullOrWhiteSpace(recoveryError))
            {
                return recoveryError;
            }

            return string.IsNullOrWhiteSpace(inspectionError)
                ? "No workspace health snapshot is available."
                : inspectionError;
        }

        private static MessageType GetMessageType(BuildWorkspaceHealthStatus status)
        {
            switch (status)
            {
                case BuildWorkspaceHealthStatus.Clean:
                    return MessageType.Info;
                case BuildWorkspaceHealthStatus.RecoveryRequired:
                case BuildWorkspaceHealthStatus.Busy:
                    return MessageType.Warning;
                case BuildWorkspaceHealthStatus.Blocked:
                default:
                    return MessageType.Error;
            }
        }

        private static string GetSeverityLabel(BuildWorkspaceIssueSeverity severity)
        {
            switch (severity)
            {
                case BuildWorkspaceIssueSeverity.Info:
                    return "INFO";
                case BuildWorkspaceIssueSeverity.Warning:
                    return "WARNING";
                case BuildWorkspaceIssueSeverity.Error:
                default:
                    return "ERROR";
            }
        }

        private static bool IsEditorBusy()
        {
            return EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode
                || UnityEditor.BuildPipeline.isBuildingPlayer;
        }

        private static void DrawOptionalMetadata(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                EditorGUILayout.LabelField(label, value);
            }
        }

        private static void DrawReadOnlyValue(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(
                string.IsNullOrWhiteSpace(value) ? "-" : value,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (titleStyle == null
                || sectionHeaderStyle == null
                || issueTitleStyle == null
                || metadataLabelStyle == null)
            {
                CreateStyles();
            }
        }

        private void CreateStyles()
        {
            titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontStyle = FontStyle.Bold
            };
            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
            issueTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true
            };
            metadataLabelStyle = new GUIStyle(EditorStyles.miniLabel);
        }
    }
}
