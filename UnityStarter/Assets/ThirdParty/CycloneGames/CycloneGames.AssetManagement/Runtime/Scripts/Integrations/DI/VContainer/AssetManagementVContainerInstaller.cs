#if CYCLONEGAMES_HAS_VCONTAINER
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
    /// var installer = new AssetManagementVContainerInstaller();                   // Resources provider
    /// var installer = new AssetManagementVContainerInstaller(r => new MyMod());  // explicit provider factory
    /// installer.Install(builder);
    /// </code>
    ///
    /// The installer registers:
    ///   - IAssetModule as Singleton (Resources or an explicit provider factory)
    ///   - IAsyncStartable entry point that calls InitializeAsync (VContainer lifecycle-safe)
    /// Scope owners must await IAssetModule.DestroyAsync before disposing the LifetimeScope.
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
        /// Custom factory for creating the IAssetModule. If null, ResourcesModule is used.
        /// </param>
        /// <param name="options">Global options passed to InitializeAsync.</param>
        /// <param name="cacheRetentionOptions">
        /// Optional cache retention configuration. When <see cref="AssetCacheRetentionOptions.Enabled"/> is false
        /// (the default), no scheduler is registered and behaviour is unchanged. When enabled, an
        /// <see cref="AssetCacheRetentionScheduler"/> is registered as a VContainer entry point.
        /// </param>
        /// <param name="packageResolver">
        /// Resolves the package the scheduler should trim. This is required when cache retention is enabled.
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
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (_cacheRetentionOptions.Enabled && _packageResolver == null)
            {
                throw new InvalidOperationException(
                    "Cache retention requires an explicit packageResolver. Global package location is intentionally unsupported.");
            }

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
                        Func<IAssetPackage> provider = () =>
                            packageResolver(resolver) ??
                            throw new InvalidOperationException("The configured packageResolver returned null.");
                        var scheduler = new AssetCacheRetentionScheduler(
                            provider,
                            retention.Policy,
                            TimeSpan.FromSeconds(retention.CheckIntervalSeconds),
                            retention.LogEvictions);
                        return new AssetCacheRetentionStartable(scheduler);
                    },
                    Lifetime.Singleton);
            }

            // VContainer's synchronous scope-disposal callback cannot await provider shutdown.
            // The composition root owns the explicit await before disposing this scope.
        }

        private static IAssetModule CreateDefaultModule()
        {
            return new ResourcesModule();
        }
    }

    /// <summary>
    /// Internal entry point that initializes the asset module during VContainer's
    /// IAsyncStartable lifecycle. Cancellation is observed before provider-global initialization begins;
    /// an initialization already in progress completes according to the selected provider contract.
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
#if CYCLONEGAMES_HAS_VCONTAINER_UNITASK
            UniTask
#elif UNITY_2023_1_OR_NEWER
            UnityEngine.Awaitable
#else
            System.Threading.Tasks.Task
#endif
            StartAsync(CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            await _module.InitializeAsync(_options);
        }
    }

    /// <summary>
    /// Configuration for the optional cache retention scheduler registered by <see cref="AssetManagementVContainerInstaller"/>.
    /// Disabled by default. Enabling it also requires an explicit package resolver.
    /// </summary>
    public readonly struct AssetCacheRetentionOptions
    {
        private const double DEFAULT_CHECK_INTERVAL_SECONDS = 30d;
        private const double DEFAULT_MINIMUM_IDLE_SECONDS = 120d;
        private const double MAXIMUM_RETENTION_SECONDS = 365d * 24d * 60d * 60d;

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
            CheckIntervalSeconds = NormalizeInterval(checkIntervalSeconds);
            LogEvictions = logEvictions;
        }

        private static double NormalizeInterval(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d)
            {
                return DEFAULT_CHECK_INTERVAL_SECONDS;
            }

            return Math.Min(seconds, MAXIMUM_RETENTION_SECONDS);
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
