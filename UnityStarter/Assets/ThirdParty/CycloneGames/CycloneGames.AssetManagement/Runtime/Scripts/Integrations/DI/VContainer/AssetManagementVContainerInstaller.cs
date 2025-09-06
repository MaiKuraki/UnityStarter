#if VCONTAINER_PRESENT
using System;
using System.Threading.Tasks;
using VContainer;
using VContainer.Unity;

namespace CycloneGames.AssetManagement.Runtime.Integrations.VContainer
{
    public class AssetManagementVContainerInstaller : IInstaller
    {
        private readonly Func<IObjectResolver, IAssetModule> moduleFactory;
        private readonly Func<IObjectResolver, Task> postBuildCallback;

        public AssetManagementVContainerInstaller(Func<IObjectResolver, IAssetModule> moduleFactory, Func<IObjectResolver, Task> postBuildCallback = null)
        {
            this.moduleFactory = moduleFactory;
            this.postBuildCallback = postBuildCallback;
        }

        public void Install(IContainerBuilder builder)
        {
            if (moduleFactory != null)
            {
                builder.Register(moduleFactory, Lifetime.Singleton).As<IAssetModule>();
            }
            else
            {
#if YOOASSET_PRESENT
                builder.Register<YooAssetModule>(Lifetime.Singleton).As<IAssetModule>();
#elif ADDRESSABLES_PRESENT
                builder.Register<AddressablesAssetModule>(Lifetime.Singleton).As<IAssetModule>();
#endif
            }

            if (postBuildCallback != null)
            {
                builder.RegisterBuildCallback(async resolver =>
                {
                    await postBuildCallback(resolver);
                });
            }
        }
    }
}
#endif