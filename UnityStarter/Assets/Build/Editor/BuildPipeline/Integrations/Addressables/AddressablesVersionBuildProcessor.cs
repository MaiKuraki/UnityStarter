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

        internal static string ValidateSupport(BuildIncrementality incrementality)
        {
            try
            {
                if (incrementality != BuildIncrementality.Clean
                    && incrementality != BuildIncrementality.Incremental)
                {
                    return $"Unsupported Addressables incrementality mode '{incrementality}'.";
                }

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

                if (incrementality == BuildIncrementality.Clean)
                {
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
                else
                {
                    Type contentUpdateType = ReflectionCache.GetType(
                        "UnityEditor.AddressableAssets.Build.ContentUpdateScript");
                    if (FindContentUpdateBuildMethod(contentUpdateType, settingsType) == null)
                    {
                        return "Addressables ContentUpdateScript.BuildContentUpdate(AddressableAssetSettings, string) API is unavailable or unsupported.";
                    }

                    MethodInfo loadMethod = FindContentStateLoadMethod(contentUpdateType);
                    if (loadMethod == null)
                    {
                        return "Addressables ContentUpdateScript.LoadContentState(string) API is unavailable or unsupported.";
                    }

                    Type stateType = loadMethod.ReturnType;
                    foreach (string fieldName in new[]
                             {
                                 "playerVersion",
                                 "editorVersion",
                                 "remoteCatalogLoadPath"
                             })
                    {
                        FieldInfo field = ReflectionCache.GetField(
                            stateType,
                            fieldName,
                            BindingFlags.Public | BindingFlags.Instance);
                        if (field == null || field.FieldType != typeof(string))
                        {
                            return $"Addressables content state field '{fieldName}' is unavailable or unsupported.";
                        }
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
                if (buildPathProperty == null)
                {
                    return "Addressables.BuildPath API is unavailable.";
                }

                return AddressablesPlayerBuildIsolation.ValidateContentSessionSupport();
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        internal static MethodInfo FindContentUpdateBuildMethod(
            Type contentUpdateType,
            Type settingsType)
        {
            if (contentUpdateType == null || settingsType == null)
            {
                return null;
            }

            MethodInfo method = ReflectionCache.GetMethod(
                contentUpdateType,
                "BuildContentUpdate",
                BindingFlags.Public | BindingFlags.Static,
                new[] { settingsType, typeof(string) });
            if (method == null || method.ReturnType == typeof(void))
            {
                return null;
            }

            PropertyInfo errorProperty = ReflectionCache.GetProperty(
                method.ReturnType,
                "Error",
                BindingFlags.Public | BindingFlags.Instance);
            return errorProperty != null && errorProperty.CanRead
                ? method
                : null;
        }

        internal static MethodInfo FindContentStateLoadMethod(Type contentUpdateType)
        {
            if (contentUpdateType == null)
            {
                return null;
            }

            MethodInfo method = ReflectionCache.GetMethod(
                contentUpdateType,
                "LoadContentState",
                BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(string) });
            return method != null && method.ReturnType != typeof(void)
                ? method
                : null;
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
                IDisposable isolationScope = null;
                try
                {
                    isolationScope =
                        AddressablesPlayerBuildIsolation.BeginContentSession(projectRoot);
                    session = new BuildSession(
                        projectRoot,
                        target,
                        contentVersion,
                        isolationScope);
                    activeSession = session;
                    isolationScope = null;
                }
                catch (Exception operationException)
                {
                    if (isolationScope == null)
                    {
                        throw;
                    }

                    try
                    {
                        isolationScope.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException(
                            "Addressables version session startup and isolation cleanup failed.",
                            operationException,
                            cleanupException);
                    }

                    throw;
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
                    session.ProjectRoot,
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
                string projectRoot,
                BuildTarget target,
                string contentVersion,
                IDisposable isolationScope)
            {
                ProjectRoot = Path.GetFullPath(
                    projectRoot
                    ?? throw new ArgumentNullException(nameof(projectRoot)));
                Target = target;
                ContentVersion = contentVersion;
                IsolationScope = isolationScope
                    ?? throw new ArgumentNullException(nameof(isolationScope));
            }

            public string ProjectRoot { get; }
            public BuildTarget Target { get; }
            public string ContentVersion { get; }
            public IDisposable IsolationScope { get; }
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

                try
                {
                    session.IsolationScope.Dispose();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Failed to restore Addressables Player isolation state.",
                        exception);
                }
            }
        }

    }
}
