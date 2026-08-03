using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal static class AddressablesBuilder
    {
        private const string LogTag = "[Addressables]";
        private const int MaximumConfigurationAssetBytes = 32 * 1024 * 1024;
        private const int MaximumVersionArtifactBytes = 64 * 1024;
        private const int AddressablesGeneratedChildPathReserve = 128;
        internal const string VersionArtifactTemporaryFileName = ".bp-version.tmp";
        internal const string VersionArtifactBackupFileName = ".bp-version.bak";
        private static readonly object ContentBuildGate = new object();
        private static bool contentBuildActive;

        internal static void Build(
            BuildTarget buildTarget,
            string contentVersion,
            AddressablesBuildConfig config,
            bool cleanBuild)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            RunInContentBuildScope(projectRoot, () =>
            {
                AddressablesSettingsTransaction.RecoverPending(projectRoot);
                RecoverPendingPublicationUnderLock(projectRoot, buildTarget, config);
                BuildInternal(projectRoot, buildTarget, contentVersion, config, cleanBuild);
            });
        }

        internal static void RecoverPendingPublication(
            BuildTarget buildTarget,
            AddressablesBuildConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            RunInContentBuildScope(projectRoot, () =>
            {
                AddressablesSettingsTransaction.RecoverPending(projectRoot);
                RecoverPendingPublicationUnderLock(projectRoot, buildTarget, config);
            });
        }

        private static void RecoverPendingPublicationUnderLock(
            string projectRoot,
            BuildTarget buildTarget,
            AddressablesBuildConfig config)
        {
            string publicationRoot = ResolvePublicationRoot(projectRoot, config.buildOutputDirectory);
            string destination = Path.Combine(publicationRoot, buildTarget.ToString());
            AddressablesPublicationTransaction.RecoverPending(
                projectRoot,
                publicationRoot,
                destination);
        }

        private static string ResolvePublicationRoot(string projectRoot, string configuredOutputDirectory)
        {
            string targetDirectory = string.IsNullOrWhiteSpace(configuredOutputDirectory)
                ? AddressablesBuildConfig.DefaultBuildOutputDirectory
                : configuredOutputDirectory;
            return BuildPathPolicy.ResolveBuildRoot(projectRoot, targetDirectory);
        }

        private static void BuildInternal(
            string projectRoot,
            BuildTarget buildTarget,
            string contentVersion,
            AddressablesBuildConfig config,
            bool cleanBuild)
        {
            ValidatePortablePathSegment(contentVersion, "Addressables content version");

            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                throw new InvalidOperationException(
                    $"Addressables build target '{buildTarget}' does not match active target '{EditorUserBuildSettings.activeBuildTarget}'. " +
                    "Switch the active target before building content.");
            }

            bool useBuildRemoteCatalog = config.buildRemoteCatalog;
            bool useCopyToOutputDirectory = config.copyToOutputDirectory;
            string useBuildOutputDirectory = config.buildOutputDirectory;
            Debug.Log(
                $"{LogTag} Building content. Target={buildTarget}, Version={contentVersion}, " +
                $"RemoteCatalog={useBuildRemoteCatalog}, Publish={useCopyToOutputDirectory}.");

            Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");

            if (settingsType == null)
            {
                throw new InvalidOperationException("Addressables is selected but the package is not installed.");
            }

            object settings = null;
            PropertyInfo buildRemoteCatalogProperty = null;
            PropertyInfo overridePlayerVersionProperty = null;
            object originalBuildRemoteCatalog = null;
            object originalOverridePlayerVersion = null;
            IReadOnlyList<AssetFileSnapshot> configurationSnapshots = null;
            AddressablesSettingsTransaction settingsTransaction = null;
            PendingAddressablesPublication pendingPublication = null;
            Exception buildFailure = null;
            bool contentBuildSucceeded = false;
            try
            {
                settings = GetDefaultSettings();
                if (settings == null)
                {
                    throw new InvalidOperationException("AddressableAssetSettings was not found. Configure Addressables before building.");
                }

                IReadOnlyList<string> dirtyConfigurationAssets = GetDirtyConfigurationAssetPaths(
                    settings,
                    settingsType,
                    includeSettingsAsset: true);
                if (dirtyConfigurationAssets.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Addressables configuration has unsaved changes. Save or revert before building: " +
                        string.Join(", ", dirtyConfigurationAssets));
                }

                ValidateResolvedPublicationSettings(
                    config,
                    settings,
                    settingsType,
                    projectRoot,
                    useBuildRemoteCatalog,
                    useCopyToOutputDirectory);
                ValidateGeneratedOutputPathBudgets(
                    settings,
                    settingsType,
                    projectRoot,
                    buildTarget,
                    useBuildRemoteCatalog);

                buildRemoteCatalogProperty = ReflectionCache.GetProperty(settingsType, "BuildRemoteCatalog", BindingFlags.Public | BindingFlags.Instance);
                overridePlayerVersionProperty = ReflectionCache.GetProperty(settingsType, "OverridePlayerVersion", BindingFlags.Public | BindingFlags.Instance);
                if (buildRemoteCatalogProperty == null || overridePlayerVersionProperty == null)
                {
                    throw new MissingMemberException(settingsType.FullName, "BuildRemoteCatalog/OverridePlayerVersion");
                }

                originalBuildRemoteCatalog = buildRemoteCatalogProperty.GetValue(settings);
                originalOverridePlayerVersion = overridePlayerVersionProperty.GetValue(settings);
                configurationSnapshots = CaptureConfigurationAssetSnapshots(settings, settingsType);
                settingsTransaction = AddressablesSettingsTransaction.Begin(
                    projectRoot,
                    configurationSnapshots);

                buildRemoteCatalogProperty.SetValue(settings, useBuildRemoteCatalog);

                if (cleanBuild)
                {
                    ClearActiveBuilderCache(settings, settingsType, buildTarget);
                }

                overridePlayerVersionProperty.SetValue(settings, contentVersion);

                object buildResult = BuildWithSettings(settingsType);

                if (buildResult != null)
                {
                    bool isSuccess = CheckBuildResult(buildResult);
                    if (isSuccess)
                    {
                        SaveVersionDataToAddressablesBuildPath(contentVersion, buildTarget);

                        if (useCopyToOutputDirectory)
                        {
                            string outputDir = useBuildOutputDirectory;
                            if (string.IsNullOrEmpty(outputDir))
                            {
                                outputDir = AddressablesBuildConfig.DefaultBuildOutputDirectory;
                            }

                            pendingPublication = CopyBuildResultToOutput(
                                buildTarget,
                                outputDir,
                                useBuildRemoteCatalog,
                                buildResult,
                                settings,
                                settingsType,
                                contentVersion,
                                config);
                        }

                        contentBuildSucceeded = true;
                    }
                    else
                    {
                        string errorInfo = GetBuildError(buildResult);
                        throw new Exception($"[Addressables] Build content failed: {errorInfo}");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Addressables content build returned a null result.");
                }
            }
            catch (Exception ex)
            {
                buildFailure = ex;
                throw;
            }
            finally
            {
                Exception restoreFailure = null;
                if (settingsTransaction != null)
                {
                    restoreFailure = RestoreAddressablesSettings(
                        settings,
                        buildRemoteCatalogProperty,
                        originalBuildRemoteCatalog,
                        overridePlayerVersionProperty,
                        originalOverridePlayerVersion);

                }

                Exception settingsFinalizationFailure = null;
                if (settingsTransaction != null)
                {
                    settingsFinalizationFailure = FinalizeSettingsTransaction(
                        settingsTransaction);
                }

                Exception publicationFinalizationFailure = null;
                if (pendingPublication != null)
                {
                    try
                    {
                        if (buildFailure == null
                            && restoreFailure == null
                            && settingsFinalizationFailure == null)
                        {
                            pendingPublication.Commit();
                        }
                        else
                        {
                            pendingPublication.Abort();
                        }
                    }
                    catch (Exception exception)
                    {
                        publicationFinalizationFailure = exception;
                    }
                }

                if (restoreFailure != null
                    || settingsFinalizationFailure != null
                    || publicationFinalizationFailure != null)
                {
                    var failures = new List<Exception>();
                    if (buildFailure != null)
                    {
                        failures.Add(buildFailure);
                    }

                    if (restoreFailure != null)
                    {
                        failures.Add(new InvalidOperationException(
                            "Failed to restore Addressables settings.",
                            restoreFailure));
                    }

                    if (settingsFinalizationFailure != null)
                    {
                        failures.Add(new InvalidOperationException(
                            "Failed to finalize the durable Addressables settings transaction.",
                            settingsFinalizationFailure));
                    }

                    if (publicationFinalizationFailure != null)
                    {
                        failures.Add(new InvalidOperationException(
                            "Failed to finalize the staged Addressables publication.",
                            publicationFinalizationFailure));
                    }

                    throw failures.Count == 1
                        ? failures[0]
                        : new AggregateException(
                            "Addressables build, settings restoration, settings transaction, and/or publication finalization failed.",
                            failures);
                }

                if (contentBuildSucceeded)
                {
                    Debug.Log($"{LogTag} Content build completed for target '{buildTarget}'.");
                }
            }
        }

        internal static object GetDefaultSettings()
        {
            Type defaultObjectType = ReflectionCache.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
            if (defaultObjectType == null)
            {
                return null;
            }

            PropertyInfo settingsProperty = ReflectionCache.GetProperty(
                defaultObjectType,
                "Settings",
                BindingFlags.Public | BindingFlags.Static);
            if (settingsProperty == null || !settingsProperty.CanRead)
            {
                throw new MissingMemberException(defaultObjectType.FullName, "Settings");
            }

            return settingsProperty.GetValue(null);
        }

        internal static IReadOnlyList<string> GetDirtyConfigurationAssetPaths(
            object settings,
            Type settingsType,
            bool includeSettingsAsset)
        {
            var dirtyPaths = new List<string>();
            foreach (string assetPath in GetConfigurationAssetPaths(
                settings,
                settingsType,
                includeSettingsAsset))
            {
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (UnityEngine.Object asset in assets)
                {
                    if (asset != null && EditorUtility.IsDirty(asset))
                    {
                        dirtyPaths.Add(assetPath);
                        break;
                    }
                }
            }

            return dirtyPaths;
        }

        private static IReadOnlyList<string> GetConfigurationAssetPaths(
            object settings,
            Type settingsType,
            bool includeSettingsAsset)
        {
            var assetPaths = new HashSet<string>(StringComparer.Ordinal);
            if (includeSettingsAsset && settings is UnityEngine.Object settingsObject)
            {
                AddAssetPath(settingsObject, assetPaths);
            }

            PropertyInfo groupsProperty = ReflectionCache.GetProperty(
                settingsType,
                "groups",
                BindingFlags.Public | BindingFlags.Instance);
            if (groupsProperty?.GetValue(settings) is IEnumerable groups)
            {
                foreach (object group in groups)
                {
                    if (group is UnityEngine.Object groupObject)
                    {
                        AddAssetPath(groupObject, assetPaths);
                    }

                    if (group != null)
                    {
                        AddUnityObjectPropertyAssetPaths(
                            group,
                            group.GetType(),
                            assetPaths,
                            "Schemas");
                    }
                }
            }

            AddUnityObjectPropertyAssetPaths(
                settings,
                settingsType,
                assetPaths,
                "ActivePlayerDataBuilder");

            return new List<string>(assetPaths);
        }

        internal static IReadOnlyList<AssetFileSnapshot> CaptureConfigurationAssetSnapshots(
            object settings,
            Type settingsType)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var snapshots = new List<AssetFileSnapshot>();
            foreach (string assetPath in GetConfigurationAssetPaths(
                settings,
                settingsType,
                includeSettingsAsset: true))
            {
                string normalizedAssetPath = assetPath.Replace('\\', '/');
                if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Addressables configuration asset must be inside Assets: '{assetPath}'.");
                }

                string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
                absolutePath = BuildPathPolicy.EnsureSafeReadableFile(
                    Application.dataPath,
                    absolutePath);

                DateTime originalLastWriteTimeUtc = File.GetLastWriteTimeUtc(absolutePath);
                FileAttributes originalAttributes = File.GetAttributes(absolutePath);
                byte[] originalBytes = ReadConfigurationAssetBounded(absolutePath);
                if (File.GetLastWriteTimeUtc(absolutePath) != originalLastWriteTimeUtc
                    || File.GetAttributes(absolutePath) != originalAttributes)
                {
                    throw new IOException(
                        $"Addressables configuration asset changed while its snapshot was being captured: '{assetPath}'.");
                }

                snapshots.Add(new AssetFileSnapshot(
                    assetPath,
                    absolutePath,
                    originalBytes,
                    originalLastWriteTimeUtc,
                    originalAttributes));
            }

            if (snapshots.Count == 0)
            {
                throw new InvalidOperationException(
                    "Addressables configuration does not resolve to any persistent assets.");
            }

            return snapshots;
        }

        private static byte[] ReadConfigurationAssetBounded(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                if (stream.Length < 0 || stream.Length > MaximumConfigurationAssetBytes)
                {
                    throw new IOException(
                        $"Addressables configuration asset exceeds {MaximumConfigurationAssetBytes} bytes: '{path}'.");
                }

                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            $"Addressables configuration asset changed while it was read: '{path}'.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                {
                    throw new IOException(
                        $"Addressables configuration asset grew while it was read: '{path}'.");
                }

                return bytes;
            }
        }

        internal static void WriteNewTextDurably(string path, string content)
        {
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                path,
                "Addressables durable text artifact");
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(content ?? string.Empty);
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        internal static void WriteVersionArtifactDurably(
            string versionFilePath,
            string contentVersion)
        {
            ValidatePortablePathSegment(
                contentVersion,
                "Addressables content version");
            string finalPath = BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                versionFilePath,
                "Addressables version artifact");
            EnsureVersionArtifactPathIsInsideProject(finalPath);
            string directory = Path.GetDirectoryName(finalPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    $"Addressables version artifact directory is missing: '{directory}'.");
            }
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                directory,
                "Addressables version artifact directory");

            string temporaryPath = BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(directory, VersionArtifactTemporaryFileName),
                "Addressables temporary version artifact");
            string backupPath = BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(directory, VersionArtifactBackupFileName),
                "Addressables backup version artifact");
            RecoverVersionArtifactScratch(finalPath, temporaryPath, backupPath);

            var versionData = new VersionDataJson { contentVersion = contentVersion };
            WriteNewTextDurably(
                temporaryPath,
                JsonUtility.ToJson(versionData, true));
            ReadAndValidateVersionArtifact(temporaryPath, contentVersion);
            if (IsRegularVersionArtifact(finalPath))
            {
                File.Replace(temporaryPath, finalPath, backupPath);
            }
            else
            {
                File.Move(temporaryPath, finalPath);
            }

            ReadAndValidateVersionArtifact(finalPath, contentVersion);
            if (IsRegularVersionArtifact(backupPath))
            {
                ReadAndValidateVersionArtifact(backupPath, expectedContentVersion: null);
                DeleteVersionArtifactStrict(backupPath);
            }
        }

        private static void RecoverVersionArtifactScratch(
            string finalPath,
            string temporaryPath,
            string backupPath)
        {
            bool finalExists = IsRegularVersionArtifact(finalPath);
            bool temporaryExists = IsRegularVersionArtifact(temporaryPath);
            bool backupExists = IsRegularVersionArtifact(backupPath);
            if (finalExists)
            {
                ReadAndValidateVersionArtifact(finalPath, expectedContentVersion: null);
                if (temporaryExists)
                {
                    ReadAndValidateVersionArtifact(temporaryPath, expectedContentVersion: null);
                }

                if (backupExists)
                {
                    ReadAndValidateVersionArtifact(backupPath, expectedContentVersion: null);
                }

                DeleteVersionArtifactStrict(temporaryPath);
                DeleteVersionArtifactStrict(backupPath);
                return;
            }

            if (backupExists)
            {
                ReadAndValidateVersionArtifact(backupPath, expectedContentVersion: null);
                if (temporaryExists)
                {
                    ReadAndValidateVersionArtifact(temporaryPath, expectedContentVersion: null);
                }

                File.Move(backupPath, finalPath);
                ReadAndValidateVersionArtifact(finalPath, expectedContentVersion: null);
                DeleteVersionArtifactStrict(temporaryPath);
                return;
            }

            if (temporaryExists)
            {
                ReadAndValidateVersionArtifact(temporaryPath, expectedContentVersion: null);
                File.Move(temporaryPath, finalPath);
                ReadAndValidateVersionArtifact(finalPath, expectedContentVersion: null);
            }
        }

        private static bool IsRegularVersionArtifact(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    throw new InvalidOperationException(
                        $"Addressables version artifact path is not a regular file: '{path}'.");
                }

                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        internal static string ReadAndValidateVersionArtifact(
            string path,
            string expectedContentVersion)
        {
            EnsureVersionArtifactPathIsInsideProject(path);
            if (!IsRegularVersionArtifact(path))
            {
                throw new FileNotFoundException(
                    "Addressables version artifact is missing.",
                    path);
            }

            byte[] bytes;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                if (stream.Length <= 0 || stream.Length > MaximumVersionArtifactBytes)
                {
                    throw new InvalidDataException(
                        $"Addressables version artifact size is invalid: '{path}'.");
                }

                bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            $"Addressables version artifact changed while it was read: '{path}'.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                {
                    throw new IOException(
                        $"Addressables version artifact grew while it was read: '{path}'.");
                }
            }

            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                throw new InvalidDataException(
                    $"Addressables version artifact must use UTF-8 without BOM: '{path}'.");
            }

            VersionDataJson data;
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes);
                data = JsonUtility.FromJson<VersionDataJson>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Addressables version artifact JSON is invalid: '{path}'.",
                    exception);
            }

            if (data == null || string.IsNullOrWhiteSpace(data.contentVersion))
            {
                throw new InvalidDataException(
                    $"Addressables version artifact contentVersion is invalid: '{path}'.");
            }

            ValidatePortablePathSegment(
                data.contentVersion,
                "Addressables version artifact contentVersion");
            if (expectedContentVersion != null
                && !string.Equals(
                    data.contentVersion,
                    expectedContentVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Addressables version artifact does not contain the expected contentVersion '{expectedContentVersion}': '{path}'.");
            }

            return data.contentVersion;
        }

        private static void EnsureVersionArtifactPathIsInsideProject(string path)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath = Path.GetFullPath(path);
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, fullPath))
            {
                throw new InvalidOperationException(
                    $"Addressables version artifact must remain inside the Unity project: '{fullPath}'.");
            }

            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                fullPath);
        }

        private static void DeleteVersionArtifactStrict(string path)
        {
            if (!IsRegularVersionArtifact(path))
            {
                return;
            }

            File.Delete(path);
            if (IsRegularVersionArtifact(path))
            {
                throw new IOException(
                    $"Addressables version scratch still exists after deletion: '{path}'.");
            }
        }

        private static void AddUnityObjectPropertyAssetPaths(
            object owner,
            Type ownerType,
            ISet<string> paths,
            params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                PropertyInfo property = ReflectionCache.GetProperty(
                    ownerType,
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                object value = property?.GetValue(owner);
                if (value is UnityEngine.Object unityObject)
                {
                    AddAssetPath(unityObject, paths);
                    continue;
                }

                if (!(value is IEnumerable enumerable))
                {
                    continue;
                }

                foreach (object item in enumerable)
                {
                    if (item is UnityEngine.Object itemObject)
                    {
                        AddAssetPath(itemObject, paths);
                    }
                }
            }
        }

        private static void AddAssetPath(
            UnityEngine.Object asset,
            ISet<string> paths)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        /// <summary>
        /// Builds Addressables content using AddressableAssetSettings.BuildPlayerContent (standard API).
        /// </summary>
        private static object BuildWithSettings(Type settingsType)
        {
            MethodInfo buildMethod = null;
            MethodInfo[] allMethods = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (MethodInfo method in allMethods)
            {
                if (method.Name != "BuildPlayerContent")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].IsOut)
                {
                    buildMethod = method;
                    break;
                }
            }

            if (buildMethod == null)
            {
                throw new MissingMethodException(settingsType.FullName, "BuildPlayerContent");
            }

            ParameterInfo outParameter = buildMethod.GetParameters()[0];
            Type resultType = outParameter.ParameterType.GetElementType();
            if (resultType == null)
            {
                throw new MissingMethodException(
                    settingsType.FullName,
                    "BuildPlayerContent(out AddressablesPlayerBuildResult)");
            }

            try
            {
                object[] invokeParameters = { null };
                buildMethod.Invoke(null, invokeParameters);
                return invokeParameters[0];
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    "Addressables BuildPlayerContent threw an exception.",
                    exception.InnerException);
            }
        }

        private static bool CheckBuildResult(object buildResult)
        {
            if (buildResult == null) return false;

            Type resultType = buildResult.GetType();
            PropertyInfo errorProperty = ReflectionCache.GetProperty(
                resultType,
                "Error",
                BindingFlags.Public | BindingFlags.Instance);
            if (errorProperty == null || !errorProperty.CanRead)
            {
                throw new MissingMemberException(resultType.FullName, "Error");
            }

            object errorValue = errorProperty.GetValue(buildResult);
            return string.IsNullOrEmpty(errorValue?.ToString());
        }

        private static string GetBuildError(object buildResult)
        {
            if (buildResult == null) return "Unknown Error";

            Type resultType = buildResult.GetType();
            PropertyInfo errorProperty = ReflectionCache.GetProperty(
                resultType,
                "Error",
                BindingFlags.Public | BindingFlags.Instance);
            if (errorProperty == null || !errorProperty.CanRead)
            {
                throw new MissingMemberException(resultType.FullName, "Error");
            }

            return errorProperty.GetValue(buildResult)?.ToString() ?? "Unknown Error";
        }

        private static PendingAddressablesPublication CopyBuildResultToOutput(
            BuildTarget buildTarget,
            string outputDirectory,
            bool buildRemoteCatalog,
            object buildResult,
            object settings,
            Type settingsType,
            string contentVersion,
            AddressablesBuildConfig config)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string customDestRoot = ResolvePublicationRoot(projectRoot, outputDirectory);
            Directory.CreateDirectory(customDestRoot);

            string buildPath = GetAddressablesBuildPath(buildTarget);
            if (string.IsNullOrEmpty(buildPath) || !Directory.Exists(buildPath))
            {
                throw new DirectoryNotFoundException($"Addressables build path was not found: '{buildPath}'.");
            }

            const string versionFileName = "AddressablesVersion.json";
            string buildVersionPath = Path.Combine(buildPath, versionFileName);
            if (!File.Exists(buildVersionPath))
            {
                throw new FileNotFoundException(
                    "Addressables version artifact is required before publication.",
                    buildVersionPath);
            }

            List<PublicationFile> files = CreatePublicationFileList(
                projectRoot,
                buildPath,
                buildRemoteCatalog,
                buildResult,
                settings,
                settingsType,
                buildVersionPath,
                customDestRoot,
                contentVersion,
                config);
            string destinationDirectory = Path.Combine(customDestRoot, buildTarget.ToString());
            Debug.Log($"{LogTag} Publishing {files.Count} files to '{destinationDirectory}'.");
            return StageFilesTransactionally(
                projectRoot,
                customDestRoot,
                destinationDirectory,
                files,
                buildTarget,
                contentVersion,
                Path.Combine("PlayerData", versionFileName));
        }

        private static List<PublicationFile> CreatePublicationFileList(
            string projectRoot,
            string playerDataRoot,
            bool buildRemoteCatalog,
            object buildResult,
            object settings,
            Type settingsType,
            string versionFilePath,
            string publicationRoot,
            string contentVersion,
            AddressablesBuildConfig config)
        {
            var roots = new List<PublicationRoot>
            {
                new PublicationRoot("PlayerData", NormalizeSourceRoot(projectRoot, playerDataRoot))
            };
            bool allowExternalProfileSources =
                config != null && config.allowExternalProfilePublicationSources;

            string profileRemoteRoot = GetProfileBuildPath(settings, settingsType, "Remote.BuildPath");
            AddRemotePublicationRoot(
                roots,
                projectRoot,
                profileRemoteRoot,
                allowExternalProfileSources);

            string remoteCatalogRoot = buildRemoteCatalog
                ? GetRemoteCatalogBuildPath(settings, settingsType)
                : null;
            if (buildRemoteCatalog && IsUndefinedProfileValue(remoteCatalogRoot))
            {
                throw new InvalidOperationException(
                    "BuildRemoteCatalog is enabled, but RemoteCatalogBuildPath is empty or unsupported.");
            }

            string remoteCatalogSourceRoot = AddRemotePublicationRoot(
                roots,
                projectRoot,
                remoteCatalogRoot,
                allowExternalProfileSources);
            if (config?.additionalPublicationRoots != null)
            {
                foreach (AddressablesPublicationRoot additionalRoot in config.additionalPublicationRoots)
                {
                    if (additionalRoot == null)
                    {
                        throw new InvalidOperationException(
                            "Addressables additional publication roots cannot contain null entries.");
                    }

                    string validationError = ValidateAdditionalPublicationRoot(
                        additionalRoot,
                        projectRoot,
                        publicationRoot);
                    if (!string.IsNullOrEmpty(validationError))
                    {
                        throw new InvalidOperationException(validationError);
                    }

                    roots.Add(new PublicationRoot(
                        additionalRoot.destinationFolder,
                        BuildPathPolicy.ResolveBuildRoot(
                            projectRoot,
                            additionalRoot.sourceDirectory)));
                }
            }

            string contentStatePath = GetOptionalBuildResultPath(
                buildResult,
                projectRoot,
                "ContentStateFilePath");
            PublicationRoot contentStatePublicationRoot = null;
            if (!string.IsNullOrEmpty(contentStatePath))
            {
                string approvedContentStateRoot = ResolveContentStatePublicationRoot(
                    settings,
                    settingsType,
                    projectRoot,
                    contentStatePath,
                    remoteCatalogSourceRoot,
                    allowExternalProfileSources);
                contentStatePublicationRoot = new PublicationRoot(
                    "BuildMetadata",
                    approvedContentStateRoot);
                roots.Add(contentStatePublicationRoot);
            }

            EnsurePublicationRootsDoNotOverlapDestination(roots, publicationRoot);

            List<string> registryFiles = GetBuildRegistryFiles(buildResult, projectRoot);
            string outputPath = GetBuildResultOutputPath(buildResult, projectRoot);
            if (!string.IsNullOrEmpty(outputPath))
            {
                AddUniquePath(registryFiles, outputPath);
            }

            AddUniquePath(registryFiles, versionFilePath);
            if (!string.IsNullOrEmpty(contentStatePath))
            {
                AddUniquePath(registryFiles, contentStatePath);
            }
            if (registryFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "Addressables build result did not expose any files through FileRegistry.");
            }

            var files = new List<PublicationFile>(registryFiles.Count);
            var destinationOwners = new Dictionary<string, string>(PortableDestinationPathComparer);
            string expectedRemoteCatalogBaseName = buildRemoteCatalog
                ? GetExpectedRemoteCatalogBaseName(settings, settingsType, contentVersion)
                : null;
            bool remoteCatalogDataFound = !buildRemoteCatalog;
            bool remoteCatalogHashFound = !buildRemoteCatalog;
            foreach (string registryFile in registryFiles)
            {
                PublicationRoot root = contentStatePublicationRoot != null
                    && PathsEqual(contentStatePath, registryFile)
                        ? contentStatePublicationRoot
                        : FindBestPublicationRoot(roots, registryFile);
                if (root == null)
                {
                    throw new InvalidOperationException(
                        $"Addressables produced an artifact outside approved player/remote roots: '{registryFile}'. " +
                        "Use Addressables.BuildPath or the active Remote.BuildPath/RemoteCatalogBuildPath.");
                }

                string safeSource = BuildPathPolicy.EnsureSafeReadableFile(root.SourcePath, registryFile);
                string relativePath = GetRelativeChildPath(root.SourcePath, safeSource);
                string destinationRelativePath = (root.Kind + "/" + relativePath).Replace('\\', '/');
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    destinationRelativePath,
                    "Addressables publication artifact path");
                if (destinationOwners.TryGetValue(destinationRelativePath, out string existingSource))
                {
                    if (!string.Equals(existingSource, safeSource, PathComparison))
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication path collision at '{destinationRelativePath}'. " +
                            $"Sources: '{existingSource}' and '{safeSource}'.");
                    }

                    continue;
                }

                destinationOwners[destinationRelativePath] = safeSource;
                files.Add(new PublicationFile(safeSource, destinationRelativePath, root.Kind));
                if (buildRemoteCatalog
                    && !string.IsNullOrEmpty(remoteCatalogSourceRoot)
                    && IsPathInsideRoot(remoteCatalogSourceRoot, safeSource))
                {
                    string fileName = Path.GetFileName(safeSource);
                    if (string.Equals(
                        fileName,
                        expectedRemoteCatalogBaseName + ".hash",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        remoteCatalogHashFound = true;
                    }
                    else if (IsSupportedRemoteCatalogDataFile(
                        fileName,
                        expectedRemoteCatalogBaseName))
                    {
                        remoteCatalogDataFound = true;
                    }
                }
            }

            if (!remoteCatalogDataFound || !remoteCatalogHashFound)
            {
                throw new InvalidOperationException(
                    $"BuildRemoteCatalog is enabled, but FileRegistry does not contain both " +
                    $"'{expectedRemoteCatalogBaseName}.hash' and a supported catalog data file.");
            }

            files.Sort((left, right) =>
            {
                int destinationComparison = StringComparer.Ordinal.Compare(
                    left.DestinationRelativePath,
                    right.DestinationRelativePath);
                return destinationComparison != 0
                    ? destinationComparison
                    : StringComparer.Ordinal.Compare(left.SourcePath, right.SourcePath);
            });
            return files;
        }

        private static PendingAddressablesPublication StageFilesTransactionally(
            string projectRoot,
            string publicationRoot,
            string destinationDirectory,
            IReadOnlyList<PublicationFile> files,
            BuildTarget buildTarget,
            string contentVersion,
            string requiredPublishedRelativePath)
        {
            BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                destinationDirectory,
                publicationRoot,
                allowExternalOutput: false);

            var transaction = AddressablesPublicationTransaction.Begin(
                projectRoot,
                publicationRoot,
                destinationDirectory,
                buildTarget + "\n" + contentVersion);
            Exception failure = null;
            try
            {
                string stagingDirectory = transaction.StagingDirectory;
                ValidatePublicationArtifactPathBudgets(
                    destinationDirectory,
                    stagingDirectory,
                    files);
                transaction.Prepare();
                var manifestEntries = new AddressablesArtifactManifestEntry[files.Count];
                for (int index = 0; index < files.Count; index++)
                {
                    PublicationFile file = files[index];
                    string stagedPath = Path.GetFullPath(Path.Combine(stagingDirectory, file.DestinationRelativePath));
                    if (!BuildPathPolicy.IsStrictDescendant(stagingDirectory, stagedPath))
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication path escaped staging: '{file.DestinationRelativePath}'.");
                    }

                    string parent = Path.GetDirectoryName(stagedPath);
                    if (string.IsNullOrEmpty(parent))
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication path has no parent: '{stagedPath}'.");
                    }

                    BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                        stagedPath,
                        "Addressables staged artifact");
                    BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                        parent,
                        "Addressables staged artifact directory");
                    Directory.CreateDirectory(parent);
                    string sourceHash = CopyFileWithStableHash(
                        file.SourcePath,
                        stagedPath,
                        out long stagedSize);
                    string stagedHash = ComputeSha256(stagedPath);
                    if (!string.Equals(sourceHash, stagedHash, StringComparison.Ordinal))
                    {
                        throw new IOException(
                            $"Addressables staged artifact hash mismatch: '{file.DestinationRelativePath}'.");
                    }

                    manifestEntries[index] = new AddressablesArtifactManifestEntry
                    {
                        kind = file.Kind,
                        path = file.DestinationRelativePath.Replace('\\', '/'),
                        size = stagedSize,
                        sha256 = stagedHash
                    };
                }

                var manifest = new AddressablesArtifactManifest
                {
                    schemaVersion = 2,
                    buildTarget = buildTarget.ToString(),
                    contentVersion = contentVersion,
                    files = manifestEntries
                };
                string manifestPath = Path.Combine(stagingDirectory, "AddressablesArtifacts.json");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    manifestPath,
                    "Addressables staged artifact manifest");
                WriteNewTextDurably(
                    manifestPath,
                    JsonUtility.ToJson(manifest, true));

                AddressablesPublicationOwnership.WriteOwner(
                    stagingDirectory,
                    transaction.TransactionId);
                ValidatePublishedFiles(stagingDirectory, files, manifestPath);
                string stagedIdentity = AddressablesPublicationOwnership.CaptureIdentity(stagingDirectory);
                transaction.MarkStageReady(stagedIdentity);
                return new PendingAddressablesPublication(
                    transaction,
                    destinationDirectory,
                    files,
                    requiredPublishedRelativePath);
            }
            catch (Exception exception)
            {
                failure = exception;
                try
                {
                    transaction.Abort();
                }
                catch (Exception rollbackException)
                {
                    failure = new AggregateException(
                        "Addressables publication and rollback both failed.",
                        exception,
                        rollbackException);
                }
            }
            if (failure != null)
            {
                try
                {
                    transaction.Dispose();
                }
                catch (Exception disposeException)
                {
                    failure = failure == null
                        ? disposeException
                        : new AggregateException(
                            "Addressables publication and transaction disposal both failed.",
                            failure,
                            disposeException);
                }
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            throw new InvalidOperationException(
                "Addressables publication staging exited without a pending transaction.");
        }

        private static void ValidatePublicationArtifactPathBudgets(
            string destinationDirectory,
            string stagingDirectory,
            IReadOnlyList<PublicationFile> files)
        {
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                destinationDirectory,
                "Addressables publication destination");
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                stagingDirectory,
                "Addressables publication stage");

            foreach (PublicationFile file in files)
            {
                string stagedPath = Path.Combine(
                    stagingDirectory,
                    file.DestinationRelativePath);
                string publishedPath = Path.Combine(
                    destinationDirectory,
                    file.DestinationRelativePath);
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    stagedPath,
                    "Addressables staged artifact");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    publishedPath,
                    "Addressables published artifact");
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    Path.GetDirectoryName(stagedPath),
                    "Addressables staged artifact directory");
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    Path.GetDirectoryName(publishedPath),
                    "Addressables published artifact directory");
            }

            const string manifestFileName = "AddressablesArtifacts.json";
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stagingDirectory, manifestFileName),
                "Addressables staged artifact manifest");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(destinationDirectory, manifestFileName),
                "Addressables published artifact manifest");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(destinationDirectory, AddressablesPublicationOwnership.OwnerFileName),
                "Addressables published ownership marker");
        }

        private static void ValidatePublishedFiles(
            string root,
            IReadOnlyList<PublicationFile> files,
            string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException(
                    "Addressables publication manifest is missing.",
                    manifestPath);
            }

            foreach (PublicationFile file in files)
            {
                string path = Path.GetFullPath(Path.Combine(root, file.DestinationRelativePath));
                if (!BuildPathPolicy.IsStrictDescendant(root, path) || !File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "Addressables publication artifact is missing.",
                        path);
                }
            }
        }

        private static string CopyFileWithStableHash(
            string sourcePath,
            string destinationPath,
            out long copiedLength)
        {
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                sourcePath,
                "Addressables source artifact");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                destinationPath,
                "Addressables staged artifact");
            using (var source = new FileStream(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                copiedLength = source.Length;
                string sourceHash = ToHex(sha256.ComputeHash(source));
                source.Position = 0;
                using (var destination = new FileStream(
                           destinationPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           64 * 1024,
                           FileOptions.WriteThrough))
                {
                    source.CopyTo(destination, 64 * 1024);
                    destination.Flush(true);
                    if (destination.Length != copiedLength)
                    {
                        throw new IOException(
                            $"Addressables staged artifact length mismatch: '{destinationPath}'.");
                    }
                }

                return sourceHash;
            }
        }

        private static List<string> GetBuildRegistryFiles(object buildResult, string projectRoot)
        {
            if (buildResult == null)
            {
                throw new ArgumentNullException(nameof(buildResult));
            }

            Type resultType = buildResult.GetType();
            PropertyInfo registryProperty = ReflectionCache.GetProperty(
                resultType,
                "FileRegistry",
                BindingFlags.Public | BindingFlags.Instance);
            object registry = registryProperty?.GetValue(buildResult);
            if (registry == null)
            {
                throw new MissingMemberException(
                    resultType.FullName,
                    "FileRegistry");
            }

            MethodInfo getFilePathsMethod = ReflectionCache.GetMethod(
                registry.GetType(),
                "GetFilePaths",
                BindingFlags.Public | BindingFlags.Instance);
            if (getFilePathsMethod == null)
            {
                throw new MissingMethodException(registry.GetType().FullName, "GetFilePaths");
            }

            object value;
            try
            {
                value = getFilePathsMethod.Invoke(registry, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    "Addressables FileRegistry.GetFilePaths failed.",
                    exception.InnerException);
            }

            if (!(value is IEnumerable enumerable))
            {
                throw new InvalidOperationException(
                    "Addressables FileRegistry.GetFilePaths returned an unsupported value.");
            }

            var files = new List<string>();
            foreach (object item in enumerable)
            {
                string path = item?.ToString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AddUniquePath(files, NormalizeArtifactPath(projectRoot, path));
                }
            }

            return files;
        }

        private static string GetBuildResultOutputPath(object buildResult, string projectRoot)
        {
            Type resultType = buildResult.GetType();
            PropertyInfo outputPathProperty = ReflectionCache.GetProperty(
                resultType,
                "OutputPath",
                BindingFlags.Public | BindingFlags.Instance);
            string outputPath = outputPathProperty?.GetValue(buildResult)?.ToString();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException(
                    "Addressables build result did not provide its runtime settings OutputPath.");
            }

            return NormalizeArtifactPath(projectRoot, outputPath);
        }

        private static string GetOptionalBuildResultPath(
            object buildResult,
            string projectRoot,
            string propertyName)
        {
            PropertyInfo property = ReflectionCache.GetProperty(
                buildResult.GetType(),
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            string path = property?.GetValue(buildResult)?.ToString();
            return string.IsNullOrWhiteSpace(path)
                ? null
                : NormalizeArtifactPath(projectRoot, path);
        }

        private static string GetProfileBuildPath(object settings, Type settingsType, string variableName)
        {
            PropertyInfo profileProperty = ReflectionCache.GetProperty(
                settingsType,
                "profileSettings",
                BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo activeProfileProperty = ReflectionCache.GetProperty(
                settingsType,
                "activeProfileId",
                BindingFlags.Public | BindingFlags.Instance);
            object profileSettings = profileProperty?.GetValue(settings);
            string activeProfileId = activeProfileProperty?.GetValue(settings)?.ToString();
            if (profileSettings == null || string.IsNullOrWhiteSpace(activeProfileId))
            {
                throw new InvalidOperationException(
                    "Addressables active profile settings are unavailable.");
            }

            Type profileType = profileSettings.GetType();
            MethodInfo getValueMethod = ReflectionCache.GetMethod(
                profileType,
                "GetValueByName",
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(string), typeof(string) });
            MethodInfo evaluateMethod = ReflectionCache.GetMethod(
                profileType,
                "EvaluateString",
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(string), typeof(string) });
            if (getValueMethod == null || evaluateMethod == null)
            {
                throw new MissingMethodException(
                    profileType.FullName,
                    "GetValueByName/EvaluateString");
            }

            try
            {
                string rawValue = getValueMethod.Invoke(
                    profileSettings,
                    new object[] { activeProfileId, variableName })?.ToString();
                return string.IsNullOrWhiteSpace(rawValue)
                    ? null
                    : evaluateMethod.Invoke(
                        profileSettings,
                        new object[] { activeProfileId, rawValue })?.ToString();
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Failed to evaluate Addressables profile variable '{variableName}'.",
                    exception.InnerException);
            }
        }

        private static string GetRemoteCatalogBuildPath(object settings, Type settingsType)
        {
            return GetProfileValueReferencePath(
                settings,
                settingsType,
                "RemoteCatalogBuildPath");
        }

        private static string GetRemoteCatalogLoadPath(object settings, Type settingsType)
        {
            return GetProfileValueReferencePath(
                settings,
                settingsType,
                "RemoteCatalogLoadPath");
        }

        private static string GetProfileValueReferencePath(
            object settings,
            Type settingsType,
            string propertyName)
        {
            PropertyInfo property = ReflectionCache.GetProperty(
                settingsType,
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            object reference = property?.GetValue(settings);
            if (reference == null)
            {
                return null;
            }

            MethodInfo getValueMethod = ReflectionCache.GetMethod(
                reference.GetType(),
                "GetValue",
                BindingFlags.Public | BindingFlags.Instance,
                new[] { settingsType });
            if (getValueMethod == null)
            {
                throw new MissingMethodException(reference.GetType().FullName, "GetValue");
            }

            try
            {
                return getValueMethod.Invoke(reference, new[] { settings })?.ToString();
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Failed to evaluate Addressables {propertyName}.",
                    exception.InnerException);
            }
        }

        private static string AddRemotePublicationRoot(
            ICollection<PublicationRoot> roots,
            string projectRoot,
            string configuredPath,
            bool allowExternalSource)
        {
            if (IsUndefinedProfileValue(configuredPath))
            {
                return null;
            }

            string normalized = BuildPathPolicy.ResolvePublicationSourceRoot(
                projectRoot,
                NormalizeSourceRoot(projectRoot, configuredPath),
                allowExternalSource);
            foreach (PublicationRoot existing in roots)
            {
                if (existing.Kind == "RemoteContent"
                    && PortablePathsEqual(existing.SourcePath, normalized))
                {
                    return normalized;
                }
            }

            roots.Add(new PublicationRoot("RemoteContent", normalized));
            return normalized;
        }

        private static string ResolveContentStatePublicationRoot(
            object settings,
            Type settingsType,
            string projectRoot,
            string contentStatePath,
            string remoteCatalogSourceRoot,
            bool allowExternalSource)
        {
            var candidates = new List<string>();
            string configuredRoot = GetConfiguredContentStateBuildRoot(
                settings,
                settingsType,
                projectRoot,
                contentStatePath);
            AddContentStateRootCandidate(
                candidates,
                projectRoot,
                configuredRoot,
                allowExternalSource);
            AddContentStateRootCandidate(
                candidates,
                projectRoot,
                remoteCatalogSourceRoot,
                allowExternalSource);
            AddContentStateRootCandidate(
                candidates,
                projectRoot,
                Path.Combine(
                    projectRoot,
                    "Library",
                    "com.unity.addressables",
                    "AddressablesBinFileDownload"),
                allowExternalSource: false);

            foreach (string candidate in candidates)
            {
                if (IsPathInsideRoot(candidate, contentStatePath))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"Addressables ContentStateFilePath is outside the configured content-state, remote-catalog, and provider cache roots: '{contentStatePath}'.");
        }

        private static string GetConfiguredContentStateBuildRoot(
            object settings,
            Type settingsType,
            string projectRoot,
            string contentStatePath)
        {
            PropertyInfo property = ReflectionCache.GetProperty(
                settingsType,
                "ContentStateBuildPath",
                BindingFlags.Public | BindingFlags.Instance);
            string configured = property?.GetValue(settings)?.ToString();
            string evaluated = IsUndefinedProfileValue(configured)
                ? null
                : EvaluateProfileString(settings, settingsType, configured);
            if (!IsUndefinedProfileValue(evaluated))
            {
                return evaluated;
            }

            PropertyInfo configFolderProperty = ReflectionCache.GetProperty(
                settingsType,
                "ConfigFolder",
                BindingFlags.Public | BindingFlags.Instance);
            string configFolder = configFolderProperty?.GetValue(settings)?.ToString();
            if (string.IsNullOrWhiteSpace(configFolder))
            {
                return null;
            }

            string normalizedConfigFolder = NormalizeSourceRoot(projectRoot, configFolder);
            string contentStateDirectory = Path.GetDirectoryName(contentStatePath);
            if (string.IsNullOrEmpty(contentStateDirectory))
            {
                return null;
            }

            contentStateDirectory = Path.GetFullPath(contentStateDirectory);
            return (PathsEqual(normalizedConfigFolder, contentStateDirectory)
                    || IsPathInsideRoot(normalizedConfigFolder, contentStateDirectory))
                ? contentStateDirectory
                : null;
        }

        private static void AddContentStateRootCandidate(
            ICollection<string> candidates,
            string projectRoot,
            string configuredPath,
            bool allowExternalSource)
        {
            if (IsUndefinedProfileValue(configuredPath))
            {
                return;
            }

            string normalized;
            if (Uri.TryCreate(configuredPath, UriKind.Absolute, out Uri uri) && !uri.IsFile)
            {
                return;
            }

            normalized = NormalizeSourceRoot(projectRoot, configuredPath);
            if (!PathsEqual(normalized, projectRoot)
                && !BuildPathPolicy.IsStrictDescendant(projectRoot, normalized))
            {
                normalized = BuildPathPolicy.ResolvePublicationSourceRoot(
                    projectRoot,
                    normalized,
                    allowExternalSource);
            }

            string[] forbiddenExactRoots =
            {
                projectRoot,
                Path.Combine(projectRoot, "Assets"),
                Path.Combine(projectRoot, "Packages"),
                Path.Combine(projectRoot, "ProjectSettings"),
                Path.Combine(projectRoot, "Library"),
                Path.Combine(projectRoot, "UserSettings")
            };
            foreach (string forbiddenRoot in forbiddenExactRoots)
            {
                if (PortablePathsEqual(normalized, forbiddenRoot))
                {
                    throw new InvalidOperationException(
                        $"Addressables content-state root must be a dedicated nested directory: '{normalized}'.");
                }
            }

            foreach (string existing in candidates)
            {
                if (PortablePathsEqual(existing, normalized))
                {
                    return;
                }
            }

            candidates.Add(normalized);
        }

        private static string EvaluateProfileString(
            object settings,
            Type settingsType,
            string value)
        {
            PropertyInfo profileProperty = ReflectionCache.GetProperty(
                settingsType,
                "profileSettings",
                BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo activeProfileProperty = ReflectionCache.GetProperty(
                settingsType,
                "activeProfileId",
                BindingFlags.Public | BindingFlags.Instance);
            object profileSettings = profileProperty?.GetValue(settings);
            string activeProfileId = activeProfileProperty?.GetValue(settings)?.ToString();
            MethodInfo evaluateMethod = profileSettings == null
                ? null
                : ReflectionCache.GetMethod(
                    profileSettings.GetType(),
                    "EvaluateString",
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(string), typeof(string) });
            if (evaluateMethod == null || string.IsNullOrWhiteSpace(activeProfileId))
            {
                throw new MissingMemberException(
                    "Addressables active profile EvaluateString API is unavailable.");
            }

            try
            {
                return evaluateMethod.Invoke(
                    profileSettings,
                    new object[] { activeProfileId, value })?.ToString();
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    "Failed to evaluate an Addressables profile string.",
                    exception.InnerException);
            }
        }

        private static string GetExpectedRemoteCatalogBaseName(
            object settings,
            Type settingsType,
            string contentVersion)
        {
            string evaluated = EvaluateProfileString(
                settings,
                settingsType,
                "/catalog_" + contentVersion);
            string fileName = Path.GetFileName(
                (evaluated ?? string.Empty).TrimEnd('/', '\\'));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(
                    "Addressables did not produce a valid remote catalog base name.");
            }

            return fileName;
        }

        private static bool IsSupportedRemoteCatalogDataFile(
            string fileName,
            string expectedBaseName)
        {
            string[] extensions = { ".json", ".bin", ".bundle" };
            foreach (string extension in extensions)
            {
                if (string.Equals(
                    fileName,
                    expectedBaseName + extension,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUndefinedProfileValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), "<undefined>", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateResolvedPublicationSettings(
            AddressablesBuildConfig config,
            object settings,
            Type settingsType,
            string projectRoot,
            bool buildRemoteCatalog,
            bool copyToOutputDirectory)
        {
            bool allowExternal = config != null
                && config.allowExternalProfilePublicationSources;
            if (copyToOutputDirectory)
            {
                string profileRemoteRoot = GetProfileBuildPath(
                    settings,
                    settingsType,
                    "Remote.BuildPath");
                if (!IsUndefinedProfileValue(profileRemoteRoot))
                {
                    BuildPathPolicy.ResolvePublicationSourceRoot(
                        projectRoot,
                        NormalizeSourceRoot(projectRoot, profileRemoteRoot),
                        allowExternal);
                }
            }

            if (!buildRemoteCatalog)
            {
                return;
            }

            string remoteCatalogBuildPath = GetRemoteCatalogBuildPath(settings, settingsType);
            string remoteCatalogLoadPath = GetRemoteCatalogLoadPath(settings, settingsType);
            if (IsUndefinedProfileValue(remoteCatalogBuildPath)
                || IsUndefinedProfileValue(remoteCatalogLoadPath))
            {
                throw new InvalidOperationException(
                    "BuildRemoteCatalog requires both RemoteCatalogBuildPath and RemoteCatalogLoadPath.");
            }

            if (copyToOutputDirectory)
            {
                BuildPathPolicy.ResolvePublicationSourceRoot(
                    projectRoot,
                    NormalizeSourceRoot(projectRoot, remoteCatalogBuildPath),
                    allowExternal);
            }
        }

        private static void ValidateGeneratedOutputPathBudgets(
            object settings,
            Type settingsType,
            string projectRoot,
            BuildTarget buildTarget,
            bool buildRemoteCatalog)
        {
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                GetAddressablesBuildPath(buildTarget),
                "Addressables.BuildPath",
                1 + AddressablesGeneratedChildPathReserve);

            string profileRemoteRoot = GetProfileBuildPath(
                settings,
                settingsType,
                "Remote.BuildPath");
            if (!IsUndefinedProfileValue(profileRemoteRoot))
            {
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    NormalizeSourceRoot(projectRoot, profileRemoteRoot),
                    "Addressables Remote.BuildPath",
                    1 + AddressablesGeneratedChildPathReserve);
            }

            if (!buildRemoteCatalog)
            {
                return;
            }

            string remoteCatalogBuildPath = GetRemoteCatalogBuildPath(
                settings,
                settingsType);
            if (!IsUndefinedProfileValue(remoteCatalogBuildPath))
            {
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    NormalizeSourceRoot(projectRoot, remoteCatalogBuildPath),
                    "Addressables RemoteCatalogBuildPath",
                    1 + AddressablesGeneratedChildPathReserve);
            }
        }

        internal static string ValidatePublicationConfiguration(
            AddressablesBuildConfig config,
            string projectRoot)
        {
            if (config == null)
            {
                return "AddressablesBuildConfig is required.";
            }

            try
            {
                if (!config.copyToOutputDirectory)
                {
                    return null;
                }

                string outputDirectory = string.IsNullOrWhiteSpace(config.buildOutputDirectory)
                    ? AddressablesBuildConfig.DefaultBuildOutputDirectory
                    : config.buildOutputDirectory;
                string publicationRoot = BuildPathPolicy.ResolveBuildRoot(projectRoot, outputDirectory);
                if (config.additionalPublicationRoots == null)
                {
                    return null;
                }

                var destinationFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (AddressablesPublicationRoot root in config.additionalPublicationRoots)
                {
                    if (root == null)
                    {
                        return "Addressables additional publication roots cannot contain null entries.";
                    }

                    string error = ValidateAdditionalPublicationRoot(root, projectRoot, publicationRoot);
                    if (!string.IsNullOrEmpty(error))
                    {
                        return error;
                    }

                    if (!destinationFolders.Add(root.destinationFolder))
                    {
                        return $"Addressables publication destination folder is duplicated: '{root.destinationFolder}'.";
                    }
                }

                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        private static string ValidateAdditionalPublicationRoot(
            AddressablesPublicationRoot root,
            string projectRoot,
            string publicationRoot)
        {
            if (string.IsNullOrWhiteSpace(root.sourceDirectory))
            {
                return "Each Addressables additional publication root requires a source directory.";
            }

            if (!IsSafePublicationFolderName(root.destinationFolder))
            {
                return $"Addressables publication folder must be one safe, non-reserved path segment: '{root.destinationFolder}'.";
            }

            string sourceRoot = BuildPathPolicy.ResolveBuildRoot(projectRoot, root.sourceDirectory);
            if (PortablePathsEqual(sourceRoot, publicationRoot)
                || IsPortableStrictDescendant(sourceRoot, publicationRoot)
                || IsPortableStrictDescendant(publicationRoot, sourceRoot))
            {
                return $"Addressables publication source and destination must not overlap: source '{sourceRoot}', destination '{publicationRoot}'.";
            }

            return null;
        }

        private static bool IsSafePublicationFolderName(string value)
        {
            try
            {
                ValidatePortablePathSegment(value, "Addressables publication folder");
            }
            catch (Exception)
            {
                return false;
            }

            return !string.Equals(value, "PlayerData", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "RemoteContent", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "BuildMetadata", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "AddressablesArtifacts.json", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidatePortablePathSegment(string value, string displayName)
        {
            BuildPathPolicy.ValidatePortableFileName(
                value,
                displayName,
                maximumUtf8ByteCount: 128);

            string deviceName = value.Split('.')[0];
            string[] reservedNames =
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            foreach (string reservedName in reservedNames)
            {
                if (string.Equals(deviceName, reservedName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"{displayName} uses a reserved device name: '{value}'.");
                }
            }
        }

        private static void EnsurePublicationRootsDoNotOverlapDestination(
            IEnumerable<PublicationRoot> roots,
            string publicationRoot)
        {
            foreach (PublicationRoot root in roots)
            {
                if (PortablePathsEqual(root.SourcePath, publicationRoot)
                    || IsPortableStrictDescendant(root.SourcePath, publicationRoot)
                    || IsPortableStrictDescendant(publicationRoot, root.SourcePath))
                {
                    throw new InvalidOperationException(
                        $"Addressables source root overlaps the publication destination: source '{root.SourcePath}', destination '{publicationRoot}'.");
                }
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                PathComparison);
        }

        private static bool PortablePathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPortableStrictDescendant(string parentPath, string childPath)
        {
            string parent = Path.GetFullPath(parentPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string child = Path.GetFullPath(childPath);
            return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        private static PublicationRoot FindBestPublicationRoot(
            IEnumerable<PublicationRoot> roots,
            string filePath)
        {
            PublicationRoot best = null;
            foreach (PublicationRoot root in roots)
            {
                if (IsPathInsideRoot(root.SourcePath, filePath)
                    && (best == null || root.SourcePath.Length > best.SourcePath.Length))
                {
                    best = root;
                }
            }

            return best;
        }

        private static string NormalizeSourceRoot(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!Path.IsPathRooted(path)
                && Uri.TryCreate(path, UriKind.Absolute, out Uri uri))
            {
                if (!uri.IsFile)
                {
                    throw new InvalidOperationException(
                        $"Addressables build source must be a local filesystem path: '{path}'.");
                }

                path = uri.LocalPath;
            }

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static string NormalizeArtifactPath(string projectRoot, string path)
        {
            return NormalizeSourceRoot(projectRoot, path)
                ?? throw new InvalidOperationException("Addressables artifact path is empty.");
        }

        private static bool IsPathInsideRoot(string root, string filePath)
        {
            return !string.IsNullOrEmpty(root)
                && BuildPathPolicy.IsStrictDescendant(root, filePath);
        }

        private static string GetRelativeChildPath(string root, string filePath)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedFile = Path.GetFullPath(filePath);
            if (!BuildPathPolicy.IsStrictDescendant(normalizedRoot, normalizedFile))
            {
                throw new InvalidOperationException(
                    $"Artifact '{normalizedFile}' is outside source root '{normalizedRoot}'.");
            }

            return normalizedFile.Substring(normalizedRoot.Length + 1)
                .Replace('\\', '/');
        }

        private static void AddUniquePath(ICollection<string> paths, string path)
        {
            foreach (string existing in paths)
            {
                if (string.Equals(existing, path, PathComparison))
                {
                    return;
                }
            }

            paths.Add(path);
        }

        internal static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        private static string ToHex(byte[] hash)
        {
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                builder.Append(value.ToString("X2"));
            }

            return builder.ToString();
        }

        private static StringComparison PathComparison =>
            Environment.OSVersion.Platform == PlatformID.Unix
                || Environment.OSVersion.Platform == PlatformID.MacOSX
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;

        private static StringComparer PortableDestinationPathComparer => StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// Gets the Addressables build output path for the specified build target.
        /// Uses the provider-owned Addressables.BuildPath contract for the active target.
        /// </summary>
        internal static string GetAddressablesBuildPath(BuildTarget buildTarget)
        {
            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                throw new InvalidOperationException(
                    $"Addressables.BuildPath uses the active build target. Expected '{buildTarget}', " +
                    $"but the active target is '{EditorUserBuildSettings.activeBuildTarget}'.");
            }

            Type addressablesType = ReflectionCache.GetType("UnityEngine.AddressableAssets.Addressables");
            if (addressablesType == null)
            {
                throw new InvalidOperationException("Addressables runtime API is unavailable.");
            }

            PropertyInfo buildPathProperty = ReflectionCache.GetProperty(
                addressablesType,
                "BuildPath",
                BindingFlags.Public | BindingFlags.Static);
            string buildPath = buildPathProperty?.GetValue(null)?.ToString();
            if (string.IsNullOrWhiteSpace(buildPath))
            {
                throw new MissingMemberException(
                    "UnityEngine.AddressableAssets.Addressables.BuildPath is unavailable or empty.");
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string resolved = Path.IsPathRooted(buildPath)
                ? Path.GetFullPath(buildPath)
                : Path.GetFullPath(Path.Combine(projectRoot, buildPath));
            return resolved;
        }

        /// <summary>
        /// Writes the canonical version file into Addressables.BuildPath. The official Player processor
        /// maps this directory to StreamingAssets/aa; content publication copies the same validated file.
        /// </summary>
        private static void SaveVersionDataToAddressablesBuildPath(
            string contentVersion,
            BuildTarget buildTarget)
        {
            try
            {
                string buildPath = GetAddressablesBuildPath(buildTarget);
                if (string.IsNullOrEmpty(buildPath) || !Directory.Exists(buildPath))
                {
                    throw new DirectoryNotFoundException($"Addressables build path was not found: '{buildPath}'.");
                }

                const string versionFileName = "AddressablesVersion.json";
                string versionFilePath = BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    Path.Combine(buildPath, versionFileName),
                    "Addressables version artifact");
                string directory = Path.GetDirectoryName(versionFilePath);
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    directory,
                    "Addressables version artifact directory");
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                WriteVersionArtifactDurably(versionFilePath, contentVersion);

                if (!File.Exists(versionFilePath))
                {
                    throw new IOException($"Addressables version file was not found after writing: '{versionFilePath}'.");
                }

                Debug.Log($"{LogTag} Wrote version artifact '{versionFilePath}'.");
            }
            catch (Exception ex)
            {
                throw new IOException("Failed to save version data to the Addressables build path.", ex);
            }
        }

        private static void ClearActiveBuilderCache(
            object settings,
            Type settingsType,
            BuildTarget buildTarget)
        {
            PropertyInfo activeBuilderProperty = ReflectionCache.GetProperty(
                settingsType,
                "ActivePlayerDataBuilder",
                BindingFlags.Public | BindingFlags.Instance);
            object activeBuilder = activeBuilderProperty?.GetValue(settings);
            if (activeBuilder == null)
            {
                throw new InvalidOperationException(
                    "Addressables clean build requires a configured ActivePlayerDataBuilder.");
            }

            MethodInfo clearMethod = ReflectionCache.GetMethod(
                activeBuilder.GetType(),
                "ClearCachedData",
                BindingFlags.Public | BindingFlags.Instance);
            if (!IsUsableClearCachedData(clearMethod))
            {
                throw new InvalidOperationException(
                    $"Addressables data builder '{activeBuilder.GetType().FullName}' does not override ClearCachedData.");
            }

            try
            {
                clearMethod.Invoke(activeBuilder, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    "Addressables active data builder cache cleanup failed.",
                    exception.InnerException);
            }

            string playerDataPath = GetAddressablesBuildPath(buildTarget);
            if (Directory.Exists(playerDataPath))
            {
                using (IEnumerator<string> entries = Directory
                    .EnumerateFileSystemEntries(playerDataPath)
                    .GetEnumerator())
                {
                    if (entries.MoveNext())
                    {
                        throw new IOException(
                            $"Addressables active data builder left stale player-data cache at '{playerDataPath}'.");
                    }
                }
            }
        }

        internal static bool IsUsableClearCachedData(MethodInfo clearMethod)
        {
            return clearMethod != null
                && clearMethod.DeclaringType != null
                && !string.Equals(
                    clearMethod.DeclaringType.FullName,
                    "UnityEditor.AddressableAssets.Build.DataBuilders.BuildScriptBase",
                    StringComparison.Ordinal);
        }

        private static Exception RestoreAddressablesSettings(
            object settings,
            PropertyInfo buildRemoteCatalogProperty,
            object originalBuildRemoteCatalog,
            PropertyInfo overridePlayerVersionProperty,
            object originalOverridePlayerVersion)
        {
            var failures = new System.Collections.Generic.List<Exception>();
            TryRestoreSetting(
                settings,
                buildRemoteCatalogProperty,
                originalBuildRemoteCatalog,
                "BuildRemoteCatalog",
                failures);
            TryRestoreSetting(
                settings,
                overridePlayerVersionProperty,
                originalOverridePlayerVersion,
                "OverridePlayerVersion",
                failures);

            if (failures.Count == 0)
            {
                return null;
            }

            Exception failure = failures.Count == 1
                ? failures[0]
                : new AggregateException("Multiple Addressables settings restoration operations failed.", failures);
            return failure;
        }

        internal static Exception FinalizeSettingsTransaction(
            AddressablesSettingsTransaction transaction)
        {
            if (transaction == null)
            {
                return null;
            }

            try
            {
                transaction.RestoreAndComplete();
                return null;
            }
            catch (Exception finalizationException)
            {
                return finalizationException;
            }
        }

        private static void TryRestoreSetting(
            object settings,
            PropertyInfo property,
            object originalValue,
            string propertyName,
            System.Collections.Generic.ICollection<Exception> failures)
        {
            if (property == null)
            {
                failures.Add(new MissingMemberException(settings.GetType().FullName, propertyName));
                return;
            }

            try
            {
                property.SetValue(settings, originalValue);
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Failed to restore Addressables setting '{propertyName}'.",
                    exception));
            }
        }

        private static IDisposable EnterContentBuildScope(string projectRoot)
        {
            lock (ContentBuildGate)
            {
                if (contentBuildActive)
                {
                    throw new InvalidOperationException("An Addressables content build is already active.");
                }

                contentBuildActive = true;
            }

            try
            {
                return new ContentBuildScope(AddressablesBuildLock.Acquire(projectRoot));
            }
            catch
            {
                lock (ContentBuildGate)
                {
                    contentBuildActive = false;
                }

                throw;
            }
        }

        private static void RunInContentBuildScope(string projectRoot, Action operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            IDisposable scope = EnterContentBuildScope(projectRoot);
            Exception operationFailure = null;
            Exception disposeFailure = null;
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                operationFailure = exception;
            }

            try
            {
                scope.Dispose();
            }
            catch (Exception exception)
            {
                disposeFailure = exception;
            }

            if (operationFailure != null && disposeFailure != null)
            {
                throw new AggregateException(
                    "Addressables operation and build-lock disposal both failed.",
                    operationFailure,
                    disposeFailure);
            }

            if (operationFailure != null)
            {
                ExceptionDispatchInfo.Capture(operationFailure).Throw();
            }

            if (disposeFailure != null)
            {
                ExceptionDispatchInfo.Capture(disposeFailure).Throw();
            }
        }

        private sealed class ContentBuildScope : IDisposable
        {
            private readonly AddressablesBuildLock buildLock;
            private bool disposed;

            public ContentBuildScope(AddressablesBuildLock buildLock)
            {
                this.buildLock = buildLock ?? throw new ArgumentNullException(nameof(buildLock));
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Exception failure = null;
                try
                {
                    buildLock.Dispose();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    lock (ContentBuildGate)
                    {
                        contentBuildActive = false;
                    }
                }

                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
        }

        private sealed class PublicationRoot
        {
            public PublicationRoot(string kind, string sourcePath)
            {
                Kind = kind;
                SourcePath = sourcePath;
            }

            public string Kind { get; }
            public string SourcePath { get; }
        }

        internal sealed class AssetFileSnapshot
        {
            public AssetFileSnapshot(
                string assetPath,
                string absolutePath,
                byte[] originalBytes,
                DateTime originalLastWriteTimeUtc,
                FileAttributes originalAttributes)
            {
                AssetPath = assetPath;
                AbsolutePath = absolutePath;
                OriginalBytes = originalBytes;
                OriginalLastWriteTimeUtc = originalLastWriteTimeUtc;
                OriginalAttributes = originalAttributes;
            }

            public string AssetPath { get; }
            public string AbsolutePath { get; }
            public byte[] OriginalBytes { get; }
            public DateTime OriginalLastWriteTimeUtc { get; }
            public FileAttributes OriginalAttributes { get; }
        }

        private sealed class PublicationFile
        {
            public PublicationFile(string sourcePath, string destinationRelativePath, string kind)
            {
                SourcePath = sourcePath;
                DestinationRelativePath = destinationRelativePath;
                Kind = kind;
            }

            public string SourcePath { get; }
            public string DestinationRelativePath { get; }
            public string Kind { get; }
        }

        private sealed class PendingAddressablesPublication
        {
            private readonly AddressablesPublicationTransaction transaction;
            private readonly string destinationDirectory;
            private readonly IReadOnlyList<PublicationFile> files;
            private readonly string requiredPublishedRelativePath;
            private bool finalized;

            public PendingAddressablesPublication(
                AddressablesPublicationTransaction transaction,
                string destinationDirectory,
                IReadOnlyList<PublicationFile> files,
                string requiredPublishedRelativePath)
            {
                this.transaction = transaction
                    ?? throw new ArgumentNullException(nameof(transaction));
                this.destinationDirectory = Path.GetFullPath(destinationDirectory);
                this.files = new List<PublicationFile>(files ?? throw new ArgumentNullException(nameof(files)))
                    .AsReadOnly();
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    requiredPublishedRelativePath,
                    "Required Addressables publication path");
                this.requiredPublishedRelativePath = requiredPublishedRelativePath;
            }

            public void Commit()
            {
                FinalizeTransaction(() =>
                {
                    transaction.Commit(() =>
                    {
                        ValidatePublishedFiles(
                            destinationDirectory,
                            files,
                            Path.Combine(
                                destinationDirectory,
                                AddressablesPublicationOwnership.ArtifactManifestFileName));
                        string requiredPath = Path.GetFullPath(Path.Combine(
                            destinationDirectory,
                            requiredPublishedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                        if (!BuildPathPolicy.IsStrictDescendant(destinationDirectory, requiredPath)
                            || !File.Exists(requiredPath))
                        {
                            throw new FileNotFoundException(
                                "Required Addressables publication artifact is missing.",
                                requiredPath);
                        }
                    });
                    Debug.Log($"{LogTag} Published and verified Addressables artifacts.");
                });
            }

            public void Abort()
            {
                FinalizeTransaction(transaction.Abort);
            }

            private void FinalizeTransaction(Action operation)
            {
                if (finalized)
                {
                    throw new InvalidOperationException(
                        "Addressables publication transaction has already been finalized.");
                }

                finalized = true;
                Exception operationFailure = null;
                try
                {
                    operation();
                }
                catch (Exception exception)
                {
                    operationFailure = exception;
                }

                Exception disposeFailure = null;
                try
                {
                    transaction.Dispose();
                }
                catch (Exception exception)
                {
                    disposeFailure = exception;
                }

                if (operationFailure != null && disposeFailure != null)
                {
                    throw new AggregateException(
                        "Addressables publication finalization and transaction disposal both failed.",
                        operationFailure,
                        disposeFailure);
                }

                if (operationFailure != null)
                {
                    ExceptionDispatchInfo.Capture(operationFailure).Throw();
                }

                if (disposeFailure != null)
                {
                    throw disposeFailure;
                }
            }
        }

        [Serializable]
        private sealed class AddressablesArtifactManifest
        {
            public int schemaVersion;
            public string buildTarget;
            public string contentVersion;
            public AddressablesArtifactManifestEntry[] files;
        }

        [Serializable]
        private sealed class AddressablesArtifactManifestEntry
        {
            public string kind;
            public string path;
            public long size;
            public string sha256;
        }

        [Serializable]
        private class VersionDataJson
        {
            public string contentVersion = string.Empty;
        }
    }
}
