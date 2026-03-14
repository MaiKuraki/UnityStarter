#if VCONTAINER_PRESENT
using System;
using System.Threading;
using VContainer;
using VContainer.Unity;

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
    ///   - Dispose callback that calls Destroy() when the LifetimeScope is destroyed
    /// </summary>
    public class AssetManagementVContainerInstaller : IInstaller
    {
        private readonly Func<IObjectResolver, IAssetModule> _moduleFactory;
        private readonly AssetManagementOptions _options;

        /// <summary>
        /// Creates an installer with optional custom module factory and options.
        /// </summary>
        /// <param name="moduleFactory">
        /// Custom factory for creating the IAssetModule. If null, the default provider is auto-detected
        /// via conditional compilation (YooAsset > Addressables > Resources).
        /// </param>
        /// <param name="options">Global options passed to InitializeAsync.</param>
        public AssetManagementVContainerInstaller(
            Func<IObjectResolver, IAssetModule> moduleFactory = null,
            AssetManagementOptions options = default)
        {
            _moduleFactory = moduleFactory;
            _options = options;
        }

        public void Install(IContainerBuilder builder)
        {
            // ── Register IAssetModule ────────────────────────────────────────
            if (_moduleFactory != null)
            {
                builder.Register(_moduleFactory, Lifetime.Singleton).As<IAssetModule>();
            }
            else
            {
#if YOOASSET_PRESENT
                builder.Register<YooAssetModule>(Lifetime.Singleton).As<IAssetModule>();
#elif ADDRESSABLES_PRESENT
                builder.Register<AddressablesModule>(Lifetime.Singleton).As<IAssetModule>();
#else
                builder.Register<ResourcesModule>(Lifetime.Singleton).As<IAssetModule>();
#endif
            }

            // ── Async initialization via VContainer entry point lifecycle ────
            // Registers AssetModuleStartable as IAsyncStartable.
            // VContainer's EntryPointDispatcher will await StartAsync before any
            // IStartable or ITickable begins, ensuring the module is ready.
            var options = _options;
            builder.RegisterEntryPoint(
                resolver => new AssetModuleStartable(resolver.Resolve<IAssetModule>(), options),
                Lifetime.Singleton);

            // ── Deterministic cleanup when LifetimeScope is destroyed ────────
            builder.RegisterDisposeCallback(resolver =>
            {
                if (resolver.TryResolve<IAssetModule>(out var module))
                {
                    module.Destroy();
                }
            });
        }
    }

    /// <summary>
    /// Internal entry point that initializes the asset module during VContainer's
    /// IAsyncStartable lifecycle — fully awaited, exception-safe, cancellation-aware.
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
            Cysharp.Threading.Tasks.UniTask
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
}
#endif