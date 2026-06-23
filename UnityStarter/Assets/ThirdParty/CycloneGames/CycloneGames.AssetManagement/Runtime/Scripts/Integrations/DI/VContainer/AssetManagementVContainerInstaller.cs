#if VCONTAINER_PRESENT
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    ///   - Dispose callback that starts DestroyAsync when the LifetimeScope is destroyed
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
}
#endif
