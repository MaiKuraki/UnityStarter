#if VCONTAINER_PRESENT
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;
using CycloneGames.AssetManagement.Runtime.CacheRetention;

namespace CycloneGames.AssetManagement.Runtime.Integrations.VContainer
{
    /// <summary>
    /// VContainer installer for the CycloneGames.AssetManagement module.
    ///
    /// Usage in your LifetimeScope.Configure():
    /// <code>
    /// var installer = new AssetManagementVContainerInstaller();                    // auto-detect provider
    /// var installer = new AssetManagementVContainerInstaller(r => new MyMod());   // custom factory
    /// installer.Install(builder);
    /// </code>
    ///
    /// The installer registers:
    ///   - IAssetModule as Singleton (auto-detected or custom factory)
    ///   - IAsyncStartable entry point that calls InitializeAsync (VContainer lifecycle-safe)
    ///   - Dispose callback that starts DestroyAsync when the LifetimeScope is destroyed
    /// </summary>
    public class AssetManagementVContainerInstaller : IInstaller
    {
        private readonly Func<IObjectResolver, IAssetModule> _moduleFactory;
        private readonly AssetManagementOptions _options;
        private readonly AssetCacheRetentionOptions _cacheRetentionOptions;
        private readonly Func<IObjectResolver, IAssetPackage> _packageResolver;

        /// <summary>
        /// Creates an installer with optional custom module factory and options.
        /// </summary>
        /// <param name="moduleFactory">
        /// Custom factory for creating the IAssetModule. If null, the default provider is auto-detected
        /// via conditional compilation (YooAsset > Addressables > Resources).
        /// </param>
        /// <param name="options">Global options passed to InitializeAsync.</param>
        /// <param name="cacheRetentionOptions">
        /// Optional cache retention configuration. When <see cref="AssetCacheRetentionOptions.Enabled"/> is false
        /// (the default), no scheduler is registered and behaviour is unchanged. When enabled, an
        /// <see cref="AssetCacheRetentionScheduler"/> is registered as a VContainer entry point.
        /// </param>
        /// <param name="packageResolver">
        /// Resolves the package the scheduler should trim. If null, the scheduler falls back to
        /// <see cref="AssetManagementLocator.DefaultPackage"/>. Provide a resolver when the package is
        /// registered in the container or created outside the locator.
        /// </param>
        public AssetManagementVContainerInstaller(
            Func<IObjectResolver, IAssetModule> moduleFactory = null,
            AssetManagementOptions options = default,
            AssetCacheRetentionOptions cacheRetentionOptions = default,
            Func<IObjectResolver, IAssetPackage> packageResolver = null)
        {
            _moduleFactory = moduleFactory;
            _options = options;
            _cacheRetentionOptions = cacheRetentionOptions;
            _packageResolver = packageResolver;
        }

        public void Install(IContainerBuilder builder)
        {
            // Register IAssetModule.
            if (_moduleFactory != null)
            {
                builder.Register(_moduleFactory, Lifetime.Singleton).As<IAssetModule>();
            }
            else
            {
                builder.Register(_ => CreateDefaultModule(), Lifetime.Singleton).As<IAssetModule>();
            }

            // Async initialization via VContainer entry point lifecycle.
            var options = _options;
            builder.RegisterEntryPoint(
                resolver => new AssetModuleStartable(resolver.Resolve<IAssetModule>(), options),
                Lifetime.Singleton);

            // Optional retention scheduler, started with the scope and disposed with it.
            if (_cacheRetentionOptions.Enabled)
            {
                var retention = _cacheRetentionOptions;
                var packageResolver = _packageResolver;
                builder.RegisterEntryPoint(
                    resolver =>
                    {
                        Func<IAssetPackage> provider = packageResolver != null
                            ? () => packageResolver(resolver)
                            : () => AssetManagementLocator.DefaultPackage;
                        var scheduler = new AssetCacheRetentionScheduler(
                            provider,
                            retention.Policy,
                            TimeSpan.FromSeconds(retention.CheckIntervalSeconds),
                            retention.LogEvictions);
                        return new AssetCacheRetentionStartable(scheduler);
                    },
                    Lifetime.Singleton);
            }

            // Deterministic cleanup when LifetimeScope is destroyed.
            builder.RegisterDisposeCallback(resolver =>
            {
                if (resolver.TryResolve<IAssetModule>(out var module))
                {
                    module.DestroyAsync().Forget();
                }
            });
        }

