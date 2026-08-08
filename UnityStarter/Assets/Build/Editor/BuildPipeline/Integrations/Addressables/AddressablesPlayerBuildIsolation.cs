using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Prevents the Addressables package Player processor from performing work
    /// that was not selected by the current build recipe.
    /// </summary>
    internal static class AddressablesPlayerBuildIsolation
    {
        private const string SettingsTypeName =
            "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings";
        private const string DefaultSettingsTypeName =
            "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject";
        private const string PlayerProcessorTypeName = "AddressablesPlayerBuildProcessor";
        private const string BuildWithPlayerPropertyName = "BuildAddressablesWithPlayerBuild";
        private const string StreamingAssetFilterPropertyName = "AddPathToStreamingAssets";
        private const string DisabledBuildWithPlayerValueName = "DoNotBuildWithPlayer";

        private static readonly object SessionGate = new object();
        private static readonly Func<string, bool> RejectStreamingAssetPath = _ => false;
        private static IsolationSession activeSession;

        internal static bool IsPackageInstalled()
        {
            return ReflectionCache.GetType(SettingsTypeName) != null
                || ReflectionCache.GetType(DefaultSettingsTypeName) != null
                || ReflectionCache.GetType(PlayerProcessorTypeName) != null;
        }

        internal static string ValidateSuppressionSupport()
        {
            try
            {
                PackageApi api = ResolvePackageApi(
                    requireSettingsAsset: false,
                    requireStreamingAssetSuppression: true);
                if (!api.IsInstalled)
                {
                    return null;
                }

                ValidateSettingsAreSaved(api);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        internal static string ValidateContentSessionSupport()
        {
            try
            {
                PackageApi api = ResolvePackageApi(
                    requireSettingsAsset: true,
                    requireStreamingAssetSuppression: false);
                if (!api.IsInstalled)
                {
                    return "Addressables Editor Player-build APIs are unavailable.";
                }

                ValidateSettingsAreSaved(api);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        internal static IDisposable BeginSuppressed(string projectRoot)
        {
            return Begin(
                projectRoot,
                requireSettingsAsset: false,
                suppressStreamingAssets: true);
        }

        internal static IDisposable BeginContentSession(string projectRoot)
        {
            return Begin(
                projectRoot,
                requireSettingsAsset: true,
                suppressStreamingAssets: false);
        }

        private static IDisposable Begin(
            string projectRoot,
            bool requireSettingsAsset,
            bool suppressStreamingAssets)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A project root is required.", nameof(projectRoot));
            }

            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            lock (SessionGate)
            {
                if (activeSession != null)
                {
                    throw new InvalidOperationException(
                        "An Addressables Player isolation session is already active.");
                }

                PackageApi api = ResolvePackageApi(
                    requireSettingsAsset,
                    suppressStreamingAssets);
                if (!api.IsInstalled)
                {
                    return NoOpScope.Instance;
                }

                ValidateSettingsAreSaved(api);

                AddressablesBuildLock buildLock = null;
                AddressablesSettingsTransaction settingsTransaction = null;
                try
                {
                    buildLock = AddressablesBuildLock.Acquire(normalizedProjectRoot);
                    AddressablesSettingsTransaction.EnsureNoPendingRecovery(
                        normalizedProjectRoot);

                    if (api.Settings != null)
                    {
                        IReadOnlyList<AddressablesBuilder.AssetFileSnapshot> snapshots =
                            AddressablesBuilder.CaptureConfigurationAssetSnapshots(
                                api.Settings,
                                api.SettingsType);
                        settingsTransaction = AddressablesSettingsTransaction.Begin(
                            normalizedProjectRoot,
                            snapshots);
                    }

                    AddressablesSettingsTransaction ownedSettingsTransaction =
                        settingsTransaction;
                    AddressablesBuildLock ownedBuildLock = buildLock;
                    settingsTransaction = null;
                    buildLock = null;
                    IsolationSession session = InstallSession(
                        api.Settings,
                        api.BuildWithPlayerProperty,
                        api.DisabledBuildWithPlayerValue,
                        suppressStreamingAssets
                            ? api.StreamingAssetFilterProperty
                            : null,
                        suppressStreamingAssets
                            ? (object)RejectStreamingAssetPath
                            : null,
                        () => AddressablesBuilder.FinalizeSettingsTransaction(
                            ownedSettingsTransaction),
                        ownedBuildLock);
                    return new IsolationSessionScope(session);
                }
                catch (Exception operationException)
                {
                    Exception cleanupFailure = CleanupUnownedResources(
                        settingsTransaction,
                        buildLock);
                    if (cleanupFailure == null)
                    {
                        throw;
                    }

                    throw new AggregateException(
                        "Addressables Player isolation startup and cleanup failed.",
                        operationException,
                        cleanupFailure);
                }
            }
        }

        internal static IDisposable BeginForTesting(
            object settings,
            PropertyInfo buildWithPlayerProperty,
            object disabledBuildWithPlayerValue,
            PropertyInfo streamingAssetFilterProperty,
            object suppressedStreamingAssetFilter,
            Func<Exception> finalizeSettingsTransaction,
            IDisposable buildLock)
        {
            lock (SessionGate)
            {
                if (activeSession != null)
                {
                    throw new InvalidOperationException(
                        "An Addressables Player isolation session is already active.");
                }

                IsolationSession session = InstallSession(
                    settings,
                    buildWithPlayerProperty,
                    disabledBuildWithPlayerValue,
                    streamingAssetFilterProperty,
                    suppressedStreamingAssetFilter,
                    finalizeSettingsTransaction,
                    buildLock);
                return new IsolationSessionScope(session);
            }
        }

        private static IsolationSession InstallSession(
            object settings,
            PropertyInfo buildWithPlayerProperty,
            object disabledBuildWithPlayerValue,
            PropertyInfo streamingAssetFilterProperty,
            object suppressedStreamingAssetFilter,
            Func<Exception> finalizeSettingsTransaction,
            IDisposable buildLock)
        {
            if ((settings == null) != (buildWithPlayerProperty == null))
            {
                throw new ArgumentException(
                    "Addressables settings and its Build With Player property must be supplied together.");
            }

            if ((streamingAssetFilterProperty == null)
                != (suppressedStreamingAssetFilter == null))
            {
                throw new ArgumentException(
                    "Addressables streaming filter property and suppression delegate must be supplied together.");
            }

            if (finalizeSettingsTransaction == null)
            {
                throw new ArgumentNullException(nameof(finalizeSettingsTransaction));
            }

            if (buildLock == null)
            {
                throw new ArgumentNullException(nameof(buildLock));
            }

            object originalBuildWithPlayerValue = null;
            object originalStreamingAssetFilter = null;
            bool buildWithPlayerChanged = false;
            bool streamingAssetFilterChanged = false;
            try
            {
                if (settings != null)
                {
                    originalBuildWithPlayerValue = buildWithPlayerProperty.GetValue(settings);
                    buildWithPlayerProperty.SetValue(
                        settings,
                        disabledBuildWithPlayerValue);
                    buildWithPlayerChanged = true;
                }

                if (streamingAssetFilterProperty != null)
                {
                    originalStreamingAssetFilter =
                        streamingAssetFilterProperty.GetValue(null);
                    streamingAssetFilterProperty.SetValue(
                        null,
                        suppressedStreamingAssetFilter);
                    streamingAssetFilterChanged = true;
                }

                var session = new IsolationSession(
                    settings,
                    buildWithPlayerProperty,
                    originalBuildWithPlayerValue,
                    disabledBuildWithPlayerValue,
                    streamingAssetFilterProperty,
                    originalStreamingAssetFilter,
                    suppressedStreamingAssetFilter,
                    finalizeSettingsTransaction,
                    buildLock);
                activeSession = session;
                return session;
            }
            catch (Exception operationException)
            {
                var failures = new List<Exception> { operationException };
                RestoreMutatedState(
                    settings,
                    buildWithPlayerProperty,
                    originalBuildWithPlayerValue,
                    disabledBuildWithPlayerValue,
                    buildWithPlayerChanged,
                    streamingAssetFilterProperty,
                    originalStreamingAssetFilter,
                    suppressedStreamingAssetFilter,
                    streamingAssetFilterChanged,
                    failures);
                AddFailure(finalizeSettingsTransaction(), failures);
                TryDispose(buildLock, "Addressables build lock", failures);
                throw CreateFailure(
                    "Addressables Player isolation could not be installed.",
                    failures);
            }
        }

        private static PackageApi ResolvePackageApi(
            bool requireSettingsAsset,
            bool requireStreamingAssetSuppression)
        {
            Type settingsType = ReflectionCache.GetType(SettingsTypeName);
            Type defaultSettingsType = ReflectionCache.GetType(DefaultSettingsTypeName);
            Type playerProcessorType = ReflectionCache.GetType(PlayerProcessorTypeName);
            bool anyApiPresent = settingsType != null
                || defaultSettingsType != null
                || playerProcessorType != null;
            if (!anyApiPresent)
            {
                return PackageApi.NotInstalled;
            }

            if (settingsType == null
                || defaultSettingsType == null
                || playerProcessorType == null)
            {
                throw new InvalidOperationException(
                    "Addressables Player-build APIs are partially available. " +
                    "Install a supported Addressables package version or remove the incomplete package.");
            }

            PropertyInfo streamingAssetFilterProperty = ReflectionCache.GetProperty(
                playerProcessorType,
                StreamingAssetFilterPropertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (requireStreamingAssetSuppression)
            {
                ValidateStaticDelegateProperty(
                    streamingAssetFilterProperty,
                    playerProcessorType,
                    StreamingAssetFilterPropertyName);
            }

            object settings = AddressablesBuilder.GetDefaultSettings();
            if (settings == null)
            {
                if (requireSettingsAsset)
                {
                    throw new InvalidOperationException(
                        "AddressableAssetSettings was not found before the Player build.");
                }

                return new PackageApi(
                    settingsType,
                    settings: null,
                    buildWithPlayerProperty: null,
                    disabledBuildWithPlayerValue: null,
                    streamingAssetFilterProperty);
            }

            PropertyInfo buildWithPlayerProperty = ReflectionCache.GetProperty(
                settingsType,
                BuildWithPlayerPropertyName,
                BindingFlags.Public | BindingFlags.Instance);
            if (buildWithPlayerProperty == null
                || !buildWithPlayerProperty.CanRead
                || !buildWithPlayerProperty.CanWrite
                || !buildWithPlayerProperty.PropertyType.IsEnum)
            {
                throw new MissingMemberException(
                    settingsType.FullName,
                    BuildWithPlayerPropertyName);
            }

            object disabledBuildWithPlayerValue;
            try
            {
                disabledBuildWithPlayerValue = Enum.Parse(
                    buildWithPlayerProperty.PropertyType,
                    DisabledBuildWithPlayerValueName,
                    ignoreCase: false);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Addressables does not expose the required DoNotBuildWithPlayer option.",
                    exception);
            }

            return new PackageApi(
                settingsType,
                settings,
                buildWithPlayerProperty,
                disabledBuildWithPlayerValue,
                streamingAssetFilterProperty);
        }

        private static void ValidateStaticDelegateProperty(
            PropertyInfo property,
            Type declaringType,
            string propertyName)
        {
            if (property == null
                || !property.CanRead
                || !property.CanWrite
                || property.PropertyType != typeof(Func<string, bool>)
                || property.GetGetMethod(nonPublic: true) == null
                || !property.GetGetMethod(nonPublic: true).IsStatic
                || property.GetSetMethod(nonPublic: true) == null
                || !property.GetSetMethod(nonPublic: true).IsStatic)
            {
                throw new MissingMemberException(
                    declaringType.FullName,
                    propertyName);
            }
        }

        private static void ValidateSettingsAreSaved(PackageApi api)
        {
            if (!api.IsInstalled || api.Settings == null)
            {
                return;
            }

            IReadOnlyList<string> dirtyAssets =
                AddressablesBuilder.GetDirtyConfigurationAssetPaths(
                    api.Settings,
                    api.SettingsType,
                    includeSettingsAsset: true);
            if (dirtyAssets.Count > 0)
            {
                throw new InvalidOperationException(
                    "Addressables configuration has unsaved changes before the Player build: " +
                    string.Join(", ", dirtyAssets));
            }
        }

        private static Exception CleanupUnownedResources(
            AddressablesSettingsTransaction settingsTransaction,
            AddressablesBuildLock buildLock)
        {
            var failures = new List<Exception>();
            if (settingsTransaction != null)
            {
                AddFailure(
                    AddressablesBuilder.FinalizeSettingsTransaction(
                        settingsTransaction),
                    failures);
            }

            TryDispose(buildLock, "Addressables build lock", failures);
            return failures.Count == 0
                ? null
                : CreateFailure(
                    "Addressables Player isolation cleanup failed.",
                    failures);
        }

        private static void RestoreMutatedState(
            object settings,
            PropertyInfo buildWithPlayerProperty,
            object originalBuildWithPlayerValue,
            object disabledBuildWithPlayerValue,
            bool buildWithPlayerChanged,
            PropertyInfo streamingAssetFilterProperty,
            object originalStreamingAssetFilter,
            object suppressedStreamingAssetFilter,
            bool streamingAssetFilterChanged,
            ICollection<Exception> failures)
        {
            if (streamingAssetFilterChanged)
            {
                try
                {
                    object current = streamingAssetFilterProperty.GetValue(null);
                    if (!Equals(current, suppressedStreamingAssetFilter))
                    {
                        throw new InvalidOperationException(
                            "Addressables streaming-asset filter changed while the build pipeline owned it. " +
                            "The foreign value was preserved.");
                    }

                    streamingAssetFilterProperty.SetValue(
                        null,
                        originalStreamingAssetFilter);
                }
                catch (Exception exception)
                {
                    failures.Add(new InvalidOperationException(
                        "Failed to restore the Addressables streaming-asset filter.",
                        exception));
                }
            }

            if (buildWithPlayerChanged)
            {
                try
                {
                    object current = buildWithPlayerProperty.GetValue(settings);
                    if (!Equals(current, disabledBuildWithPlayerValue))
                    {
                        failures.Add(new InvalidOperationException(
                            "Addressables Build With Player state changed while the build pipeline owned it."));
                    }

                    buildWithPlayerProperty.SetValue(
                        settings,
                        originalBuildWithPlayerValue);
                }
                catch (Exception exception)
                {
                    failures.Add(new InvalidOperationException(
                        "Failed to restore Addressables Build With Player state.",
                        exception));
                }
            }
        }

        private static void AddFailure(
            Exception exception,
            ICollection<Exception> failures)
        {
            if (exception != null)
            {
                failures.Add(exception);
            }
        }

        private static void TryDispose(
            IDisposable disposable,
            string name,
            ICollection<Exception> failures)
        {
            if (disposable == null)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Failed to dispose {name}.",
                    exception));
            }
        }

        private static Exception CreateFailure(
            string message,
            IReadOnlyList<Exception> failures)
        {
            if (failures.Count == 1)
            {
                return failures[0];
            }

            return new AggregateException(message, failures);
        }

        private sealed class IsolationSession
        {
            public IsolationSession(
                object settings,
                PropertyInfo buildWithPlayerProperty,
                object originalBuildWithPlayerValue,
                object disabledBuildWithPlayerValue,
                PropertyInfo streamingAssetFilterProperty,
                object originalStreamingAssetFilter,
                object suppressedStreamingAssetFilter,
                Func<Exception> finalizeSettingsTransaction,
                IDisposable buildLock)
            {
                Settings = settings;
                BuildWithPlayerProperty = buildWithPlayerProperty;
                OriginalBuildWithPlayerValue = originalBuildWithPlayerValue;
                DisabledBuildWithPlayerValue = disabledBuildWithPlayerValue;
                StreamingAssetFilterProperty = streamingAssetFilterProperty;
                OriginalStreamingAssetFilter = originalStreamingAssetFilter;
                SuppressedStreamingAssetFilter = suppressedStreamingAssetFilter;
                FinalizeSettingsTransaction = finalizeSettingsTransaction;
                BuildLock = buildLock;
            }

            public object Settings { get; }
            public PropertyInfo BuildWithPlayerProperty { get; }
            public object OriginalBuildWithPlayerValue { get; }
            public object DisabledBuildWithPlayerValue { get; }
            public PropertyInfo StreamingAssetFilterProperty { get; }
            public object OriginalStreamingAssetFilter { get; }
            public object SuppressedStreamingAssetFilter { get; }
            public Func<Exception> FinalizeSettingsTransaction { get; }
            public IDisposable BuildLock { get; }
        }

        private sealed class IsolationSessionScope : IDisposable
        {
            private readonly IsolationSession session;
            private bool disposed;

            public IsolationSessionScope(IsolationSession session)
            {
                this.session = session
                    ?? throw new ArgumentNullException(nameof(session));
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                var failures = new List<Exception>();
                lock (SessionGate)
                {
                    if (!ReferenceEquals(activeSession, session))
                    {
                        return;
                    }

                    try
                    {
                        RestoreMutatedState(
                            session.Settings,
                            session.BuildWithPlayerProperty,
                            session.OriginalBuildWithPlayerValue,
                            session.DisabledBuildWithPlayerValue,
                            session.Settings != null,
                            session.StreamingAssetFilterProperty,
                            session.OriginalStreamingAssetFilter,
                            session.SuppressedStreamingAssetFilter,
                            session.StreamingAssetFilterProperty != null,
                            failures);
                        AddFailure(
                            session.FinalizeSettingsTransaction(),
                            failures);
                        TryDispose(
                            session.BuildLock,
                            "Addressables build lock",
                            failures);
                    }
                    finally
                    {
                        activeSession = null;
                    }
                }

                if (failures.Count > 0)
                {
                    throw CreateFailure(
                        "Addressables Player isolation did not restore cleanly.",
                        failures);
                }
            }
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new NoOpScope();

            public void Dispose()
            {
            }
        }

        private sealed class PackageApi
        {
            public static readonly PackageApi NotInstalled = new PackageApi();

            private PackageApi()
            {
                IsInstalled = false;
            }

            public PackageApi(
                Type settingsType,
                object settings,
                PropertyInfo buildWithPlayerProperty,
                object disabledBuildWithPlayerValue,
                PropertyInfo streamingAssetFilterProperty)
            {
                IsInstalled = true;
                SettingsType = settingsType;
                Settings = settings;
                BuildWithPlayerProperty = buildWithPlayerProperty;
                DisabledBuildWithPlayerValue = disabledBuildWithPlayerValue;
                StreamingAssetFilterProperty = streamingAssetFilterProperty;
            }

            public bool IsInstalled { get; }
            public Type SettingsType { get; }
            public object Settings { get; }
            public PropertyInfo BuildWithPlayerProperty { get; }
            public object DisabledBuildWithPlayerValue { get; }
            public PropertyInfo StreamingAssetFilterProperty { get; }
        }
    }
}
