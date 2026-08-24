using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using Build.Data;
using Build.VersionControl.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal sealed class BuildGlobalStateScope : IDisposable
    {
        private const string PlayerSettingsAssetPath = BuildPipelineAssetSaveFilter.PlayerSettingsAssetPath;

        private static BuildGlobalStateScope activeScope;

        private readonly BuildTarget originalActiveTarget;
        private readonly ScriptingImplementation originalScriptingBackend;
        private readonly string originalCompanyName;
        private readonly string originalProductName;
        private readonly string originalBundleVersion;
        private readonly string originalApplicationIdentifier;
        private readonly int originalAndroidBundleVersionCode;
        private readonly string originalIosBuildNumber;
        private readonly bool originalExportAndroidProject;
        private readonly bool originalDevelopmentBuild;
        private readonly EditorBuildSceneState[] originalEditorBuildScenes;
        private readonly PlayerSettingsSplashState originalSplashState;
        private readonly string[] originalPreloadedAssetIds;
        private readonly BuildRequest request;
        private readonly BuildVersionContext appliedVersion;
        private readonly PlayerSettings playerSettingsAsset;
        private readonly string playerSettingsAbsolutePath;
        private readonly GlobalBuildStateTransaction transaction;
        private bool transactionStarted;
        private bool disposed;

        private BuildGlobalStateScope(
            BuildRequest request,
            BuildVersionContext appliedVersion,
            GlobalBuildStateTransaction transaction)
        {
            this.request = request;
            this.appliedVersion = appliedVersion ?? throw new ArgumentNullException(nameof(appliedVersion));
            this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            playerSettingsAsset = GetPlayerSettingsAsset();
            if (EditorUtility.IsDirty(playerSettingsAsset))
            {
                throw new InvalidOperationException(
                    "PlayerSettings has unsaved changes. Save or revert it before starting a transactional build.");
            }

            playerSettingsAbsolutePath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                PlayerSettingsAssetPath));
            if (!File.Exists(playerSettingsAbsolutePath))
            {
                throw new FileNotFoundException(
                    "PlayerSettings asset was not found.",
                    playerSettingsAbsolutePath);
            }

            originalActiveTarget = EditorUserBuildSettings.activeBuildTarget;
            originalScriptingBackend = PlayerSettings.GetScriptingBackend(request.NamedTarget);
            originalCompanyName = PlayerSettings.companyName;
            originalProductName = PlayerSettings.productName;
            originalBundleVersion = PlayerSettings.bundleVersion;
            originalApplicationIdentifier = PlayerSettings.GetApplicationIdentifier(request.NamedTarget);
            originalAndroidBundleVersionCode = PlayerSettings.Android.bundleVersionCode;
            originalIosBuildNumber = PlayerSettings.iOS.buildNumber;
            originalExportAndroidProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            originalDevelopmentBuild = EditorUserBuildSettings.development;
            originalEditorBuildScenes = CaptureEditorBuildScenes();
            originalSplashState = PlayerSettingsLicensePolicy.Capture(playerSettingsAsset);
            originalPreloadedAssetIds = PlayerSettingsPreloadedAssetPolicy.Capture();
        }

        public static BuildGlobalStateScope CaptureAndApply(BuildRequest request, BuildVersionContext version)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            string projectRoot = Path.GetFullPath(request.ProjectRoot);
            EnsurePlayerSettingsAssetIsClean("starting a transactional build");
            GlobalBuildStateTransaction transaction = GlobalBuildStateTransaction.Acquire(projectRoot);
            BuildGlobalStateScope scope = null;
            try
            {
                if (transaction.HasPendingRecovery)
                {
                    throw new BuildFailedException(
                        "A pending global build-state transaction requires explicit workspace recovery before another build can begin.");
                }

                EnsureActiveBuildTargetMatches(
                    request,
                    EditorUserBuildSettings.activeBuildTarget);
                scope = new BuildGlobalStateScope(request, version, transaction);
                transaction.Begin(
                    PlayerSettingsAssetPath,
                    (int)scope.originalActiveTarget,
                    (int)request.Target,
                    scope.CaptureOriginalOwnedPlayerSettings());
                scope.transactionStarted = true;
                transaction.BeginGlobalMutation();
                scope.Apply(version);
                scope.ValidateAppliedState(version);
                transaction.EnsurePlayerSettingsUnchangedBeforePersistence();
                scope.PersistAppliedPlayerSettings(version);
                if (activeScope != null)
                {
                    throw new InvalidOperationException(
                        "A Unity global-state scope is already active in this Editor process.");
                }

                activeScope = scope;
                return scope;
            }
            catch (Exception applyException)
            {
                Exception cleanupException = null;
                if (scope != null && scope.transactionStarted)
                {
                    try
                    {
                        scope.Dispose();
                    }
                    catch (Exception exception)
                    {
                        cleanupException = exception;
                    }
                }
                else
                {
                    cleanupException = transaction.Release();
                }

                if (cleanupException != null)
                {
                    throw new AggregateException(
                        "Failed to acquire/apply and restore Unity build settings.",
                        applyException,
                        cleanupException);
                }

                throw;
            }
        }

        internal static void RecoverPending(string projectRoot)
        {
            EnsurePlayerSettingsAssetIsClean("recovering an interrupted build");
            GlobalBuildStateTransaction transaction =
                GlobalBuildStateTransaction.Acquire(projectRoot);
            Exception recoveryFailure = null;
            try
            {
                if (transaction.HasPendingRecovery)
                {
                    RecoverInterruptedUnityState(transaction);
                }
            }
            catch (Exception exception)
            {
                recoveryFailure = exception;
            }

            Exception releaseFailure = transaction.Release();
            if (recoveryFailure != null && releaseFailure != null)
            {
                throw new AggregateException(
                    "Global build-state recovery and lock release both failed.",
                    recoveryFailure,
                    releaseFailure);
            }

            if (recoveryFailure != null)
            {
                ExceptionDispatchInfo.Capture(recoveryFailure).Throw();
            }

            if (releaseFailure != null)
            {
                ExceptionDispatchInfo.Capture(releaseFailure).Throw();
            }
        }

        internal static void EnsureCurrentPlayerSettingsOwned()
        {
            BuildGlobalStateScope scope = activeScope;
            if (scope == null || scope.disposed)
            {
                throw new InvalidOperationException(
                    "No active Unity global-state scope can authorize the Player build.");
            }

            scope.transaction.EnsurePlayerSettingsOwned();
            if (EditorUtility.IsDirty(scope.playerSettingsAsset))
            {
                throw new IOException(
                    "PlayerSettings acquired unsaved in-memory changes after the persistence barrier. " +
                    "The Player build or publication was blocked and recovery evidence was retained.");
            }

            scope.ValidateAppliedState(scope.appliedVersion);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (ReferenceEquals(activeScope, this))
            {
                activeScope = null;
            }

            var restoreFailures = new List<Exception>();
            bool playerSettingsFileRestored = false;
            TryRestore(
                RestoreEditorUserBuildState,
                "Editor user build state",
                restoreFailures);
            TryRestore(
                () =>
                {
                    transaction.RestoreGlobalSettingsFiles();
                    playerSettingsFileRestored = true;
                },
                "PlayerSettings asset bytes",
                restoreFailures);

            if (playerSettingsFileRestored)
            {
                TryRestore(ClearPlayerSettingsDirtyState, "PlayerSettings dirty state", restoreFailures);
            }

            if (restoreFailures.Count == 0)
            {
                TryRestore(transaction.Complete, "global-state journal completion", restoreFailures);
            }

            Exception releaseFailure = transaction.Release();
            if (releaseFailure != null)
            {
                restoreFailures.Add(releaseFailure);
            }

            if (restoreFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Failed to restore one or more Unity build settings.",
                    new AggregateException(restoreFailures));
            }
        }

        private static void TryRestore(
            Action operation,
            string stateName,
            ICollection<Exception> failures)
        {
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Failed to restore {stateName}.",
                    exception));
            }
        }

        private void RestoreEditorUserBuildState()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject =
                originalExportAndroidProject;
            EditorUserBuildSettings.development = originalDevelopmentBuild;
            EditorBuildSettings.scenes = CreateEditorBuildSettingsScenes(
                originalEditorBuildScenes);
        }

        private static PlayerSettings GetPlayerSettingsAsset()
        {
            PlayerSettings[] assets = Resources.FindObjectsOfTypeAll<PlayerSettings>();
            if (assets.Length != 1 || assets[0] == null)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one loaded PlayerSettings asset, but found {assets.Length}.");
            }

            string assetPath = AssetDatabase.GetAssetPath(assets[0]);
            if (!string.Equals(assetPath, PlayerSettingsAssetPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected PlayerSettings asset path: '{assetPath}'.");
            }

            return assets[0];
        }

        private static void EnsurePlayerSettingsAssetIsClean(string operation)
        {
            PlayerSettings asset = GetPlayerSettingsAsset();
            if (EditorUtility.IsDirty(asset))
            {
                throw new InvalidOperationException(
                    $"PlayerSettings has unsaved changes. Save or revert it before {operation}.");
            }
        }

        private void ClearPlayerSettingsDirtyState()
        {
            PlayerSettings loadedAsset = playerSettingsAsset == null
                ? GetPlayerSettingsAsset()
                : playerSettingsAsset;
            EditorUtility.ClearDirty(loadedAsset);
            if (EditorUtility.IsDirty(loadedAsset))
            {
                throw new InvalidOperationException(
                    "PlayerSettings remained dirty after its original bytes were restored.");
            }

            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            PlayerSettings reloadedAsset = GetPlayerSettingsAsset();
            if (EditorUtility.IsDirty(reloadedAsset))
            {
                throw new InvalidOperationException(
                    "PlayerSettings became dirty while reloading its restored file.");
            }
        }

        private void Apply(BuildVersionContext version)
        {
            EnsureActiveBuildTargetMatches(
                request,
                EditorUserBuildSettings.activeBuildTarget);

            PlayerSettings.SetScriptingBackend(request.NamedTarget, request.ScriptingBackend);
            PlayerSettings.companyName = request.CompanyName;
            PlayerSettings.productName = request.ProductName;
            PlayerSettings.bundleVersion = version.ApplicationVersion;
            PlayerSettings.SetApplicationIdentifier(request.NamedTarget, request.ApplicationIdentifier);
            if (request.Target == BuildTarget.Android)
            {
                PlayerSettings.Android.bundleVersionCode = checked((int)version.BuildNumber);
            }
            else if (request.Target == BuildTarget.iOS)
            {
                PlayerSettings.iOS.buildNumber = version.BuildNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            EditorUserBuildSettings.exportAsGoogleAndroidProject = request.ExportAndroidProject;
            EditorUserBuildSettings.development = request.DebugBuild;
            if (RequiresRecipeScenes(request))
            {
                EditorBuildSettings.scenes = CreateRequestedEditorBuildScenes(
                    request.BuildScenePaths);
            }
            transaction.MarkEditorBuildSettingsApplied();
            PlayerSettingsLicensePolicy.Apply(playerSettingsAsset);
        }

        private void ValidateAppliedState(BuildVersionContext version)
        {
            var failures = new List<string>();
            if (EditorUserBuildSettings.activeBuildTarget != request.Target)
            {
                failures.Add("active build target");
            }

            if (PlayerSettings.GetScriptingBackend(request.NamedTarget) != request.ScriptingBackend)
            {
                failures.Add("scripting backend");
            }

            if (!string.Equals(PlayerSettings.companyName, request.CompanyName, StringComparison.Ordinal))
            {
                failures.Add("company name");
            }

            if (!string.Equals(PlayerSettings.productName, request.ProductName, StringComparison.Ordinal))
            {
                failures.Add("product name");
            }

            if (!string.Equals(PlayerSettings.bundleVersion, version.ApplicationVersion, StringComparison.Ordinal))
            {
                failures.Add("bundle version");
            }

            if (!string.Equals(
                    PlayerSettings.GetApplicationIdentifier(request.NamedTarget),
                    request.ApplicationIdentifier,
                    StringComparison.Ordinal))
            {
                failures.Add("application identifier");
            }

            if (request.Target == BuildTarget.Android
                && PlayerSettings.Android.bundleVersionCode != checked((int)version.BuildNumber))
            {
                failures.Add("Android build number");
            }
            else if (request.Target == BuildTarget.iOS
                     && !string.Equals(
                         PlayerSettings.iOS.buildNumber,
                         version.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                         StringComparison.Ordinal))
            {
                failures.Add("iOS build number");
            }

            if (EditorUserBuildSettings.exportAsGoogleAndroidProject != request.ExportAndroidProject)
            {
                failures.Add("Android export setting");
            }

            if (EditorUserBuildSettings.development != request.DebugBuild)
            {
                failures.Add("Development build setting");
            }

            if (RequiresRecipeScenes(request)
                && !EditorBuildScenesEqual(
                    CaptureEditorBuildScenes(),
                    CreateRequestedEditorBuildSceneStates(request.BuildScenePaths)))
            {
                failures.Add("Editor build scene sequence");
            }

            try
            {
                PlayerSettingsLicensePolicy.Validate(playerSettingsAsset);
            }
            catch (Exception exception)
            {
                failures.Add("license-compliant splash policy: " + exception.Message);
            }

            try
            {
                if (!PlayerSettingsPreloadedAssetPolicy.SequenceEqual(
                        PlayerSettingsPreloadedAssetPolicy.Capture(),
                        originalPreloadedAssetIds))
                {
                    failures.Add("preloaded asset sequence");
                }
            }
            catch (Exception exception)
            {
                failures.Add("preloaded asset sequence: " + exception.Message);
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Unity rejected one or more requested build settings: " + string.Join(", ", failures) + ".");
            }
        }

        private void PersistAppliedPlayerSettings(BuildVersionContext version)
        {
            BuildPipelineAssetSaveFilter.SaveOnlyPlayerSettings(playerSettingsAsset);
            GlobalBuildStateTransaction.PlayerSettingsPersistenceToken persistenceToken =
                transaction.CapturePlayerSettingsPersistenceToken();
            ValidateAppliedState(version);
            if (EditorUtility.IsDirty(playerSettingsAsset))
            {
                throw new IOException(
                    "PlayerSettings became dirty while validating the targeted persistence barrier. " +
                    "The global-state transaction was retained for fail-closed recovery.");
            }

            transaction.MarkGlobalMutationApplied(
                persistenceToken,
                CaptureCurrentOwnedPlayerSettings(),
                HasRequestedPlayerSettingsMutation(version));
            if (EditorUtility.IsDirty(playerSettingsAsset))
            {
                throw new IOException(
                    "PlayerSettings became dirty while the authorized post-image was being journaled. " +
                    "The global-state transaction was retained for fail-closed recovery.");
            }
        }

        private bool HasRequestedPlayerSettingsMutation(BuildVersionContext version)
        {
            return originalScriptingBackend != request.ScriptingBackend
                || !string.Equals(originalCompanyName, request.CompanyName, StringComparison.Ordinal)
                || !string.Equals(originalProductName, request.ProductName, StringComparison.Ordinal)
                || !string.Equals(originalBundleVersion, version.ApplicationVersion, StringComparison.Ordinal)
                || !string.Equals(
                    originalApplicationIdentifier,
                    request.ApplicationIdentifier,
                    StringComparison.Ordinal)
                || (request.Target == BuildTarget.Android
                    && originalAndroidBundleVersionCode != checked((int)version.BuildNumber))
                || (request.Target == BuildTarget.iOS
                    && !string.Equals(
                        originalIosBuildNumber,
                        version.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal))
                || PlayerSettingsLicensePolicy.RequiresMutation(originalSplashState);
        }

        private PlayerSettingsOwnedState CaptureOriginalOwnedPlayerSettings()
        {
            return new PlayerSettingsOwnedState(
                (int)originalScriptingBackend,
                originalCompanyName,
                originalProductName,
                originalBundleVersion,
                originalApplicationIdentifier,
                originalAndroidBundleVersionCode,
                originalIosBuildNumber,
                originalExportAndroidProject,
                originalDevelopmentBuild,
                originalEditorBuildScenes,
                originalSplashState,
                originalPreloadedAssetIds);
        }

        private PlayerSettingsOwnedState CaptureCurrentOwnedPlayerSettings()
        {
            return new PlayerSettingsOwnedState(
                (int)PlayerSettings.GetScriptingBackend(request.NamedTarget),
                PlayerSettings.companyName,
                PlayerSettings.productName,
                PlayerSettings.bundleVersion,
                PlayerSettings.GetApplicationIdentifier(request.NamedTarget),
                PlayerSettings.Android.bundleVersionCode,
                PlayerSettings.iOS.buildNumber,
                EditorUserBuildSettings.exportAsGoogleAndroidProject,
                EditorUserBuildSettings.development,
                CaptureEditorBuildScenes(),
                PlayerSettingsLicensePolicy.Capture(playerSettingsAsset),
                PlayerSettingsPreloadedAssetPolicy.Capture());
        }

        private static bool RequiresRecipeScenes(BuildRequest request)
        {
            IReadOnlyList<BuildStepInvocation> hotUpdateInvocations =
                request.GetInvocationsByStepType(BuildStepTypeIds.HotUpdate);
            for (int index = 0; index < hotUpdateInvocations.Count; index++)
            {
                if (hotUpdateInvocations[index].Incrementality ==
                    BuildIncrementality.Clean)
                {
                    return true;
                }
            }

            return false;
        }

        private static EditorBuildSettingsScene[] CreateRequestedEditorBuildScenes(
            IReadOnlyList<string> paths)
        {
            var scenes = new EditorBuildSettingsScene[paths.Count];
            for (int index = 0; index < paths.Count; index++)
            {
                scenes[index] = new EditorBuildSettingsScene(paths[index], true);
            }

            return scenes;
        }

        private static EditorBuildSettingsScene[] CreateEditorBuildSettingsScenes(
            IReadOnlyList<EditorBuildSceneState> states)
        {
            var scenes = new EditorBuildSettingsScene[states.Count];
            for (int index = 0; index < states.Count; index++)
            {
                scenes[index] = new EditorBuildSettingsScene(
                    states[index].Path,
                    states[index].Enabled);
            }

            return scenes;
        }

        private static EditorBuildSceneState[] CreateRequestedEditorBuildSceneStates(
            IReadOnlyList<string> paths)
        {
            var scenes = new EditorBuildSceneState[paths.Count];
            for (int index = 0; index < paths.Count; index++)
            {
                scenes[index] = new EditorBuildSceneState(paths[index], true);
            }

            return scenes;
        }

        private static EditorBuildSceneState[] CaptureEditorBuildScenes()
        {
            EditorBuildSettingsScene[] configured =
                EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            var result = new EditorBuildSceneState[configured.Length];
            for (int index = 0; index < configured.Length; index++)
            {
                EditorBuildSettingsScene scene = configured[index];
                result[index] = new EditorBuildSceneState(
                    scene?.path,
                    scene != null && scene.enabled);
            }

            return result;
        }

        private static bool EditorBuildScenesEqual(
            IReadOnlyList<EditorBuildSceneState> left,
            IReadOnlyList<EditorBuildSceneState> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (left[index].Enabled != right[index].Enabled
                    || !string.Equals(
                        left[index].Path,
                        right[index].Path,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void RecoverInterruptedUnityState(GlobalBuildStateTransaction transaction)
        {
            transaction.RestorePendingEditorUserState();
            transaction.RestorePendingTransaction();
            PlayerSettings loadedSettings = GetPlayerSettingsAsset();
            EditorUtility.ClearDirty(loadedSettings);
            if (EditorUtility.IsDirty(loadedSettings))
            {
                throw new InvalidOperationException(
                    "PlayerSettings remained dirty after interrupted transaction recovery.");
            }

            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (transaction.PendingRecoveryHasVersionInfo)
            {
                string recoveredAssetPath = transaction.PendingRecoveryVersionInfoAssetPath;
                UnityEngine.Object recoveredAsset = AssetDatabase.LoadMainAssetAtPath(recoveredAssetPath);
                if (transaction.PendingRecoveryVersionInfoOriginallyExisted)
                {
                    if (!(recoveredAsset is VersionInfoData))
                    {
                        throw new InvalidOperationException(
                            $"Recovered VersionInfoData could not be loaded after AssetDatabase refresh: '{recoveredAssetPath}'.");
                    }

                    EditorUtility.ClearDirty(recoveredAsset);
                    if (EditorUtility.IsDirty(recoveredAsset))
                    {
                        throw new InvalidOperationException(
                            $"Recovered VersionInfoData remained dirty: '{recoveredAssetPath}'.");
                    }
                }
                else if (recoveredAsset != null)
                {
                    throw new InvalidOperationException(
                        $"Transient VersionInfoData remained loaded after interrupted recovery: '{recoveredAssetPath}'.");
                }
            }

            transaction.ConfirmPendingRecovery();
        }

        internal static void EnsureActiveBuildTargetMatches(
            BuildRequest request,
            BuildTarget activeTarget)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (activeTarget == request.Target)
            {
                return;
            }

            string resolution = request.BatchMode
                ? $"Restart Unity with the native '-buildTarget {BuildCommandLine.GetUnityBuildTargetArgument(request.Target)}' argument before invoking the build entry point."
                : $"Select '{request.Target}' in File > Build Settings, wait for Unity to finish importing and compiling, then invoke the build again.";
            throw new BuildFailedException(
                $"Active build target '{activeTarget}' does not match requested target '{request.Target}'. " +
                resolution + " The build pipeline never switches active targets synchronously because Unity may compile scripts and reload the domain.");
        }

    }

    internal sealed class VersionInfoAssetScope : IDisposable
    {
        private readonly string assetPath;
        private readonly string absolutePath;
        private readonly bool assetExisted;
        private readonly GlobalBuildStateTransaction transaction;
        private bool disposed;

        private VersionInfoAssetScope(
            string assetPath,
            GlobalBuildStateTransaction transaction)
        {
            this.assetPath = assetPath;
            this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existingAsset != null && EditorUtility.IsDirty(existingAsset))
            {
                throw new InvalidOperationException(
                    $"VersionInfoData has unsaved changes and cannot be used transactionally: '{assetPath}'. Save or revert it before building.");
            }

            assetExisted = File.Exists(absolutePath);
            if (assetExisted && !(existingAsset is VersionInfoData))
            {
                throw new InvalidOperationException(
                    $"Version info path is occupied by an incompatible asset: '{assetPath}'.");
            }

            if (!assetExisted && existingAsset != null)
            {
                throw new InvalidOperationException(
                    $"Version info path is occupied by an incompatible virtual asset: '{assetPath}'.");
            }
        }

        public static VersionInfoAssetScope Create(string assetPath, BuildVersionContext version)
        {
            ValidateAssetPath(assetPath);
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            GlobalBuildStateTransaction transaction = GlobalBuildStateTransaction.RequireCurrent();
            var scope = new VersionInfoAssetScope(assetPath, transaction);
            try
            {
                transaction.PrepareVersionInfo(assetPath);
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                string parentAssetPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(parentAssetPath))
                {
                    throw new DirectoryNotFoundException(
                        $"The transactional VersionInfoData parent could not be imported: '{parentAssetPath}'.");
                }

                scope.WriteVersionInfoAsset(version);
                return scope;
            }
            catch (Exception writeException)
            {
                try
                {
                    scope.Dispose();
                }
                catch (Exception restoreException)
                {
                    throw new AggregateException(
                        "Failed to write and restore transient VersionInfoData.",
                        writeException,
                        restoreException);
                }

                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            transaction.RestoreVersionInfoFiles();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (assetExisted)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                VersionInfoData restored = AssetDatabase.LoadAssetAtPath<VersionInfoData>(assetPath);
                if (restored == null)
                {
                    throw new InvalidOperationException(
                        $"Restored VersionInfoData could not be loaded: '{assetPath}'.");
                }

                EditorUtility.ClearDirty(restored);
                if (EditorUtility.IsDirty(restored))
                {
                    throw new InvalidOperationException(
                        $"Restored VersionInfoData remained dirty: '{assetPath}'.");
                }
            }
            else
            {
                if (File.Exists(absolutePath)
                    || File.Exists(absolutePath + ".meta")
                    || AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    throw new IOException(
                        $"Transient VersionInfoData still exists after restoration: '{assetPath}'.");
                }
            }

            transaction.ConfirmVersionInfoRestored();
        }

        private static void ValidateAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)
                || assetPath.Contains("\\")
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"VersionInfoData path must be a project-relative .asset path below Assets: '{assetPath}'.");
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    assetPath,
                    "VersionInfoData path");
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"VersionInfoData path is not portable: '{assetPath}'.",
                    exception);
            }
        }

        private void WriteVersionInfoAsset(BuildVersionContext version)
        {
            string stageAssetPath = transaction.VersionInfoStageAssetPath;
            string targetObjectName = Path.GetFileNameWithoutExtension(assetPath);
            VersionInfoData data = ScriptableObject.CreateInstance<VersionInfoData>();
            try
            {
                // CreateAsset requires the main object name to match the staging
                // filename. Serialize the final target name only after creation so
                // the bytes sealed by MarkVersionStageReady already match the
                // installed asset and never need an unjournaled post-install edit.
                data.name = Path.GetFileNameWithoutExtension(stageAssetPath);
                AssetDatabase.CreateAsset(data, stageAssetPath);
                data.commitHash = version.CommitHash;
                data.commitCount = version.CommitCount;
                data.commitBranch = version.Branch;
                data.commitDate = version.CommitDate;
                data.buildDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                data.name = targetObjectName;
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }
            catch
            {
                if (AssetDatabase.LoadMainAssetAtPath(stageAssetPath) == null && data != null)
                {
                    UnityEngine.Object.DestroyImmediate(data);
                }

                throw;
            }

            transaction.MarkVersionStageReady();
            transaction.PublishStagedVersionInfo();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            VersionInfoData installed = AssetDatabase.LoadAssetAtPath<VersionInfoData>(assetPath);
            if (installed == null)
            {
                throw new InvalidOperationException(
                    $"Installed transient VersionInfoData could not be loaded: '{assetPath}'.");
            }

            if (!string.Equals(installed.name, targetObjectName, StringComparison.Ordinal))
            {
                throw new IOException(
                    $"Installed transient VersionInfoData has unexpected main object name '{installed.name}'. " +
                    $"Expected '{targetObjectName}'.");
            }

            transaction.RefreshInstalledVersionIdentity();
            if (assetExisted && AssetDatabase.LoadMainAssetAtPath(stageAssetPath) != null)
            {
                if (!AssetDatabase.DeleteAsset(stageAssetPath))
                {
                    throw new IOException(
                        $"Failed to remove VersionInfoData staging asset: '{stageAssetPath}'.");
                }
            }
        }
    }

    internal static class BuildVersionResolver
    {
        public static BuildVersionContext Resolve(BuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return Resolve(request, VersionControlFactory.CreateDetectedProvider());
        }

        internal static BuildVersionContext Resolve(
            BuildRequest request,
            IVersionControlProvider provider)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            BuildIdentityOverride identityOverride = request.IdentityOverride;
            bool allowLocalFallback = !request.BatchMode
                && (request.Purpose == BuildPurpose.Development
                    || request.Purpose == BuildPurpose.LocalReleasePreview);
            VersionControlMetadata metadata = null;
            long detectedCommitCount = 0L;
            string detectionFailure = null;

            if (provider == null)
            {
                detectionFailure = "No supported version-control provider was detected.";
            }
            else
            {
                try
                {
                    metadata = provider.Capture();
                    detectedCommitCount = ValidateMetadata(metadata);
                }
                catch (Exception exception)
                {
                    metadata = null;
                    detectionFailure =
                        "Version-control metadata capture failed: " + exception.Message;
                }
            }

            // Interactive local iterations (local release previews and non-batch
            // development builds) never consume the version-controlled commit-count
            // version namespace. Route them to the stable local version even when VC
            // metadata is available, so repeated local builds in the same workspace
            // reuse a dedicated, overwritable version instead of colliding with an
            // existing commit-count directory. An explicit source-identity override
            // still wins because it intentionally pins a specific version.
            if (!request.BatchMode
                && (request.Purpose == BuildPurpose.LocalReleasePreview
                    || request.Purpose == BuildPurpose.Development)
                && !identityOverride.HasSourceIdentity)
            {
                string localVersionReason = detectionFailure
                    ?? "Local iterative build resolves a stable, non-version-controlled local version.";
                return CreateLocalVersion(request, identityOverride, localVersionReason);
            }

            if (metadata == null)
            {
                if (identityOverride.HasSourceIdentity
                    && identityOverride.BuildNumber.HasValue)
                {
                    Debug.LogWarning(
                        "[BuildPipeline] Using the explicit source and build identity because local version-control detection is unavailable. " +
                        detectionFailure);
                    return CreateExplicitVersionWithoutDetection(request, identityOverride);
                }

                if (allowLocalFallback
                    && !identityOverride.HasSourceIdentity)
                {
                    return CreateLocalVersion(
                        request,
                        identityOverride,
                        detectionFailure);
                }

                throw new BuildFailedException(
                    detectionFailure + " Batch-mode and release builds require either reliable local version-control metadata " +
                    "or a complete explicit source identity and build number.");
            }

            ValidateExplicitSourceMatchesDetected(identityOverride, metadata);

            long detectedBuildNumber = Math.Max(1L, detectedCommitCount);
            long effectiveBuildNumber = identityOverride.BuildNumber
                ?? detectedBuildNumber;
            ValidateNativeBuildNumber(request.Target, effectiveBuildNumber);
            bool hasExplicitIdentity =
                identityOverride.BuildNumber.HasValue
                || identityOverride.HasSourceIdentity;
            string effectiveProvider = identityOverride.HasSourceIdentity
                ? identityOverride.SourceProvider
                : metadata.ProviderId;
            string effectiveRevision = identityOverride.HasSourceIdentity
                ? identityOverride.SourceRevision
                : metadata.CommitHash;
            string effectiveBranch = identityOverride.HasSourceIdentity
                ? identityOverride.SourceBranch
                : metadata.BranchName;
            return new BuildVersionContext(
                request.ApplicationVersion,
                CreatePackageVersion(request.ApplicationVersion, effectiveBuildNumber),
                effectiveBuildNumber,
                effectiveRevision,
                metadata.CommitCount,
                effectiveBranch,
                metadata.CommitDate,
                effectiveProvider,
                metadata.Workspace,
                hasExplicitIdentity
                    ? BuildIdentityOrigin.ExplicitOverride
                    : BuildIdentityOrigin.VersionControl,
                metadata.CommitHash,
                metadata.CommitCount,
                metadata.BranchName,
                metadata.CommitDate,
                metadata.ProviderId,
                detectedBuildNumber,
                identityOverride.CiProvider,
                identityOverride.CiRunId);
        }

        // Reserved build-number sentinels for interactive local iterations.
        // They are intentionally placed far above any realistic version-control
        // commit count so a repeated local build can never collide with a committed
        // release/CI package directory, and each local purpose gets its own stable
        // slot so Development and LocalReleasePreview output never overwrite each other.
        private const long LocalDevelopmentBuildNumber = 9000001L;
        private const long LocalPreviewBuildNumber = 9000002L;

        private static BuildVersionContext CreateLocalVersion(
            BuildRequest request,
            BuildIdentityOverride identityOverride,
            string reason)
        {
            bool localPreview = request.Purpose == BuildPurpose.LocalReleasePreview;
            string displayName = localPreview
                ? "Local Optimized Preview"
                : "local Development";
            Debug.LogWarning(
                "[BuildPipeline] Using explicit " + displayName +
                " version metadata. " + reason);
            const string CommitCount = "0";
            long buildNumber = identityOverride.BuildNumber
                ?? (localPreview ? LocalPreviewBuildNumber : LocalDevelopmentBuildNumber);
            ValidateNativeBuildNumber(request.Target, buildNumber);
            return new BuildVersionContext(
                request.ApplicationVersion,
                CreatePackageVersion(request.ApplicationVersion, buildNumber),
                buildNumber,
                localPreview ? "local-preview" : "local",
                CommitCount,
                localPreview ? "local-preview" : "local-development",
                "unversioned",
                localPreview ? "LocalPreview" : "LocalDevelopment",
                VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.MetadataUnavailable),
                localPreview
                    ? BuildIdentityOrigin.LocalPreview
                    : BuildIdentityOrigin.LocalDevelopment,
                ciProvider: identityOverride.CiProvider,
                ciRunId: identityOverride.CiRunId);
        }

        private static BuildVersionContext CreateExplicitVersionWithoutDetection(
            BuildRequest request,
            BuildIdentityOverride identityOverride)
        {
            long buildNumber = identityOverride.BuildNumber.Value;
            ValidateNativeBuildNumber(request.Target, buildNumber);
            string buildNumberText = buildNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return new BuildVersionContext(
                request.ApplicationVersion,
                CreatePackageVersion(request.ApplicationVersion, buildNumber),
                buildNumber,
                identityOverride.SourceRevision,
                buildNumberText,
                identityOverride.SourceBranch,
                string.Empty,
                identityOverride.SourceProvider,
                VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.MetadataUnavailable),
                BuildIdentityOrigin.ExplicitOverride,
                ciProvider: identityOverride.CiProvider,
                ciRunId: identityOverride.CiRunId);
        }

        private static void ValidateExplicitSourceMatchesDetected(
            BuildIdentityOverride identityOverride,
            VersionControlMetadata metadata)
        {
            if (!identityOverride.HasSourceIdentity)
            {
                return;
            }

            if (!string.Equals(
                    identityOverride.SourceProvider,
                    metadata.ProviderId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    identityOverride.SourceRevision,
                    metadata.CommitHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "Explicit source identity does not match the detected workspace revision. " +
                    $"Explicit={identityOverride.SourceProvider}:{identityOverride.SourceRevision}, " +
                    $"detected={metadata.ProviderId}:{metadata.CommitHash}.");
            }
        }

        private static long ValidateMetadata(VersionControlMetadata metadata)
        {
            if (metadata == null)
            {
                throw new InvalidOperationException(
                    "Version-control provider returned no metadata snapshot.");
            }

            ValidateBoundedText(metadata.ProviderId, "provider id", 64);
            ValidateBoundedText(metadata.CommitHash, "commit hash", 128);
            ValidateBoundedText(metadata.BranchName, "branch name", 512);
            ValidateBoundedText(metadata.CommitDate, "commit date", 128);
            if (!long.TryParse(
                    metadata.CommitCount,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long count)
                || count < 0)
            {
                throw new InvalidOperationException(
                    "Version-control provider returned an invalid commit count.");
            }

            return count;
        }

        private static string CreatePackageVersion(
            string applicationVersion,
            long buildNumber)
        {
            return applicationVersion + "." + buildNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void ValidateNativeBuildNumber(
            BuildTarget target,
            long buildNumber)
        {
            if (buildNumber < 1L || buildNumber > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"The effective native build number must be between 1 and {int.MaxValue}.");
            }

            if (target == BuildTarget.Android && buildNumber > 2100000000L)
            {
                throw new InvalidOperationException(
                    "The effective Android build number exceeds Google Play's maximum versionCode of 2100000000.");
            }
        }

        private static void ValidateBoundedText(
            string value,
            string displayName,
            int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            {
                throw new InvalidOperationException(
                    $"Version-control {displayName} is empty or exceeds {maximumLength} characters.");
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]))
                {
                    throw new InvalidOperationException(
                        $"Version-control {displayName} contains a control character.");
                }
            }
        }
    }
}