        private static IAssetModule CreateDefaultModule()
        {
#if YOOASSET_PRESENT
            var yooAssetModule = TryCreateModule("CycloneGames.AssetManagement.Runtime.YooAssetModule, CycloneGames.AssetManagement.Runtime.Providers.YooAsset");
            if (yooAssetModule != null)
            {
                return yooAssetModule;
            }
#endif

#if ADDRESSABLES_PRESENT
            var addressablesModule = TryCreateModule("CycloneGames.AssetManagement.Runtime.AddressablesModule, CycloneGames.AssetManagement.Runtime.Providers.Addressables");
            if (addressablesModule != null)
            {
                return addressablesModule;
            }
#endif

            return new ResourcesModule();
        }

        private static IAssetModule TryCreateModule(string assemblyQualifiedTypeName)
        {
            var type = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
            return type == null ? null : Activator.CreateInstance(type) as IAssetModule;
        }
    }

    /// <summary>
    /// Internal entry point that initializes the asset module during VContainer's
    /// IAsyncStartable lifecycle: fully awaited, exception-safe, cancellation-aware.
    /// </summary>
    internal sealed class AssetModuleStartable : IAsyncStartable
    {
        private readonly IAssetModule _module;
        private readonly AssetManagementOptions _options;

        public AssetModuleStartable(IAssetModule module, AssetManagementOptions options)
        {
            _module = module;
            _options = options;
        }

        public async
#if VCONTAINER_UNITASK_INTEGRATION
            UniTask
#elif UNITY_2023_1_OR_NEWER
            UnityEngine.Awaitable
#else
            System.Threading.Tasks.Task
#endif
            StartAsync(CancellationToken cancellation = default)
        {
            await _module.InitializeAsync(_options);
        }
    }

    /// <summary>
    /// Configuration for the optional cache retention scheduler registered by <see cref="AssetManagementVContainerInstaller"/>.
    /// Default value has <see cref="Enabled"/> == false, so the installer behaves exactly as before unless a caller opts in.
    /// </summary>
    public readonly struct AssetCacheRetentionOptions
    {
        private const double DEFAULT_CHECK_INTERVAL_SECONDS = 30d;
        private const double DEFAULT_MINIMUM_IDLE_SECONDS = 120d;

        public readonly bool Enabled;
        public readonly AssetCacheRetentionPolicy Policy;
        public readonly double CheckIntervalSeconds;
        public readonly bool LogEvictions;

        public AssetCacheRetentionOptions(
            bool enabled,
            AssetCacheRetentionPolicy policy = default,
            double checkIntervalSeconds = 30d,
            bool logEvictions = false)
        {
            Enabled = enabled;
            Policy = enabled && policy.EvictionRules.Count == 0 && policy.PreserveRules.Count == 0
                ? AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(DEFAULT_MINIMUM_IDLE_SECONDS))
                : policy;
            CheckIntervalSeconds = IsInvalidInterval(checkIntervalSeconds)
                ? DEFAULT_CHECK_INTERVAL_SECONDS
                : checkIntervalSeconds;
            LogEvictions = logEvictions;
        }

        private static bool IsInvalidInterval(double seconds)
        {
            return double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d;
        }
    }

    /// <summary>
    /// Bridges <see cref="AssetCacheRetentionScheduler"/> to VContainer's entry-point lifecycle.
    /// </summary>
    internal sealed class AssetCacheRetentionStartable : IStartable, IDisposable
    {
        private readonly AssetCacheRetentionScheduler _scheduler;

        public AssetCacheRetentionStartable(AssetCacheRetentionScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public void Start()
        {
            _scheduler.Start();
        }

        public void Dispose()
        {
            _scheduler.Dispose();
        }
    }
}
#endif
