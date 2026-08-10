using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    [CustomEditor(typeof(DataTableLubanSettings))]
    [CanEditMultipleObjects]
    public sealed class DataTableLubanSettingsEditor : UnityEditor.Editor
    {
        private const int MaximumRenderedOutputCharacters = 32 * 1024;

        private static readonly HashSet<string> OwnedSerializedFields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "m_Script",
                "schemaVersion",
                "buildConfigurationPath",
                "defaultProfileName",
                "refreshAssetsAfterSuccess",
                "maximumCapturedOutputCharacters",
            };

        private SerializedProperty _buildConfigurationPath;
        private SerializedProperty _defaultProfileName;
        private SerializedProperty _refreshAssetsAfterSuccess;
        private SerializedProperty _maximumCapturedOutputCharacters;
        private string _contractError = string.Empty;
        private bool _showReadiness = true;
        private bool _showSetup = true;
        private bool _showProfile = true;
        private bool _showIssues = true;
        private bool _showToolchain;
        private bool _showActions = true;
        private bool _showLastResult = true;
        private bool _showOutput;
        private bool _savedSettingsThisFrame;

        private void OnEnable()
        {
            _buildConfigurationPath = serializedObject.FindProperty("buildConfigurationPath");
            _defaultProfileName = serializedObject.FindProperty("defaultProfileName");
            _refreshAssetsAfterSuccess = serializedObject.FindProperty("refreshAssetsAfterSuccess");
            _maximumCapturedOutputCharacters = serializedObject.FindProperty(
                "maximumCapturedOutputCharacters");
            if (!TryValidateSerializedFieldOwnership(serializedObject, out _contractError))
            {
                return;
            }

            DataTableLubanAuthoringCoordinator.Changed += HandleCoordinatorChanged;
            EditorApplication.update += RepaintWhileActive;
            if (!serializedObject.isEditingMultipleObjects)
            {
                DataTableLubanAuthoringCoordinator.Observe(
                    this,
                    (DataTableLubanSettings)target);
            }
        }

        private void OnDisable()
        {
            DataTableLubanAuthoringCoordinator.StopObserving(this);
            DataTableLubanAuthoringCoordinator.Changed -= HandleCoordinatorChanged;
            EditorApplication.update -= RepaintWhileActive;
        }

        public override void OnInspectorGUI()
        {
            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                DrawInspectorContents();
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        private void DrawInspectorContents()
        {
            serializedObject.Update();
            if (!string.IsNullOrEmpty(_contractError))
            {
                DrawContractFailure();
                return;
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                DrawMultipleSelection();
                return;
            }

            var settings = (DataTableLubanSettings)target;
            string previousConfiguration = settings.BuildConfigurationPath;
            string previousProfile = settings.SelectedProfileName;
            _savedSettingsThisFrame = false;
            DataTableLubanAuthoringSnapshot snapshot = DataTableLubanAuthoringCoordinator.Snapshot;
            bool running = DataTableLubanAuthoringCoordinator.IsOperationInProgress;
            DataTableLubanAuthoringState effectiveState = running
                ? DataTableLubanAuthoringState.Busy
                : snapshot.State;
            string status = running ? "RUNNING" : snapshot.StatusLabel;
            string subtitle = string.IsNullOrEmpty(snapshot.SelectedProfile.CodeOutputPath)
                ? settings.SelectedProfileName
                : settings.SelectedProfileName + "  ->  " + snapshot.SelectedProfile.CodeOutputPath;
            DataTableLubanInspectorUi.DrawHero(
                "Luban Data Pipeline",
                subtitle,
                status,
                DataTableLubanInspectorUi.GetStateTone(effectiveState));

            DrawReadiness(snapshot, effectiveState);
            bool settingsChanged = DrawSetup(settings, snapshot);
            DrawSelectedProfile(snapshot);
            DrawIssues(snapshot);
            DrawToolchain(snapshot);
            DrawActions(settings, snapshot, running);
            DrawLastOperation();
            DrawQuickStart(settings, snapshot);

            bool applied = serializedObject.ApplyModifiedProperties();
            if ((settingsChanged || applied) && !_savedSettingsThisFrame)
            {
                bool requiresDeepInspection =
                    !string.Equals(
                        previousConfiguration,
                        settings.BuildConfigurationPath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        previousProfile,
                        settings.SelectedProfileName,
                        StringComparison.Ordinal);
                DataTableLubanAuthoringCoordinator.NotifySettingsChanged(
                    settings,
                    requiresDeepInspection);
            }
        }

        internal static bool TryValidateSerializedFieldOwnership(
            SerializedObject value,
            out string error)
        {
            if (value == null)
            {
                error = "SerializedObject is required.";
                return false;
            }

            var unknown = new List<string>();
            SerializedProperty iterator = value.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (!OwnedSerializedFields.Contains(iterator.propertyPath))
                {
                    unknown.Add(iterator.propertyPath);
                }
            }

            string[] required =
            {
                "schemaVersion",
                "buildConfigurationPath",
                "defaultProfileName",
                "refreshAssetsAfterSuccess",
                "maximumCapturedOutputCharacters",
            };
            for (int index = 0; index < required.Length; index++)
            {
                if (value.FindProperty(required[index]) == null)
                {
                    unknown.Add("missing:" + required[index]);
                }
            }

            if (unknown.Count == 0)
            {
                error = string.Empty;
                return true;
            }

            error =
                "The DataTableLubanSettings Inspector does not own every serialized field:\n" +
                string.Join("\n", unknown);
            return false;
        }

        private void DrawReadiness(
            DataTableLubanAuthoringSnapshot snapshot,
            DataTableLubanAuthoringState effectiveState)
        {
            _showReadiness = DataTableLubanInspectorUi.DrawSection(
                "Pipeline Readiness",
                _showReadiness,
                DataTableLubanInspectorUi.SafetyColor,
                effectiveState == DataTableLubanAuthoringState.Busy
                    ? "RUNNING"
                    : snapshot.StatusLabel,
                DataTableLubanInspectorUi.GetStateTone(effectiveState),
                "Read-only projection of authoring, toolchain, output, and transaction state.");
            if (!_showReadiness)
            {
                return;
            }

            DataTableLubanInspectorUi.BeginPanel();
            DataTableLubanInspectorUi.DrawStatusRow(
                "Settings Asset",
                snapshot.SettingsAssetCount == 1
                    ? "Single"
                    : snapshot.SettingsAssetCount + " assets",
                snapshot.SettingsAssetCount == 1
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Error);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Authoring",
                snapshot.SettingsDirty ? "Unsaved" : "Saved",
                snapshot.SettingsDirty
                    ? DataTableLubanInspectorTone.Warning
                    : DataTableLubanInspectorTone.Ready);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Configuration",
                snapshot.IsInspectionPending
                    ? "Inspecting"
                    : string.IsNullOrEmpty(snapshot.InspectionError) ? "Parsed" : "Invalid",
                snapshot.IsInspectionPending
                    ? DataTableLubanInspectorTone.Busy
                    : string.IsNullOrEmpty(snapshot.InspectionError)
                        ? DataTableLubanInspectorTone.Ready
                        : DataTableLubanInspectorTone.Error);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Profile",
                snapshot.SelectedProfileName,
                string.IsNullOrEmpty(snapshot.SelectedProfile.Name)
                    ? DataTableLubanInspectorTone.Error
                    : DataTableLubanInspectorTone.Ready);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Luban Identity",
                snapshot.Toolchain.LubanIdentityStatus,
                IsReadyValue(snapshot.Toolchain.LubanIdentityStatus, "approved")
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Warning);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Source Fingerprint",
                snapshot.Toolchain.SourceFingerprintStatus,
                IsReadyValue(snapshot.Toolchain.SourceFingerprintStatus, "current")
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Warning);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Published Output",
                GetPublishedOutputDisplayValue(
                    snapshot.Output.State,
                    snapshot.Transaction.State,
                    snapshot.Transaction.RecoveryRequired,
                    snapshot.IsInspectionPending),
                GetOutputTone(
                    snapshot.Output.State,
                    snapshot.Transaction.State,
                    snapshot.IsInspectionPending));
            DataTableLubanInspectorUi.DrawStatusRow(
                "Transaction",
                snapshot.Transaction.State,
                snapshot.Transaction.RecoveryRequired
                    ? DataTableLubanInspectorTone.Error
                    : string.Equals(snapshot.Transaction.State, "active", StringComparison.Ordinal)
                        ? DataTableLubanInspectorTone.Busy
                        : DataTableLubanInspectorTone.Ready);
            DataTableLubanInspectorUi.EndPanel();
        }

        private bool DrawSetup(
            DataTableLubanSettings settings,
            DataTableLubanAuthoringSnapshot snapshot)
        {
            _showSetup = DataTableLubanInspectorUi.DrawSection(
                "Project Setup",
                _showSetup,
                DataTableLubanInspectorUi.SetupColor,
                snapshot.SettingsDirty ? "UNSAVED" : "CONFIGURED",
                snapshot.SettingsDirty
                    ? DataTableLubanInspectorTone.Warning
                    : DataTableLubanInspectorTone.Info);
            if (!_showSetup)
            {
                return false;
            }

            bool changed = false;
            DataTableLubanInspectorUi.BeginPanel();
            EditorGUILayout.LabelField("Build Configuration", EditorStyles.miniBoldLabel);
            DataTableLubanFieldActionLayout configurationLayout =
                DataTableLubanInspectorUi.GetFieldActionLayout(EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(
                configurationLayout.FieldRect,
                _buildConfigurationPath,
                GUIContent.none);
            if (GUI.Button(configurationLayout.FirstActionRect, "Browse"))
            {
                changed |= BrowseConfiguration(_buildConfigurationPath);
            }

            if (GUI.Button(configurationLayout.SecondActionRect, "Reset"))
            {
                _buildConfigurationPath.stringValue =
                    DataTableLubanSettings.DefaultBuildConfigurationPath;
                changed = true;
            }

            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "Resolved Path",
                snapshot.ConfigurationPath,
                showReveal: true,
                tooltip: "Read-only absolute path derived from Build Configuration.");

            EditorGUILayout.Space(4f);
            changed |= DrawProfilePopup(snapshot);
            DrawSerializedToggleLeft(
                _refreshAssetsAfterSuccess,
                new GUIContent(
                    "Refresh After Success",
                    "Refresh the AssetDatabase after successful generation or recovery."));
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Captured Output Limit",
                    "Combined stdout/stderr characters retained for the last Editor operation."),
                EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(
                _maximumCapturedOutputCharacters,
                GUIContent.none);

            EditorGUILayout.Space(4f);
            DataTableLubanDualButtonLayout settingsActions =
                DataTableLubanInspectorUi.GetDualButtonLayout(EditorGUIUtility.singleLineHeight);
            if (GUI.Button(settingsActions.FirstRect, "Ping Settings"))
            {
                EditorGUIUtility.PingObject(settings);
            }

            using (new EditorGUI.DisabledScope(!EditorUtility.IsDirty(settings)))
            {
                if (GUI.Button(settingsActions.SecondRect, "Save Settings"))
                {
                    serializedObject.ApplyModifiedProperties();
                    _savedSettingsThisFrame = true;
                    DataTableLubanAuthoring.SaveSettings(settings);
                }
            }

            DataTableLubanInspectorUi.EndPanel();
            return changed;
        }

        private bool DrawProfilePopup(DataTableLubanAuthoringSnapshot snapshot)
        {
            EditorGUILayout.LabelField("Default Profile", EditorStyles.miniBoldLabel);
            if (snapshot.Profiles.Length == 0)
            {
                EditorGUILayout.PropertyField(
                    _defaultProfileName,
                    GUIContent.none);
                return false;
            }

            string current = _defaultProfileName.stringValue ?? string.Empty;
            var profileLabels = new string[snapshot.Profiles.Length];
            int selectedIndex = -1;
            for (int index = 0; index < snapshot.Profiles.Length; index++)
            {
                DataTableLubanProfileSnapshot profile = snapshot.Profiles[index];
                profileLabels[index] = profile.Name + "  (" + profile.CodeTarget + "/" +
                                       profile.DataTarget + ", " + profile.LineEnding + ")";
                if (string.Equals(profile.Name, current, StringComparison.Ordinal))
                {
                    selectedIndex = index;
                }
            }

            if (selectedIndex < 0)
            {
                DataTableLubanInspectorUi.DrawNotice(
                    "PROFILE NOT FOUND",
                    "The configured profile was not found. Select one of the parsed profiles below.",
                    detail: null,
                    tone: DataTableLubanInspectorTone.Warning);
                var missingLabels = new string[profileLabels.Length + 1];
                missingLabels[0] = "<Missing: " + current + ">";
                Array.Copy(profileLabels, 0, missingLabels, 1, profileLabels.Length);
                int selected = EditorGUILayout.Popup(0, missingLabels);
                if (selected <= 0)
                {
                    return false;
                }

                _defaultProfileName.stringValue = snapshot.Profiles[selected - 1].Name;
                return true;
            }

            int next = EditorGUILayout.Popup(selectedIndex, profileLabels);
            if (next == selectedIndex)
            {
                return false;
            }

            _defaultProfileName.stringValue = snapshot.Profiles[next].Name;
            return true;
        }

        private void DrawSelectedProfile(DataTableLubanAuthoringSnapshot snapshot)
        {
            DataTableLubanProfileSnapshot profile = snapshot.SelectedProfile;
            _showProfile = DataTableLubanInspectorUi.DrawSection(
                "Selected Profile",
                _showProfile,
                DataTableLubanInspectorUi.ProfileColor,
                string.IsNullOrEmpty(profile.Name) ? "MISSING" : profile.Name.ToUpperInvariant(),
                string.IsNullOrEmpty(profile.Name)
                    ? DataTableLubanInspectorTone.Error
                    : DataTableLubanInspectorTone.Info);
            if (!_showProfile)
            {
                return;
            }

            DataTableLubanInspectorUi.BeginPanel();
            DataTableLubanInspectorUi.DrawStatusRow(
                "Targets",
                profile.CodeTarget + " / " + profile.DataTarget,
                string.IsNullOrEmpty(profile.Name)
                    ? DataTableLubanInspectorTone.Error
                    : DataTableLubanInspectorTone.Info);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Line Ending",
                profile.LineEnding,
                DataTableLubanInspectorTone.Neutral);
            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "Code Output",
                profile.CodeOutputPath,
                showReveal: true);
            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "Data Output",
                profile.DataOutputPath,
                showReveal: true);
            DataTableLubanInspectorUi.EndPanel();
        }

        private void DrawIssues(DataTableLubanAuthoringSnapshot snapshot)
        {
            int errorCount = 0;
            for (int index = 0; index < snapshot.Issues.Length; index++)
            {
                if (snapshot.Issues[index].Severity == DataTableLubanIssueSeverity.Error)
                {
                    errorCount++;
                }
            }

            _showIssues = DataTableLubanInspectorUi.DrawSection(
                "Validation Issues",
                _showIssues,
                errorCount == 0
                    ? DataTableLubanInspectorUi.SafetyColor
                    : new Color(0.70f, 0.30f, 0.22f),
                snapshot.Issues.Length == 0 ? "CLEAR" : snapshot.Issues.Length + " ISSUE(S)",
                errorCount == 0
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Error);
            if (!_showIssues)
            {
                return;
            }

            DataTableLubanInspectorUi.BeginPanel();
            if (snapshot.IsInspectionPending)
            {
                DataTableLubanInspectorUi.DrawNotice(
                    "INSPECTION",
                    "Inspecting the authoritative pipeline configuration and workspace state...",
                    detail: null,
                    tone: DataTableLubanInspectorTone.Info);
            }
            else if (snapshot.Issues.Length == 0)
            {
                DataTableLubanInspectorUi.DrawStatusRow(
                    "Configuration",
                    "No reported issues",
                    DataTableLubanInspectorTone.Ready);
            }
            else
            {
                for (int index = 0; index < snapshot.Issues.Length; index++)
                {
                    DataTableLubanAuthoringIssue issue = snapshot.Issues[index];
                    DataTableLubanInspectorUi.DrawNotice(
                        issue.Code,
                        issue.Message,
                        issue.Path,
                        DataTableLubanInspectorUi.GetIssueTone(issue.Severity));
                }
            }

            DataTableLubanInspectorUi.EndPanel();
        }

        private void DrawToolchain(DataTableLubanAuthoringSnapshot snapshot)
        {
            string unavailableDisplay = GetPublishedOutputDisplayValue(
                snapshot.Output.State,
                snapshot.Transaction.State,
                snapshot.Transaction.RecoveryRequired,
                snapshot.IsInspectionPending);
            DataTableLubanInspectorTone unavailableTone = GetOutputTone(
                snapshot.Output.State,
                snapshot.Transaction.State,
                snapshot.IsInspectionPending);
            bool writerOwnsTransaction =
                string.Equals(snapshot.Output.State, "unavailable", StringComparison.Ordinal) &&
                string.Equals(snapshot.Transaction.State, "active", StringComparison.Ordinal);
            _showToolchain = DataTableLubanInspectorUi.DrawSection(
                "Advanced Toolchain",
                _showToolchain,
                DataTableLubanInspectorUi.ToolchainColor,
                string.IsNullOrEmpty(snapshot.Toolchain.State)
                    ? "UNKNOWN"
                    : snapshot.Toolchain.State.ToUpperInvariant(),
                IsReadyValue(snapshot.Toolchain.State, "ready")
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Warning);
            if (!_showToolchain)
            {
                return;
            }

            DataTableLubanInspectorUi.BeginPanel();
            DataTableLubanInspectorUi.DrawNotice(
                "READ-ONLY PROJECTION",
                "These values are a read-only projection of build_config.ini and the latest pipeline inspection.",
                detail: null,
                tone: DataTableLubanInspectorTone.Info);
            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "Source Root",
                snapshot.SourceRoot,
                showReveal: true);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Configuration SHA-256",
                Abbreviate(snapshot.ConfigurationSha256, 20),
                DataTableLubanInspectorTone.Neutral,
                snapshot.ConfigurationSha256);
            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "CodeGen Project",
                snapshot.Toolchain.CodegenProjectPath,
                showReveal: true);
            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "Luban Configuration",
                snapshot.Toolchain.LubanConfigurationPath,
                showReveal: true);
            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "Luban Executable",
                snapshot.Toolchain.LubanExecutablePath,
                showReveal: true);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Host",
                snapshot.Toolchain.UseDotNetHost ? "dotnet" : "native executable",
                DataTableLubanInspectorTone.Info);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Version",
                snapshot.Toolchain.ConfiguredVersion,
                DataTableLubanInspectorTone.Neutral);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Tool Identity",
                snapshot.Toolchain.LubanIdentityStatus,
                IsReadyValue(snapshot.Toolchain.LubanIdentityStatus, "approved")
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Warning);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Source Identity",
                snapshot.Toolchain.SourceFingerprintStatus,
                IsReadyValue(snapshot.Toolchain.SourceFingerprintStatus, "current")
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Warning);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Schema SHA-256",
                Abbreviate(snapshot.Toolchain.SchemaSha256, 20),
                DataTableLubanInspectorTone.Neutral,
                snapshot.Toolchain.SchemaSha256);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Configured Luban SHA",
                Abbreviate(snapshot.Toolchain.ConfiguredSha256, 20),
                DataTableLubanInspectorTone.Neutral,
                snapshot.Toolchain.ConfiguredSha256);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Actual Luban SHA",
                string.IsNullOrEmpty(snapshot.Toolchain.ActualSha256) &&
                string.Equals(snapshot.Output.State, "unavailable", StringComparison.Ordinal)
                    ? unavailableDisplay
                    : Abbreviate(snapshot.Toolchain.ActualSha256, 20),
                string.IsNullOrEmpty(snapshot.Toolchain.ActualSha256) &&
                string.Equals(snapshot.Output.State, "unavailable", StringComparison.Ordinal)
                    ? unavailableTone
                    : DataTableLubanInspectorTone.Neutral,
                snapshot.Toolchain.ActualSha256);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Configured Source",
                Abbreviate(snapshot.Toolchain.ConfiguredSourceFingerprint, 20),
                DataTableLubanInspectorTone.Neutral,
                snapshot.Toolchain.ConfiguredSourceFingerprint);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Actual Source",
                string.IsNullOrEmpty(snapshot.Toolchain.ActualSourceFingerprint) &&
                string.Equals(snapshot.Output.State, "unavailable", StringComparison.Ordinal)
                    ? unavailableDisplay
                    : Abbreviate(snapshot.Toolchain.ActualSourceFingerprint, 20),
                string.IsNullOrEmpty(snapshot.Toolchain.ActualSourceFingerprint) &&
                string.Equals(snapshot.Output.State, "unavailable", StringComparison.Ordinal)
                    ? unavailableTone
                    : DataTableLubanInspectorTone.Neutral,
                snapshot.Toolchain.ActualSourceFingerprint);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Process Timeout",
                snapshot.ProcessTimeoutSeconds > 0
                    ? snapshot.ProcessTimeoutSeconds + " seconds"
                    : "Unknown",
                DataTableLubanInspectorTone.Neutral);
            DataTableLubanInspectorUi.DrawReadOnlyPath(
                "Generation Receipt",
                snapshot.Output.ReceiptPath,
                showReveal: true);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Generation",
                string.IsNullOrEmpty(snapshot.Output.Generation) &&
                string.Equals(snapshot.Output.State, "unavailable", StringComparison.Ordinal)
                    ? unavailableDisplay
                    : snapshot.Output.Generation,
                string.Equals(snapshot.Output.State, "unavailable", StringComparison.Ordinal)
                    ? unavailableTone
                    : DataTableLubanInspectorTone.Neutral,
                snapshot.Output.Generation);
            if (writerOwnsTransaction)
            {
                DataTableLubanInspectorUi.DrawNotice(
                    "VALIDATION DEFERRED",
                    "Deep toolchain and receipt validation is deferred while a writer owns the publication transaction. Existing files are shown only as sampled facts.",
                    detail: null,
                    tone: DataTableLubanInspectorTone.Info);
            }

            if (snapshot.Transaction.LockExists)
            {
                DataTableLubanInspectorUi.DrawReadOnlyPath(
                    "Writer Lock",
                    snapshot.Transaction.LockPath,
                    showReveal: true);
                DataTableLubanInspectorUi.DrawStatusRow(
                    "Writer Process",
                    snapshot.Transaction.WriterProcessId > 0
                        ? snapshot.Transaction.WriterProcessId +
                          (snapshot.Transaction.WriterProcessAlive ? " (alive)" : " (not alive)")
                        : "Unknown",
                    snapshot.Transaction.WriterProcessAlive
                        ? DataTableLubanInspectorTone.Busy
                        : DataTableLubanInspectorTone.Warning);
            }

            DataTableLubanInspectorUi.EndPanel();
        }

        private void DrawActions(
            DataTableLubanSettings settings,
            DataTableLubanAuthoringSnapshot snapshot,
            bool running)
        {
            bool inspecting = DataTableLubanAuthoringCoordinator.IsInspecting;
            string actionStatus = running
                ? "RUNNING"
                : inspecting
                    ? "INSPECTING"
                    : snapshot.Transaction.RecoveryRequired || snapshot.CanRecover
                        ? "RECOVERY"
                        : snapshot.CanGenerate && snapshot.CanCheck
                            ? "READY"
                            : snapshot.CanCheck
                                ? "CHECK ONLY"
                                : "BLOCKED";
            DataTableLubanInspectorTone actionTone = running || inspecting
                ? DataTableLubanInspectorTone.Busy
                : snapshot.Transaction.RecoveryRequired || snapshot.CanRecover
                    ? DataTableLubanInspectorTone.Error
                    : snapshot.CanGenerate && snapshot.CanCheck
                        ? DataTableLubanInspectorTone.Ready
                        : snapshot.CanCheck
                            ? DataTableLubanInspectorTone.Warning
                            : DataTableLubanInspectorTone.Error;
            _showActions = DataTableLubanInspectorUi.DrawSection(
                "Pipeline Actions",
                _showActions,
                snapshot.Transaction.RecoveryRequired
                    ? new Color(0.70f, 0.30f, 0.22f)
                    : DataTableLubanInspectorUi.ActionColor,
                actionStatus,
                actionTone);
            if (!_showActions)
            {
                return;
            }

            DataTableLubanInspectorUi.BeginPanel();
            if (running)
            {
                string operation = DataTableLubanAuthoringCoordinator.IsOperationInProgress
                    ? DataTableLubanAuthoringCoordinator.CurrentOperation.ToString()
                    : "External pipeline operation";
                DataTableLubanInspectorUi.DrawStatusRow(
                    "Operation",
                    operation,
                    DataTableLubanInspectorTone.Busy);
                string phase = DataTableLubanAuthoringCoordinator.IsPreflightInProgress
                    ? "Preflight Inspection"
                    : DataTableLubanAuthoringCoordinator.RunnerPhase.ToString();
                DataTableLubanInspectorUi.DrawStatusRow(
                    "Phase",
                    phase,
                    DataTableLubanAuthoringCoordinator.RunnerPhase ==
                    DataTableLubanRunnerPhase.CancellationRequested
                        ? DataTableLubanInspectorTone.Warning
                        : DataTableLubanInspectorTone.Busy);
                if (!string.IsNullOrEmpty(DataTableLubanAuthoringCoordinator.RunnerProfileName))
                {
                    DataTableLubanInspectorUi.DrawStatusRow(
                        "Profile",
                        DataTableLubanAuthoringCoordinator.RunnerProfileName,
                        DataTableLubanInspectorTone.Neutral);
                }

                int processId = DataTableLubanAuthoringCoordinator.RunnerProcessId;
                if (processId > 0)
                {
                    DataTableLubanInspectorUi.DrawStatusRow(
                        "Process ID",
                        processId.ToString(),
                        DataTableLubanInspectorTone.Neutral);
                }

                long started = DataTableLubanAuthoringCoordinator.OperationStartedUtcTicks;
                if (started > 0)
                {
                    TimeSpan elapsed = DateTime.UtcNow - new DateTime(started, DateTimeKind.Utc);
                    DataTableLubanInspectorUi.DrawStatusRow(
                        "Elapsed",
                        elapsed.TotalSeconds.ToString("0") + " seconds",
                        DataTableLubanInspectorTone.Busy);
                }

                using (new EditorGUI.DisabledScope(!DataTableLubanAuthoringCoordinator.CanCancel))
                {
                    if (GUILayout.Button("Request Safe Cancellation", GUILayout.Height(28f)))
                    {
                        DataTableLubanAuthoring.CancelActiveOperation();
                    }
                }

                DataTableLubanInspectorUi.DrawNotice(
                    "CANCELLATION",
                    "Cancellation is cooperative and may be deferred until publication commits or rolls back safely.",
                    detail: null,
                    tone: DataTableLubanInspectorTone.Info);
                DrawAuthoringError();
                DataTableLubanInspectorUi.EndPanel();
                return;
            }

            if (snapshot.Transaction.RecoveryRequired || snapshot.CanRecover)
            {
                DataTableLubanInspectorUi.DrawNotice(
                    "RECOVERY REQUIRED",
                    "A retained transaction must be recovered before generation can continue. Recovery uses the exact run ID and configuration identity reported by the writer lock.",
                    detail: null,
                    tone: DataTableLubanInspectorTone.Error);
                DataTableLubanInspectorUi.DrawStatusRow(
                    "Recovery Run",
                    Abbreviate(snapshot.Transaction.RunId, 16),
                    DataTableLubanInspectorTone.Error,
                    snapshot.Transaction.RunId);
                if (DataTableLubanInspectorUi.DrawPrimaryButton(
                        new GUIContent("Recover Transaction"),
                        snapshot.CanRecover && !DataTableLubanAuthoringCoordinator.IsInspecting))
                {
                    DataTableLubanAuthoring.Recover(settings);
                }

                if (!snapshot.CanRecover || inspecting)
                {
                    DataTableLubanInspectorUi.DrawNotice(
                        "ACTION BLOCKED",
                        GetActionBlocker(snapshot, inspecting),
                        detail: null,
                        tone: DataTableLubanInspectorTone.Warning);
                }

                DataTableLubanInspectorUi.DrawReadOnlyPath(
                    "Transaction",
                    snapshot.Transaction.TransactionPath,
                    showReveal: true);
                DrawAuthoringError();
                DataTableLubanInspectorUi.EndPanel();
                return;
            }

            if (DataTableLubanInspectorUi.DrawPrimaryButton(
                    new GUIContent(
                        "Generate " + settings.SelectedProfileName,
                        "Reinspect, generate an isolated candidate, and publish changed files transactionally."),
                    snapshot.CanGenerate && !inspecting))
            {
                DataTableLubanAuthoring.Generate(settings);
            }

            DataTableLubanDualButtonLayout pipelineActions =
                DataTableLubanInspectorUi.GetDualButtonLayout(26f);
            using (new EditorGUI.DisabledScope(!snapshot.CanCheck || inspecting))
            {
                if (GUI.Button(pipelineActions.FirstRect, "Check Generated Output"))
                {
                    DataTableLubanAuthoring.Check(settings);
                }
            }

            using (new EditorGUI.DisabledScope(inspecting))
            {
                if (GUI.Button(pipelineActions.SecondRect, "Refresh Status"))
                {
                    DataTableLubanAuthoring.RefreshStatus(settings);
                }
            }

            if (!snapshot.CanGenerate || !snapshot.CanCheck || inspecting)
            {
                DataTableLubanInspectorUi.DrawNotice(
                    "ACTION BLOCKED",
                    GetActionBlocker(snapshot, inspecting),
                    detail: null,
                    tone: DataTableLubanInspectorTone.Warning);
            }

            DrawAuthoringError();

            DataTableLubanInspectorUi.EndPanel();
        }

        private static void DrawAuthoringError()
        {
            if (!string.IsNullOrEmpty(DataTableLubanAuthoringCoordinator.AuthoringError))
            {
                DataTableLubanInspectorUi.DrawNotice(
                    "AUTHORING ERROR",
                    DataTableLubanAuthoringCoordinator.AuthoringError,
                    detail: null,
                    tone: DataTableLubanInspectorTone.Error);
            }
        }

        private void DrawLastOperation()
        {
            if (!DataTableLubanAuthoringCoordinator.HasLastResult)
            {
                return;
            }

            DataTableLubanRunResult result = DataTableLubanAuthoringCoordinator.LastResult;
            _showLastResult = DataTableLubanInspectorUi.DrawSection(
                "Last Operation",
                _showLastResult,
                result.Success
                    ? DataTableLubanInspectorUi.SafetyColor
                    : new Color(0.70f, 0.30f, 0.22f),
                result.Success ? "SUCCEEDED" : result.RecoveryRequired ? "RECOVERY" : "FAILED",
                result.Success
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Error);
            if (!_showLastResult)
            {
                return;
            }

            DataTableLubanInspectorUi.BeginPanel();
            DataTableLubanInspectorUi.DrawStatusRow(
                "Exit Code",
                result.ExitCode.ToString(),
                result.Success
                    ? DataTableLubanInspectorTone.Ready
                    : DataTableLubanInspectorTone.Error);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Duration",
                result.DurationMilliseconds + " ms",
                DataTableLubanInspectorTone.Neutral);
            DataTableLubanInspectorUi.DrawStatusRow(
                "Output Capture",
                result.OutputTruncated ? "Truncated" : "Complete",
                result.OutputTruncated
                    ? DataTableLubanInspectorTone.Warning
                    : DataTableLubanInspectorTone.Ready);
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                DataTableLubanInspectorUi.DrawNotice(
                    "OPERATION ERROR",
                    result.ErrorMessage,
                    detail: null,
                    tone: DataTableLubanInspectorTone.Error);
            }

            _showOutput = DataTableLubanInspectorUi.DrawContainedFoldout(
                _showOutput,
                "Captured stdout / stderr");
            if (_showOutput)
            {
                DrawCapturedOutput("stdout", result.StandardOutput);
                DrawCapturedOutput("stderr", result.StandardError);
            }

            if (GUILayout.Button("Copy Diagnostics"))
            {
                EditorGUIUtility.systemCopyBuffer = BuildDiagnosticsText(result);
            }

            DataTableLubanInspectorUi.EndPanel();
        }

        private static void DrawQuickStart(
            DataTableLubanSettings settings,
            DataTableLubanAuthoringSnapshot snapshot)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quick Start", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "1. Approve the pinned Luban artifact.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "2. Resolve validation issues.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "3. Generate the selected profile.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "4. Check the published output.",
                EditorStyles.wordWrappedMiniLabel);
            DataTableLubanDualButtonLayout quickStartActions =
                DataTableLubanInspectorUi.GetDualButtonLayout(EditorGUIUtility.singleLineHeight);
            if (GUI.Button(quickStartActions.FirstRect, "Open Package Guide"))
            {
                OpenPackageGuide(settings);
            }

            using (new EditorGUI.DisabledScope(!File.Exists(snapshot.ConfigurationPath)))
            {
                if (GUI.Button(quickStartActions.SecondRect, "Reveal Configuration"))
                {
                    EditorUtility.RevealInFinder(snapshot.ConfigurationPath);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawMultipleSelection()
        {
            DataTableLubanInspectorUi.DrawHero(
                "Luban Data Pipeline",
                "Multiple settings assets selected",
                "BLOCKED",
                DataTableLubanInspectorTone.Error);
            DataTableLubanInspectorUi.DrawNotice(
                "MULTIPLE SETTINGS ASSETS",
                "Exactly one DataTableLubanSettings asset is authoritative. Multi-object editing is disabled; select the authoritative asset individually.",
                detail: null,
                tone: DataTableLubanInspectorTone.Error);
        }

        private void DrawContractFailure()
        {
            DataTableLubanInspectorUi.DrawHero(
                "Inspector Contract Failure",
                "Authoring is disabled until every serialized field has an explicit owner.",
                "BLOCKED",
                DataTableLubanInspectorTone.Error);
            DataTableLubanInspectorUi.DrawNotice(
                "INSPECTOR CONTRACT FAILURE",
                _contractError,
                detail: null,
                tone: DataTableLubanInspectorTone.Error);
        }

        private static void DrawSerializedToggleLeft(
            SerializedProperty property,
            GUIContent label)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginProperty(rect, label, property);
            bool previousMixedValue = EditorGUI.showMixedValue;
            try
            {
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUI.ToggleLeft(rect, label, property.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    property.boolValue = value;
                }
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixedValue;
                EditorGUI.EndProperty();
            }
        }

        private static bool BrowseConfiguration(SerializedProperty property)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string current = projectRoot;
            try
            {
                string configured = Path.GetFullPath(Path.Combine(
                    projectRoot,
                    property.stringValue ?? string.Empty));
                current = File.Exists(configured)
                    ? Path.GetDirectoryName(configured)
                    : Directory.Exists(Path.GetDirectoryName(configured))
                        ? Path.GetDirectoryName(configured)
                        : projectRoot;
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                current = projectRoot;
            }

            string selected = EditorUtility.OpenFilePanel(
                "Select DataTable build_config.ini",
                current,
                "ini");
            if (string.IsNullOrEmpty(selected))
            {
                return false;
            }

            if (!string.Equals(
                    Path.GetFileName(selected),
                    "build_config.ini",
                    StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Pipeline Configuration",
                    "Select a file named exactly build_config.ini.",
                    "OK");
                return false;
            }

            string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
            string fullPath = Path.GetFullPath(selected);
            if (!IsContained(repositoryRoot, fullPath))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Pipeline Configuration",
                    "The configuration must remain inside the repository root.",
                    "OK");
                return false;
            }

            property.stringValue = MakeRelativePath(projectRoot, fullPath);
            return true;
        }

        private static string MakeRelativePath(string baseDirectory, string fullPath)
        {
            var baseUri = new Uri(EnsureTrailingSeparator(Path.GetFullPath(baseDirectory)));
            var fileUri = new Uri(Path.GetFullPath(fullPath));
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString())
                .Replace('\\', '/');
        }

        private static bool IsContained(string root, string path)
        {
            string prefix = EnsureTrailingSeparator(Path.GetFullPath(root));
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return path.StartsWith(prefix, comparison);
        }

        private static string EnsureTrailingSeparator(string value)
        {
            return value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? value
                : value + Path.DirectorySeparatorChar;
        }

        private static void DrawCapturedOutput(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            string rendered = value.Length <= MaximumRenderedOutputCharacters
                ? value
                : value.Substring(0, MaximumRenderedOutputCharacters) +
                  "\n... Inspector preview truncated; use Copy Diagnostics for the retained output.";
            DataTableLubanInspectorUi.DrawReadOnlyPreview(label, rendered, 54f);
        }

        private static string BuildDiagnosticsText(DataTableLubanRunResult result)
        {
            var builder = new StringBuilder(1024);
            builder.Append("Success: ").AppendLine(result.Success.ToString())
                .Append("ExitCode: ").AppendLine(result.ExitCode.ToString())
                .Append("DurationMilliseconds: ").AppendLine(result.DurationMilliseconds.ToString())
                .Append("Cancelled: ").AppendLine(result.Cancelled.ToString())
                .Append("TimedOut: ").AppendLine(result.TimedOut.ToString())
                .Append("RecoveryRequired: ").AppendLine(result.RecoveryRequired.ToString())
                .Append("RecoveryRunId: ").AppendLine(result.RecoveryRunId)
                .Append("Error: ").AppendLine(result.ErrorMessage)
                .AppendLine("stdout:")
                .AppendLine(result.StandardOutput)
                .AppendLine("stderr:")
                .Append(result.StandardError);
            return builder.ToString();
        }

        private static void OpenPackageGuide(DataTableLubanSettings settings)
        {
            string packageRoot = DataTableLubanToolProjectLocator.GetPackageAssetRoot(settings);
            string readmePath = string.IsNullOrEmpty(packageRoot)
                ? string.Empty
                : packageRoot + "/README.md";
            UnityEngine.Object readme = string.IsNullOrEmpty(readmePath)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(readmePath);
            if (readme != null)
            {
                AssetDatabase.OpenAsset(readme);
                return;
            }

            EditorUtility.DisplayDialog(
                "DataTable Package Guide",
                "The package README could not be located from the settings script.",
                "OK");
        }

        private void HandleCoordinatorChanged()
        {
            Repaint();
        }

        private void RepaintWhileActive()
        {
            if (DataTableLubanAuthoringCoordinator.IsInspecting ||
                DataTableLubanAuthoringCoordinator.IsOperationInProgress ||
                DataTableLubanRunner.IsRunning)
            {
                Repaint();
            }
        }

        private static bool IsReadyValue(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.Ordinal);
        }

        internal static string GetPublishedOutputDisplayValue(
            string state,
            string transactionState,
            bool recoveryRequired,
            bool inspectionPending)
        {
            if (!string.Equals(state, "unavailable", StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(state) ? "-" : state;
            }

            if (inspectionPending)
            {
                return "Pending inspection";
            }

            if (string.Equals(transactionState, "active", StringComparison.Ordinal))
            {
                return "Deferred while writer is active";
            }

            if (recoveryRequired)
            {
                return "Unavailable until recovery";
            }

            return "Unavailable until validation succeeds";
        }

        internal static DataTableLubanInspectorTone GetOutputTone(
            string state,
            string transactionState,
            bool inspectionPending)
        {
            if (string.Equals(state, "current", StringComparison.Ordinal))
            {
                return DataTableLubanInspectorTone.Ready;
            }

            if (string.Equals(state, "unavailable", StringComparison.Ordinal))
            {
                return inspectionPending ||
                       string.Equals(transactionState, "active", StringComparison.Ordinal)
                    ? DataTableLubanInspectorTone.Busy
                    : DataTableLubanInspectorTone.Warning;
            }

            if (string.Equals(state, "missing", StringComparison.Ordinal) ||
                string.Equals(state, "stale", StringComparison.Ordinal) ||
                string.Equals(state, "drifted", StringComparison.Ordinal))
            {
                return DataTableLubanInspectorTone.Warning;
            }

            return string.IsNullOrEmpty(state)
                ? DataTableLubanInspectorTone.Neutral
                : DataTableLubanInspectorTone.Error;
        }

        private static string GetActionBlocker(
            DataTableLubanAuthoringSnapshot snapshot,
            bool inspecting)
        {
            if (inspecting)
            {
                return "Pipeline actions are disabled while the authoritative status inspection is running.";
            }

            if (snapshot == null)
            {
                return "No authoritative pipeline status is available.";
            }

            for (var index = 0; index < snapshot.Issues.Length; index++)
            {
                DataTableLubanAuthoringIssue issue = snapshot.Issues[index];
                if (issue.Severity == DataTableLubanIssueSeverity.Error ||
                    issue.Severity == DataTableLubanIssueSeverity.Warning)
                {
                    return "[" + issue.Code + "] " + issue.Message;
                }
            }

            if (!string.IsNullOrEmpty(snapshot.InspectionError))
            {
                return snapshot.InspectionError;
            }

            return "The latest inspection did not authorize this action. Refresh status for current details.";
        }

        private static string Abbreviate(string value, int maximumCharacters)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumCharacters)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maximumCharacters) + "...";
        }
    }
}
