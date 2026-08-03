using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Validates the canonical Addressables version artifact while the composable pipeline owns a Player build.
    /// </summary>
    public sealed class AddressablesVersionBuildProcessor : BuildPlayerProcessor
    {
        private const string VersionFileName = "AddressablesVersion.json";

        private static readonly object SessionGate = new object();
        private static BuildSession activeSession;

        public override int callbackOrder => 2;

        internal static string ValidateSupport(bool cleanBuild = false)
        {
            try
            {
                Type settingsType = ReflectionCache.GetType(
                    "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                if (settingsType == null)
                {
                    return "Addressables Editor settings API is unavailable.";
                }

                object settings = AddressablesBuilder.GetDefaultSettings();
                if (settings == null)
                {
                    return "AddressableAssetSettings was not found.";
                }

                System.Collections.Generic.IReadOnlyList<string> dirtyAssets =
                    AddressablesBuilder.GetDirtyConfigurationAssetPaths(
                        settings,
                        settingsType,
                        includeSettingsAsset: true);
                if (dirtyAssets.Count > 0)
                {
                    return "Addressables configuration has unsaved changes: " +
                        string.Join(", ", dirtyAssets);
                }

                foreach (string propertyName in new[] { "BuildRemoteCatalog", "OverridePlayerVersion" })
                {
                    PropertyInfo requiredProperty = ReflectionCache.GetProperty(
                        settingsType,
                        propertyName,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (requiredProperty == null
                        || !requiredProperty.CanRead
                        || !requiredProperty.CanWrite)
                    {
                        return $"Addressables {propertyName} API is unavailable.";
                    }
                }

                bool buildMethodFound = false;
                foreach (MethodInfo method in settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == "BuildPlayerContent")
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].IsOut)
                        {
                            Type resultType = parameters[0].ParameterType.GetElementType();
                            PropertyInfo errorProperty = resultType == null
                                ? null
                                : ReflectionCache.GetProperty(
                                    resultType,
                                    "Error",
                                    BindingFlags.Public | BindingFlags.Instance);
                            if (errorProperty != null && errorProperty.CanRead)
                            {
                                buildMethodFound = true;
                                break;
                            }
                        }
                    }
                }

                if (!buildMethodFound)
                {
                    return "Addressables BuildPlayerContent API is unavailable or unsupported.";
                }

                if (cleanBuild)
                {
                    PropertyInfo activeBuilderProperty = ReflectionCache.GetProperty(
                        settingsType,
                        "ActivePlayerDataBuilder",
                        BindingFlags.Public | BindingFlags.Instance);
                    object activeBuilder = activeBuilderProperty?.GetValue(settings);
                    MethodInfo clearMethod = activeBuilder == null
                        ? null
                        : ReflectionCache.GetMethod(
                            activeBuilder.GetType(),
                            "ClearCachedData",
                            BindingFlags.Public | BindingFlags.Instance);
                    if (!AddressablesBuilder.IsUsableClearCachedData(clearMethod))
                    {
                        return "Addressables clean build requires an active data builder that overrides ClearCachedData.";
                    }
                }

                PropertyInfo property = ReflectionCache.GetProperty(
                    settingsType,
                    "BuildAddressablesWithPlayerBuild",
                    BindingFlags.Public | BindingFlags.Instance);
                if (property == null
                    || !property.CanRead
                    || !property.CanWrite
                    || !property.PropertyType.IsEnum)
                {
                    return "Addressables BuildAddressablesWithPlayerBuild API is unavailable.";
                }

                Enum.Parse(property.PropertyType, "DoNotBuildWithPlayer", ignoreCase: false);
                Type addressablesType = ReflectionCache.GetType(
                    "UnityEngine.AddressableAssets.Addressables");
                PropertyInfo buildPathProperty = ReflectionCache.GetProperty(
                    addressablesType,
                    "BuildPath",
                    BindingFlags.Public | BindingFlags.Static);
                return buildPathProperty == null
                    ? "Addressables.BuildPath API is unavailable."
                    : null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        internal static IDisposable BeginSession(BuildTarget target, string contentVersion)
        {
            if (target == BuildTarget.NoTarget)
            {
                throw new ArgumentOutOfRangeException(nameof(target), target, "A valid build target is required.");
            }

            if (string.IsNullOrWhiteSpace(contentVersion))
            {
                throw new ArgumentException("Addressables content version is required.", nameof(contentVersion));
            }

            BuildSession session;
            lock (SessionGate)
            {
                if (activeSession != null)
                {
                    throw new InvalidOperationException("An Addressables Player build session is already active.");
                }

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                AddressablesBuildLock buildLock = null;
                AddressablesSettingsTransaction settingsTransaction = null;
                try
                {
                    buildLock = AddressablesBuildLock.Acquire(projectRoot);
                    AddressablesSettingsTransaction.RecoverPending(projectRoot);
                    AddressablesPublicationTransaction.RecoverPending(projectRoot);

                    Type settingsType = ReflectionCache.GetType(
                        "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                    if (settingsType == null)
                    {
                        throw new InvalidOperationException(
                            "Addressables is selected, but its Editor settings API is unavailable.");
                    }

                    object settings = AddressablesBuilder.GetDefaultSettings();
                    if (settings == null)
                    {
                        throw new InvalidOperationException(
                            "AddressableAssetSettings was not found before the Player build.");
                    }

                    IReadOnlyList<string> dirtyAssets =
                        AddressablesBuilder.GetDirtyConfigurationAssetPaths(
                            settings,
                            settingsType,
                            includeSettingsAsset: true);
                    if (dirtyAssets.Count > 0)
                    {
                        throw new InvalidOperationException(
                            "Addressables configuration has unsaved changes before the Player build: "
                            + string.Join(", ", dirtyAssets));
                    }

                    PropertyInfo buildWithPlayerProperty = ReflectionCache.GetProperty(
                        settingsType,
                        "BuildAddressablesWithPlayerBuild",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (buildWithPlayerProperty == null
                        || !buildWithPlayerProperty.CanRead
                        || !buildWithPlayerProperty.CanWrite
                        || !buildWithPlayerProperty.PropertyType.IsEnum)
                    {
                        throw new MissingMemberException(
                            settingsType.FullName,
                            "BuildAddressablesWithPlayerBuild");
                    }

                    object disabledValue;
                    try
                    {
                        disabledValue = Enum.Parse(
                            buildWithPlayerProperty.PropertyType,
                            "DoNotBuildWithPlayer",
                            ignoreCase: false);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            "Addressables does not expose the required DoNotBuildWithPlayer option.",
                            exception);
                    }

                    object originalValue = buildWithPlayerProperty.GetValue(settings);
                    IReadOnlyList<AddressablesBuilder.AssetFileSnapshot> configurationSnapshots =
                        AddressablesBuilder.CaptureConfigurationAssetSnapshots(settings, settingsType);
                    settingsTransaction = AddressablesSettingsTransaction.Begin(
                        projectRoot,
                        configurationSnapshots);
                    try
                    {
                        buildWithPlayerProperty.SetValue(settings, disabledValue);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            "Failed to disable Addressables content rebuilding for the Player build.",
                            exception);
                    }

                    session = new BuildSession(
                        target,
                        contentVersion,
                        settings,
                        buildWithPlayerProperty,
                        originalValue,
                        settingsTransaction,
                        buildLock);
                    activeSession = session;
                    settingsTransaction = null;
                    buildLock = null;
                }
                catch (Exception operationException)
                {
                    var cleanupFailures = new List<Exception>();
                    if (settingsTransaction != null)
                    {
                        try
                        {
                            settingsTransaction.Dispose();
                        }
                        catch (Exception exception)
                        {
                            cleanupFailures.Add(new InvalidOperationException(
                                "Failed to recover Addressables settings after Player session startup failed.",
                                exception));
                        }
                    }

                    if (buildLock != null)
                    {
                        try
                        {
                            buildLock.Dispose();
                        }
                        catch (Exception exception)
                        {
                            cleanupFailures.Add(new InvalidOperationException(
                                "Failed to release the Addressables build lock after Player session startup failed.",
                                exception));
                        }
                    }

                    if (cleanupFailures.Count == 0)
                    {
                        throw;
                    }

                    cleanupFailures.Insert(0, operationException);
                    throw new AggregateException(
                        "Addressables Player build session startup and cleanup failed.",
                        cleanupFailures);
                }
            }

            return new BuildSessionScope(session);
        }

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            BuildSession session;
            lock (SessionGate)
            {
                session = activeSession;
            }

            if (session == null)
            {
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != session.Target)
            {
                throw new BuildFailedException(
                    $"Addressables version session target '{session.Target}' does not match active target '{EditorUserBuildSettings.activeBuildTarget}'.");
            }

            Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
            if (settingsType == null)
            {
                throw new BuildFailedException("Addressables is selected, but its Editor settings API is unavailable.");
            }

            object settings = AddressablesBuilder.GetDefaultSettings();
            if (settings == null)
            {
                throw new BuildFailedException("AddressableAssetSettings was not found during Player build preparation.");
            }

            string buildDirectory = AddressablesBuilder.GetAddressablesBuildPath(session.Target);
            if (string.IsNullOrWhiteSpace(buildDirectory) || !Directory.Exists(buildDirectory))
            {
                throw new BuildFailedException($"Addressables build output was not found: '{buildDirectory}'.");
            }

            string versionFilePath = Path.Combine(buildDirectory, VersionFileName);
            if (!File.Exists(versionFilePath))
            {
                throw new BuildFailedException($"Addressables version artifact was not found: '{versionFilePath}'.");
            }

            try
            {
                AddressablesBuilder.ReadAndValidateVersionArtifact(
                    versionFilePath,
                    session.ContentVersion);
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"Addressables version artifact is unreadable: '{versionFilePath}'. {exception.Message}");
            }

            Debug.Log(
                $"[AddressablesVersionBuildProcessor] Validated version '{session.ContentVersion}' in the provider-owned player data.");
        }

        private sealed class BuildSession
        {
            public BuildSession(
                BuildTarget target,
                string contentVersion,
                object settings,
                PropertyInfo buildWithPlayerProperty,
                object originalBuildWithPlayerValue,
                AddressablesSettingsTransaction settingsTransaction,
                AddressablesBuildLock buildLock)
            {
                Target = target;
                ContentVersion = contentVersion;
                Settings = settings;
                BuildWithPlayerProperty = buildWithPlayerProperty;
                OriginalBuildWithPlayerValue = originalBuildWithPlayerValue;
                SettingsTransaction = settingsTransaction
                    ?? throw new ArgumentNullException(nameof(settingsTransaction));
                BuildLock = buildLock ?? throw new ArgumentNullException(nameof(buildLock));
            }

            public BuildTarget Target { get; }
            public string ContentVersion { get; }
            public object Settings { get; }
            public PropertyInfo BuildWithPlayerProperty { get; }
            public object OriginalBuildWithPlayerValue { get; }
            public AddressablesSettingsTransaction SettingsTransaction { get; }
            public AddressablesBuildLock BuildLock { get; }
        }

        private sealed class BuildSessionScope : IDisposable
        {
            private readonly BuildSession session;
            private bool disposed;

            public BuildSessionScope(BuildSession session)
            {
                this.session = session;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                bool ownsSession = false;
                lock (SessionGate)
                {
                    if (ReferenceEquals(activeSession, session))
                    {
                        ownsSession = true;
                        activeSession = null;
                    }
                }

                if (!ownsSession)
                {
                    return;
                }

                var failures = new List<Exception>();
                try
                {
                    session.BuildWithPlayerProperty.SetValue(
                        session.Settings,
                        session.OriginalBuildWithPlayerValue);
                }
                catch (Exception exception)
                {
                    failures.Add(new InvalidOperationException(
                        "Failed to restore Addressables Build With Player setting.",
                        exception));
                }

                Exception settingsTransactionFailure =
                    AddressablesBuilder.FinalizeSettingsTransaction(
                        session.SettingsTransaction);
                if (settingsTransactionFailure != null)
                {
                    failures.Add(new InvalidOperationException(
                        "Failed to finalize the durable Addressables settings transaction.",
                        settingsTransactionFailure));
                }

                try
                {
                    session.BuildLock.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(new InvalidOperationException(
                        "Failed to release the Addressables build lock.",
                        exception));
                }

                if (failures.Count == 1)
                {
                    throw failures[0];
                }

                if (failures.Count > 1)
                {
                    throw new AggregateException(
                        "Multiple Addressables Player build session settings failed to restore.",
                        failures);
                }
            }
        }

    }
}
