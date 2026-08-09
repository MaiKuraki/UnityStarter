using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal enum DataTableLubanAuthoringCancellationTarget
    {
        None,
        Preflight,
        Runner,
    }

    internal readonly struct DataTableLubanAuthoringOperationProjection
    {
        internal DataTableLubanAuthoringOperationProjection(
            DataTableLubanOperation operation,
            long startedUtcTicks,
            bool hasLastResult,
            DataTableLubanRunResult lastResult)
        {
            Operation = operation;
            StartedUtcTicks = startedUtcTicks;
            HasLastResult = hasLastResult;
            LastResult = lastResult;
        }

        internal DataTableLubanOperation Operation { get; }
        internal long StartedUtcTicks { get; }
        internal bool HasLastResult { get; }
        internal DataTableLubanRunResult LastResult { get; }
    }

    internal sealed class DataTableLubanSettingsAssetIndex
    {
        private readonly Func<int> _countProvider;
        private int _count = -1;

        internal DataTableLubanSettingsAssetIndex(Func<int> countProvider)
        {
            _countProvider = countProvider ?? throw new ArgumentNullException(nameof(countProvider));
        }

        internal int Count
        {
            get
            {
                if (_count < 0)
                {
                    _count = _countProvider();
                }

                return _count;
            }
        }

        internal void Invalidate()
        {
            _count = -1;
        }
    }

    internal sealed class DataTableLubanSettingsObserverRegistry
    {
        private readonly System.Collections.Generic.List<Observer> _observers =
            new System.Collections.Generic.List<Observer>(2);

        internal int Count => _observers.Count;

        internal DataTableLubanSettings Current =>
            _observers.Count == 0 ? null : _observers[_observers.Count - 1].Settings;

        internal void Observe(object owner, DataTableLubanSettings settings)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            for (var index = 0; index < _observers.Count; index++)
            {
                if (!ReferenceEquals(_observers[index].Owner, owner))
                {
                    continue;
                }

                _observers.RemoveAt(index);
                break;
            }

            _observers.Add(new Observer(owner, settings));
        }

        internal bool StopObserving(object owner)
        {
            if (owner == null)
            {
                return false;
            }

            for (var index = _observers.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(_observers[index].Owner, owner))
                {
                    continue;
                }

                _observers.RemoveAt(index);
                return true;
            }

            return false;
        }

        private readonly struct Observer
        {
            internal Observer(object owner, DataTableLubanSettings settings)
            {
                Owner = owner;
                Settings = settings;
            }

            internal object Owner { get; }
            internal DataTableLubanSettings Settings { get; }
        }
    }

    [InitializeOnLoad]
    internal static class DataTableLubanAuthoringCoordinator
    {
        private const int EditorTimeoutGraceSeconds = 60;
        private const int MaximumEditorTimeoutSeconds = 24 * 60 * 60;
        private const int LifecycleTerminationTimeoutMilliseconds = 10_000;
        private const double SettingsInspectionDebounceSeconds = 0.65d;
        private static readonly DataTableLubanSettingsAssetIndex SettingsAssets =
            new DataTableLubanSettingsAssetIndex(
                () => DataTableLubanSettings.FindAssetPaths().Length);
        private static readonly DataTableLubanSettingsObserverRegistry Observers =
            new DataTableLubanSettingsObserverRegistry();

        private static DataTableLubanSettings _observedSettings;
        private static DataTableLubanAuthoringSnapshot _snapshot =
            DataTableLubanAuthoringSnapshot.Empty;
        private static string _snapshotKey = string.Empty;
        private static string _activeInspectionKey = string.Empty;
        private static bool _activeInspectionIsPassive;
        private static CancellationTokenSource _inspectionCancellation;
        private static CancellationTokenSource _operationCancellation;
        private static DataTableLubanSettings _deferredInspectionSettings;
        private static bool _inspectionDeferred;
        private static bool _observerInspectionDeferred;
        private static bool _settingsInspectionPending;
        private static double _settingsInspectionDueTime;
        private static bool _operationInProgress;
        private static bool _runnerStartedForOperation;
        private static DataTableLubanOperation _operation;
        private static long _operationStartedUtcTicks;
        private static bool _hasLastResult;
        private static DataTableLubanRunResult _lastResult;
        private static string _authoringError = string.Empty;
        private static long _lastRunnerRevision = -1;

        static DataTableLubanAuthoringCoordinator()
        {
            EditorApplication.projectChanged += HandleProjectChanged;
            EditorApplication.update += HandleEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForEditorLifecycle;
            EditorApplication.quitting += ShutdownForEditorLifecycle;
        }

        internal static event Action Changed;

        internal static DataTableLubanAuthoringSnapshot Snapshot => _snapshot;
        internal static bool IsInspecting => _inspectionCancellation != null;
        internal static bool IsOperationInProgress =>
            _operationInProgress || DataTableLubanRunner.CurrentState.IsActive;
        internal static DataTableLubanOperation CurrentOperation
        {
            get
            {
                return GetOperationProjection().Operation;
            }
        }
        internal static long OperationStartedUtcTicks
        {
            get
            {
                return GetOperationProjection().StartedUtcTicks;
            }
        }
        internal static bool HasLastResult => GetOperationProjection().HasLastResult;
        internal static DataTableLubanRunResult LastResult
        {
            get
            {
                return GetOperationProjection().LastResult;
            }
        }
        internal static string AuthoringError => _authoringError;
        internal static bool CanCancel =>
            GetCancellationTarget(
                DataTableLubanRunner.CurrentState,
                _operationInProgress,
                _operationCancellation != null,
                _runnerStartedForOperation) != DataTableLubanAuthoringCancellationTarget.None;
        internal static DataTableLubanRunnerPhase RunnerPhase =>
            DataTableLubanRunner.CurrentState.Phase;
        internal static int RunnerProcessId => DataTableLubanRunner.CurrentState.ProcessId;
        internal static string RunnerProfileName =>
            DataTableLubanRunner.CurrentState.ProfileName;
        internal static bool IsPreflightInProgress =>
            _operationInProgress && !_runnerStartedForOperation;

        internal static void Observe(object owner, DataTableLubanSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            Observers.Observe(owner, settings);
            if (IsOperationInProgress)
            {
                _observedSettings = settings;
                _observerInspectionDeferred = true;
                return;
            }

            _observedSettings = settings;
            RequestInspection(settings, force: false, passive: true);
        }

        internal static void StopObserving(object owner)
        {
            DataTableLubanSettings previous = _observedSettings;
            if (!Observers.StopObserving(owner))
            {
                return;
            }

            _observedSettings = Observers.Current;
            if (Observers.Count == 0)
            {
                _settingsInspectionPending = false;
                _observerInspectionDeferred = false;
                if (_activeInspectionIsPassive)
                {
                    TryCancel(_inspectionCancellation);
                }

                return;
            }

            if (!ReferenceEquals(previous, _observedSettings) &&
                _observedSettings != null)
            {
                RequestInspection(_observedSettings, force: false, passive: true);
            }
        }

        internal static void RequestInspection(
            DataTableLubanSettings settings,
            bool force,
            bool publishDiagnostics = false,
            bool passive = false)
        {
            if (settings == null)
            {
                if (publishDiagnostics)
                {
                    RejectAuthoringRequest(
                        "Pipeline inspection was blocked because the settings asset is missing.");
                }

                return;
            }

            if (passive && Observers.Count == 0)
            {
                return;
            }

            if (IsOperationInProgress)
            {
                if (passive)
                {
                    _observedSettings = Observers.Current ?? settings;
                    _observerInspectionDeferred = true;
                }
                else
                {
                    _deferredInspectionSettings = settings;
                    _inspectionDeferred = true;
                }
                if (publishDiagnostics)
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Info,
                        BuildInspectionMessage(
                            settings,
                            "Inspection deferred until the active pipeline operation completes."));
                }

                return;
            }

            string key = CreateSnapshotKey(settings);
            if (_inspectionCancellation != null &&
                string.Equals(_activeInspectionKey, key, StringComparison.Ordinal))
            {
                if (passive)
                {
                    _observerInspectionDeferred = false;
                }

                if (!passive)
                {
                    _activeInspectionIsPassive = false;
                }

                if (publishDiagnostics)
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Info,
                        BuildInspectionMessage(settings, "Inspection is already in progress."));
                }

                return;
            }

            if (ShouldDeferPassiveInspectionRequest(
                    passive,
                    _inspectionCancellation != null,
                    _activeInspectionIsPassive))
            {
                _observedSettings = Observers.Current ?? settings;
                _observerInspectionDeferred = true;
                if (publishDiagnostics)
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Info,
                        BuildInspectionMessage(
                            settings,
                            "Passive inspection deferred until the active explicit inspection completes."));
                }

                return;
            }

            if (!force &&
                string.Equals(_snapshotKey, key, StringComparison.Ordinal) &&
                _snapshot != DataTableLubanAuthoringSnapshot.Empty)
            {
                if (passive)
                {
                    _observerInspectionDeferred = false;
                }

                return;
            }

            if (passive)
            {
                _observedSettings = Observers.Current ?? settings;
                _observerInspectionDeferred = false;
            }

            _snapshotKey = key;
            if (!passive)
            {
                _deferredInspectionSettings = null;
                _inspectionDeferred = false;
            }
            _settingsInspectionPending = false;
            CancellationTokenSource previous = _inspectionCancellation;
            previous?.Cancel();
            var cancellation = new CancellationTokenSource();
            _inspectionCancellation = cancellation;
            _activeInspectionKey = key;
            _activeInspectionIsPassive = passive;
            _snapshot = CreatePendingSnapshot(settings);
            if (publishDiagnostics)
            {
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Info,
                    BuildInspectionMessage(settings, "Inspection started."));
            }

            NotifyChanged();
            InspectAndPublishAsync(settings, key, cancellation, publishDiagnostics).Forget();
        }

        internal static void NotifySettingsChanged(
            DataTableLubanSettings settings,
            bool requiresDeepInspection)
        {
            if (settings == null)
            {
                return;
            }

            _observedSettings = settings;
            _snapshotKey = CreateSnapshotKey(settings);
            _inspectionCancellation?.Cancel();
            _snapshot = CreateLocallyDirtySnapshot(settings, _snapshot);
            _authoringError = string.Empty;
            if (requiresDeepInspection)
            {
                _settingsInspectionPending = true;
                _settingsInspectionDueTime =
                    EditorApplication.timeSinceStartup + SettingsInspectionDebounceSeconds;
            }

            NotifyChanged();
        }

        internal static void ExecuteOperation(
            DataTableLubanSettings settings,
            DataTableLubanOperation operation)
        {
            if (settings == null)
            {
                RejectOperation(
                    operation,
                    settings,
                    "The settings asset is missing.");
                return;
            }

            if (IsOperationInProgress)
            {
                RejectOperation(
                    operation,
                    settings,
                    "Another DataTable pipeline operation is already active.");
                return;
            }

            if (IsInspecting)
            {
                RejectOperation(
                    operation,
                    settings,
                    "A pipeline inspection is already active. Wait for it to finish or refresh again.");
                return;
            }

            SettingsAssets.Invalidate();
            ExecuteOperationAsync(settings, operation).Forget();
        }

        internal static bool RequestSafeCancellation()
        {
            DataTableLubanRunnerState runnerState = DataTableLubanRunner.CurrentState;
            DataTableLubanAuthoringCancellationTarget target = GetCancellationTarget(
                runnerState,
                _operationInProgress,
                _operationCancellation != null,
                _runnerStartedForOperation);
            if (target == DataTableLubanAuthoringCancellationTarget.Runner)
            {
                return DataTableLubanRunner.CurrentState.CanCancel &&
                       DataTableLubanRunner.CancelActiveRun();
            }

            if (target != DataTableLubanAuthoringCancellationTarget.Preflight)
            {
                return false;
            }

            runnerState = DataTableLubanRunner.CurrentState;
            if (runnerState.IsActive)
            {
                return runnerState.CanCancel && DataTableLubanRunner.CancelActiveRun();
            }

            CancellationTokenSource cancellation = _operationCancellation;
            if (cancellation == null)
            {
                return false;
            }

            try
            {
                cancellation.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        internal static DataTableLubanAuthoringCancellationTarget GetCancellationTarget(
            DataTableLubanRunnerState runnerState,
            bool operationInProgress,
            bool operationCancellationAvailable,
            bool runnerStartedForOperation)
        {
            if (runnerState.IsActive)
            {
                return runnerState.CanCancel
                    ? DataTableLubanAuthoringCancellationTarget.Runner
                    : DataTableLubanAuthoringCancellationTarget.None;
            }

            return operationInProgress &&
                   operationCancellationAvailable &&
                   !runnerStartedForOperation
                ? DataTableLubanAuthoringCancellationTarget.Preflight
                : DataTableLubanAuthoringCancellationTarget.None;
        }

        internal static bool ShouldDeferPassiveInspectionRequest(
            bool requestIsPassive,
            bool hasActiveInspection,
            bool activeInspectionIsPassive)
        {
            return requestIsPassive &&
                   hasActiveInspection &&
                   !activeInspectionIsPassive;
        }

        internal static DataTableLubanSettings SelectPostOperationInspectionTarget(
            DataTableLubanSettings operationSettings,
            DataTableLubanSettings observedSettings)
        {
            return observedSettings ?? operationSettings;
        }

        internal static DataTableLubanAuthoringOperationProjection ProjectOperationState(
            DataTableLubanRunnerState runnerState,
            bool operationInProgress,
            bool runnerStartedForOperation,
            DataTableLubanOperation authoringOperation,
            long authoringStartedUtcTicks,
            bool hasAuthoringResult,
            DataTableLubanRunResult authoringResult)
        {
            if (operationInProgress && !runnerStartedForOperation)
            {
                return new DataTableLubanAuthoringOperationProjection(
                    authoringOperation,
                    authoringStartedUtcTicks,
                    false,
                    default);
            }

            bool useRunnerOperation = runnerState.IsActive || runnerState.HasLastResult;
            return new DataTableLubanAuthoringOperationProjection(
                useRunnerOperation ? runnerState.Operation : authoringOperation,
                runnerState.StartedUtcTicks > 0
                    ? runnerState.StartedUtcTicks
                    : authoringStartedUtcTicks,
                runnerState.HasLastResult || hasAuthoringResult,
                runnerState.HasLastResult ? runnerState.LastResult : authoringResult);
        }

        private static DataTableLubanAuthoringOperationProjection GetOperationProjection()
        {
            return ProjectOperationState(
                DataTableLubanRunner.CurrentState,
                _operationInProgress,
                _runnerStartedForOperation,
                _operation,
                _operationStartedUtcTicks,
                _hasLastResult,
                _lastResult);
        }

        internal static void SaveSettings(DataTableLubanSettings settings)
        {
            if (settings == null)
            {
                RejectAuthoringRequest("Settings could not be saved because the asset is missing.");
                return;
            }

            try
            {
                AssetDatabase.SaveAssetIfDirty(settings);
                if (EditorUtility.IsDirty(settings))
                {
                    _authoringError =
                        "The DataTableLubanSettings asset remains unsaved after Unity attempted to persist it.";
                    _snapshot = CreateLocallyDirtySnapshot(settings, _snapshot);
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Error,
                        BuildInspectionMessage(settings, _authoringError));
                    NotifyChanged();
                    return;
                }

                _authoringError = string.Empty;
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Info,
                    BuildInspectionMessage(settings, "Settings saved."));
                RequestInspection(settings, force: true, publishDiagnostics: true);
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                _authoringError = exception.Message;
                DataTableEditorDiagnostics.PublishException(
                    DataTableDiagnosticLevel.Error,
                    exception,
                    BuildInspectionMessage(settings, "Settings could not be saved."));
                NotifyChanged();
            }
        }

        private static async UniTaskVoid InspectAndPublishAsync(
            DataTableLubanSettings settings,
            string key,
            CancellationTokenSource cancellation,
            bool publishDiagnostics)
        {
            DataTableLubanInspectionResult result;
            try
            {
                result = await DataTableLubanInspectionClient.InspectAsync(
                    settings,
                    cancellation.Token);
                await UniTask.SwitchToMainThread();
                if (!ReferenceEquals(_inspectionCancellation, cancellation) ||
                    !string.Equals(_snapshotKey, key, StringComparison.Ordinal))
                {
                    return;
                }

                if (result.Cancelled)
                {
                    if (publishDiagnostics)
                    {
                        DataTableEditorDiagnostics.Publish(
                            DataTableDiagnosticLevel.Info,
                            BuildInspectionMessage(settings, "Inspection was cancelled."));
                    }

                    return;
                }

                PublishInspection(settings, result);
                if (publishDiagnostics)
                {
                    PublishInspectionDiagnostic(settings, result);
                }
            }
            catch (OperationCanceledException)
            {
                // Replaced inspections are intentionally silent.
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                await UniTask.SwitchToMainThread();
                if (ReferenceEquals(_inspectionCancellation, cancellation))
                {
                    _snapshot = CreateFailureSnapshot(settings, exception.Message);
                    if (publishDiagnostics)
                    {
                        DataTableEditorDiagnostics.PublishException(
                            DataTableDiagnosticLevel.Error,
                            exception,
                            BuildInspectionMessage(settings, "Inspection failed."));
                    }
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (ReferenceEquals(_inspectionCancellation, cancellation))
                {
                    _inspectionCancellation = null;
                    _activeInspectionKey = string.Empty;
                    _activeInspectionIsPassive = false;
                    NotifyChanged();
                }

                cancellation.Dispose();
            }
        }

        private static async UniTaskVoid ExecuteOperationAsync(
            DataTableLubanSettings settings,
            DataTableLubanOperation operation)
        {
            _operationInProgress = true;
            _operation = operation;
            _operationStartedUtcTicks = DateTime.UtcNow.Ticks;
            _authoringError = string.Empty;
            _hasLastResult = false;
            var cancellation = new CancellationTokenSource();
            _operationCancellation = cancellation;
            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Info,
                BuildOperationMessage(
                    operation,
                    settings,
                    "Fresh preflight inspection started."));
            NotifyChanged();

            try
            {
                DataTableLubanInspectionResult inspection =
                    await DataTableLubanInspectionClient.InspectAsync(settings, cancellation.Token);
                await UniTask.SwitchToMainThread();
                if (!inspection.Success)
                {
                    if (inspection.Cancelled)
                    {
                        DataTableEditorDiagnostics.Publish(
                            DataTableDiagnosticLevel.Info,
                            BuildOperationMessage(
                                operation,
                                settings,
                                "Preflight inspection was cancelled."));
                        return;
                    }

                    _authoringError = inspection.Error;
                    _snapshot = CreateFailureSnapshot(settings, inspection.Error);
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Error,
                        BuildOperationMessage(
                            operation,
                            settings,
                            "Preflight inspection failed: " + inspection.Error));
                    NotifyChanged();
                    return;
                }

                PublishInspection(settings, inspection);
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Info,
                    BuildOperationMessage(
                        operation,
                        settings,
                        "Fresh preflight inspection completed with status " +
                        _snapshot.StatusLabel + "."));
                bool permitted = operation switch
                {
                    DataTableLubanOperation.Generate => _snapshot.CanGenerate,
                    DataTableLubanOperation.Check => _snapshot.CanCheck,
                    DataTableLubanOperation.Recover => _snapshot.CanRecover,
                    _ => false,
                };
                if (!permitted)
                {
                    string issue = GetFirstBlockingIssue(_snapshot);
                    _authoringError = "The fresh pipeline inspection blocked " +
                                      operation.ToString().ToLowerInvariant() + "." +
                                      (string.IsNullOrEmpty(issue) ? string.Empty : " " + issue);
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Warning,
                        BuildOperationMessage(operation, settings, _authoringError));
                    NotifyChanged();
                    return;
                }

                int processTimeoutSeconds = _snapshot.ProcessTimeoutSeconds;
                if (processTimeoutSeconds < 1)
                {
                    _authoringError = "The inspected process timeout must be positive.";
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Error,
                        BuildOperationMessage(operation, settings, _authoringError));
                    NotifyChanged();
                    return;
                }

                int editorTimeoutSeconds = (int)Math.Min(
                    MaximumEditorTimeoutSeconds,
                    (long)processTimeoutSeconds + EditorTimeoutGraceSeconds);
                var profile = new DataTableLubanProfile(
                    DataTableLubanToolProjectLocator.ResolveToolProjectPath(settings),
                    _snapshot.ConfigurationPath,
                    _snapshot.SelectedProfileName,
                    checked(editorTimeoutSeconds * 1000),
                    settings.RefreshAssetsAfterSuccess,
                    settings.MaximumCapturedOutputCharacters);
                DataTableLubanCommand command = operation switch
                {
                    DataTableLubanOperation.Generate => DataTableLubanCommand.Generate(profile),
                    DataTableLubanOperation.Check => DataTableLubanCommand.Check(profile),
                    DataTableLubanOperation.Recover => DataTableLubanCommand.Recover(
                        profile,
                        _snapshot.Transaction.RunId),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
                };

                _runnerStartedForOperation = true;
                _lastResult = await DataTableLubanRunner.ExecuteAsync(command, cancellation.Token);
                _hasLastResult = true;
            }
            catch (OperationCanceledException)
            {
                // Explicit cancellation remains visible through Runner state/result.
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                _authoringError = exception.Message;
                DataTableEditorDiagnostics.PublishException(
                    DataTableDiagnosticLevel.Error,
                    exception,
                    BuildOperationMessage(operation, settings, "Authoring operation failed."));
                NotifyChanged();
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (ReferenceEquals(_operationCancellation, cancellation))
                {
                    _operationCancellation = null;
                }

                cancellation.Dispose();
                _runnerStartedForOperation = false;
                _operationInProgress = false;
                NotifyChanged();
                DataTableLubanSettings observedSettings = Observers.Current;
                if (_inspectionDeferred)
                {
                    _observerInspectionDeferred = observedSettings != null;
                }
                else
                {
                    DataTableLubanSettings inspectionTarget =
                        SelectPostOperationInspectionTarget(settings, observedSettings);
                    if (inspectionTarget != null)
                    {
                        RequestInspection(
                            inspectionTarget,
                            force: true,
                            passive: observedSettings != null);
                    }
                }
            }
        }

        private static void PublishInspection(
            DataTableLubanSettings settings,
            DataTableLubanInspectionResult result)
        {
            if (!result.Success)
            {
                _snapshot = CreateFailureSnapshot(settings, result.Error);
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(settings);
            int assetCount = SettingsAssets.Count;
            bool dirty = EditorUtility.IsDirty(settings);
            _snapshot = DataTableLubanInspectionProtocol.Project(
                result.Document,
                assetPath,
                assetCount,
                dirty);
            _authoringError = string.Empty;
            NotifyChanged();
        }

        private static DataTableLubanAuthoringSnapshot CreatePendingSnapshot(
            DataTableLubanSettings settings)
        {
            string assetPath = AssetDatabase.GetAssetPath(settings);
            int assetCount = SettingsAssets.Count;
            bool dirty = EditorUtility.IsDirty(settings);
            string configurationPath;
            try
            {
                configurationPath = settings.ResolveBuildConfigurationPath();
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                return CreateFailureSnapshot(settings, exception.Message);
            }

            return new DataTableLubanAuthoringSnapshot(
                DataTableLubanAuthoringState.Inspecting,
                false,
                false,
                false,
                assetPath,
                configurationPath,
                string.Empty,
                string.Empty,
                settings.SelectedProfileName,
                0,
                Array.Empty<DataTableLubanAuthoringIssue>(),
                Array.Empty<DataTableLubanProfileSnapshot>(),
                default,
                default,
                default,
                default,
                string.Empty,
                assetCount,
                dirty,
                true);
        }

        private static DataTableLubanAuthoringSnapshot CreateFailureSnapshot(
            DataTableLubanSettings settings,
            string error)
        {
            string assetPath = settings == null ? string.Empty : AssetDatabase.GetAssetPath(settings);
            int assetCount = settings == null ? 0 : SettingsAssets.Count;
            bool dirty = settings != null && EditorUtility.IsDirty(settings);
            string configurationPath = string.Empty;
            if (settings != null)
            {
                try
                {
                    configurationPath = settings.ResolveBuildConfigurationPath();
                }
                catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = exception.Message;
                    }
                }
            }

            var issues = new[]
            {
                new DataTableLubanAuthoringIssue(
                    "INSPECTION_FAILED",
                    DataTableLubanIssueSeverity.Error,
                    "configuration",
                    string.IsNullOrWhiteSpace(error)
                        ? "Pipeline inspection failed."
                        : error,
                    configurationPath),
            };
            return new DataTableLubanAuthoringSnapshot(
                DataTableLubanAuthoringState.Invalid,
                false,
                false,
                false,
                assetPath,
                configurationPath,
                string.Empty,
                string.Empty,
                settings == null ? string.Empty : settings.SelectedProfileName,
                0,
                issues,
                Array.Empty<DataTableLubanProfileSnapshot>(),
                default,
                default,
                default,
                default,
                error,
                assetCount,
                dirty,
                false);
        }

        private static DataTableLubanAuthoringSnapshot CreateLocallyDirtySnapshot(
            DataTableLubanSettings settings,
            DataTableLubanAuthoringSnapshot source)
        {
            source ??= DataTableLubanAuthoringSnapshot.Empty;
            int retainedIssueCount = 0;
            for (var index = 0; index < source.Issues.Length; index++)
            {
                if (!string.Equals(
                        source.Issues[index].Code,
                        "SETTINGS_UNSAVED",
                        StringComparison.Ordinal))
                {
                    retainedIssueCount++;
                }
            }

            var issues = new DataTableLubanAuthoringIssue[retainedIssueCount + 1];
            int destination = 0;
            for (var index = 0; index < source.Issues.Length; index++)
            {
                if (!string.Equals(
                        source.Issues[index].Code,
                        "SETTINGS_UNSAVED",
                        StringComparison.Ordinal))
                {
                    issues[destination++] = source.Issues[index];
                }
            }

            string assetPath = AssetDatabase.GetAssetPath(settings);
            issues[destination] = new DataTableLubanAuthoringIssue(
                "SETTINGS_UNSAVED",
                DataTableLubanIssueSeverity.Error,
                "configuration",
                "Save the DataTableLubanSettings asset before running the pipeline so Editor and CI use durable configuration.",
                assetPath);

            string configurationPath = source.ConfigurationPath;
            try
            {
                configurationPath = settings.ResolveBuildConfigurationPath();
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                // The next authoritative inspection reports the invalid path in detail.
            }

            DataTableLubanProfileSnapshot selectedProfile = default;
            for (var index = 0; index < source.Profiles.Length; index++)
            {
                if (string.Equals(
                        source.Profiles[index].Name,
                        settings.SelectedProfileName,
                        StringComparison.Ordinal))
                {
                    selectedProfile = source.Profiles[index];
                    break;
                }
            }

            DataTableLubanAuthoringState state = source.State;
            if (state == DataTableLubanAuthoringState.Ready ||
                state == DataTableLubanAuthoringState.Inspecting ||
                state == DataTableLubanAuthoringState.Unknown)
            {
                state = DataTableLubanAuthoringState.Blocked;
            }

            return new DataTableLubanAuthoringSnapshot(
                state,
                false,
                false,
                false,
                assetPath,
                configurationPath,
                source.ConfigurationSha256,
                source.SourceRoot,
                settings.SelectedProfileName,
                source.ProcessTimeoutSeconds,
                issues,
                source.Profiles,
                selectedProfile,
                source.Toolchain,
                source.Output,
                source.Transaction,
                source.InspectionError,
                SettingsAssets.Count,
                true,
                false);
        }

        private static string CreateSnapshotKey(DataTableLubanSettings settings)
        {
            string assetPath = AssetDatabase.GetAssetPath(settings);
            string configurationPath;
            long configurationLength = -1;
            long configurationWriteTicks = -1;
            try
            {
                configurationPath = settings.ResolveBuildConfigurationPath();
                var file = new FileInfo(configurationPath);
                if (file.Exists)
                {
                    configurationLength = file.Length;
                    configurationWriteTicks = file.LastWriteTimeUtc.Ticks;
                }
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                configurationPath = settings.BuildConfigurationPath;
            }

            return string.Join(
                "|",
                assetPath,
                settings.SchemaVersion,
                settings.BuildConfigurationPath,
                settings.SelectedProfileName,
                settings.RefreshAssetsAfterSuccess,
                settings.MaximumCapturedOutputCharacters,
                EditorUtility.IsDirty(settings),
                configurationPath,
                configurationLength,
                configurationWriteTicks);
        }

        private static void HandleProjectChanged()
        {
            SettingsAssets.Invalidate();
            DataTableLubanSettings observedSettings = Observers.Current;
            if (observedSettings != null)
            {
                RequestInspection(observedSettings, force: true, passive: true);
            }
        }

        private static void HandleEditorUpdate()
        {
            DataTableLubanRunnerState runnerState = DataTableLubanRunner.CurrentState;
            if (runnerState.Revision != _lastRunnerRevision)
            {
                _lastRunnerRevision = runnerState.Revision;
                NotifyChanged();
            }

            if (_inspectionDeferred &&
                _deferredInspectionSettings != null &&
                !IsOperationInProgress &&
                _inspectionCancellation == null)
            {
                DataTableLubanSettings deferredSettings = _deferredInspectionSettings;
                _deferredInspectionSettings = null;
                _inspectionDeferred = false;
                RequestInspection(deferredSettings, force: true, passive: false);
                return;
            }

            DataTableLubanSettings observedSettings = Observers.Current;
            if (_observerInspectionDeferred &&
                observedSettings != null &&
                !IsOperationInProgress &&
                _inspectionCancellation == null)
            {
                _observerInspectionDeferred = false;
                RequestInspection(observedSettings, force: true, passive: true);
                return;
            }

            if (_settingsInspectionPending &&
                observedSettings != null &&
                !IsOperationInProgress &&
                _inspectionCancellation == null &&
                EditorApplication.timeSinceStartup >= _settingsInspectionDueTime)
            {
                _settingsInspectionPending = false;
                RequestInspection(observedSettings, force: true, passive: true);
            }
        }

        private static void ShutdownForEditorLifecycle()
        {
            _settingsInspectionPending = false;
            _deferredInspectionSettings = null;
            _inspectionDeferred = false;
            _observerInspectionDeferred = false;
            bool confirmed = DataTableLubanInspectionClient.ShutdownAndTerminateActiveProcesses(
                LifecycleTerminationTimeoutMilliseconds,
                out int activeProcessCount,
                out string error);
            TryCancel(_inspectionCancellation);
            if (_operationInProgress && !_runnerStartedForOperation)
            {
                TryCancel(_operationCancellation);
            }

            if (activeProcessCount == 0)
            {
                return;
            }

            DataTableEditorDiagnostics.Publish(
                confirmed ? DataTableDiagnosticLevel.Warning : DataTableDiagnosticLevel.Error,
                confirmed
                    ? "Editor shutdown or assembly reload terminated every active DataTable pipeline inspection process tree."
                    : "Editor shutdown or assembly reload could not confirm every DataTable pipeline inspection process tree terminated. " +
                      error);
        }

        private static void TryCancel(CancellationTokenSource cancellation)
        {
            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The async owner completed between lifecycle observation and cancellation.
            }
        }

        private static void RejectOperation(
            DataTableLubanOperation operation,
            DataTableLubanSettings settings,
            string reason)
        {
            _authoringError = reason ?? "The authoring operation was blocked.";
            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Warning,
                BuildOperationMessage(operation, settings, _authoringError));
            NotifyChanged();
        }

        private static void RejectAuthoringRequest(string reason)
        {
            _authoringError = reason ?? "The authoring request was blocked.";
            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Warning,
                _authoringError);
            NotifyChanged();
        }

        private static void PublishInspectionDiagnostic(
            DataTableLubanSettings settings,
            DataTableLubanInspectionResult result)
        {
            if (!result.Success)
            {
                DataTableEditorDiagnostics.Publish(
                    result.Cancelled
                        ? DataTableDiagnosticLevel.Info
                        : DataTableDiagnosticLevel.Error,
                    BuildInspectionMessage(
                        settings,
                        result.Cancelled
                            ? "Inspection was cancelled."
                            : "Inspection failed: " + result.Error));
                return;
            }

            DataTableDiagnosticLevel level = _snapshot.State == DataTableLubanAuthoringState.Invalid
                ? DataTableDiagnosticLevel.Error
                : _snapshot.State == DataTableLubanAuthoringState.Blocked ||
                  _snapshot.State == DataTableLubanAuthoringState.RecoveryRequired
                    ? DataTableDiagnosticLevel.Warning
                    : DataTableDiagnosticLevel.Info;
            string firstIssue = GetFirstBlockingIssue(_snapshot);
            DataTableEditorDiagnostics.Publish(
                level,
                BuildInspectionMessage(
                    settings,
                    "Inspection completed with status " + _snapshot.StatusLabel + "." +
                    (string.IsNullOrEmpty(firstIssue) ? string.Empty : " " + firstIssue)));
        }

        private static string BuildInspectionMessage(
            DataTableLubanSettings settings,
            string detail)
        {
            return "DataTable Luban inspection; profile='" +
                   (settings == null ? string.Empty : settings.SelectedProfileName) +
                   "'; config='" + ResolveConfigurationForMessage(settings) + "'. " +
                   (detail ?? string.Empty);
        }

        private static string BuildOperationMessage(
            DataTableLubanOperation operation,
            DataTableLubanSettings settings,
            string detail)
        {
            return "DataTable Luban " + operation.ToString().ToLowerInvariant() +
                   "; profile='" +
                   (settings == null ? string.Empty : settings.SelectedProfileName) +
                   "'; config='" + ResolveConfigurationForMessage(settings) + "'. " +
                   (detail ?? string.Empty);
        }

        private static string ResolveConfigurationForMessage(DataTableLubanSettings settings)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            try
            {
                return settings.ResolveBuildConfigurationPath();
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                return settings.BuildConfigurationPath;
            }
        }

        private static string GetFirstBlockingIssue(DataTableLubanAuthoringSnapshot snapshot)
        {
            if (snapshot?.Issues == null)
            {
                return string.Empty;
            }

            for (var index = 0; index < snapshot.Issues.Length; index++)
            {
                DataTableLubanAuthoringIssue issue = snapshot.Issues[index];
                if (issue.Severity == DataTableLubanIssueSeverity.Error ||
                    issue.Severity == DataTableLubanIssueSeverity.Warning)
                {
                    return issue.Code + ": " + issue.Message;
                }
            }

            return snapshot.Issues.Length == 0
                ? string.Empty
                : snapshot.Issues[0].Code + ": " + snapshot.Issues[0].Message;
        }

        private static void NotifyChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                // One broken Inspector must not prevent other observers from refreshing.
            }
        }
    }
}
