#if VCONTAINER_PRESENT
using VContainer;
using VContainer.Unity;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Runtime.Integrations.VContainer
{
    public class FactoryVContainerInstaller : IInstaller
    {
        private readonly Lifetime _lifetime;

        public FactoryVContainerInstaller(Lifetime lifetime = Lifetime.Singleton)
        {
            _lifetime = lifetime;
        }

        public void Install(IContainerBuilder builder)
        {
            // Register the VContainer-aware spawner as the implementation of IUnityObjectSpawner
            builder.Register<VContainerObjectSpawner>(_lifetime).As<IUnityObjectSpawner>();
        }
    }

    public static class FactoryVContainerExtensions
    {
        /// <summary>
        /// Extension method to easily register the Factory system.
        /// </summary>
        public static void RegisterFactorySystem(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton)
        {
            builder.Register<VContainerObjectSpawner>(lifetime).As<IUnityObjectSpawner>();
        }
    }
}
#endif