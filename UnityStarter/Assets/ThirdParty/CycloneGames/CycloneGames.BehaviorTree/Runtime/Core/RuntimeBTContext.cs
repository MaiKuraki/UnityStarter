using System;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public class RuntimeBTContext : IRuntimeBTContext
    {
        public GameObject OwnerGameObject { get; set; }
        public IRuntimeBTServiceResolver ServiceResolver { get; set; }

        public RuntimeBTContext(GameObject ownerGameObject = null, IRuntimeBTServiceResolver serviceResolver = null)
        {
            OwnerGameObject = ownerGameObject;
            ServiceResolver = serviceResolver;
        }

        public T GetOwner<T>() where T : class
        {
            if (OwnerGameObject is T ownerAsT)
                return ownerAsT;

            if (typeof(Component).IsAssignableFrom(typeof(T)) && OwnerGameObject != null)
                return OwnerGameObject.GetComponent(typeof(T)) as T;

            return null;
        }

        public T GetService<T>() where T : class
        {
            return ServiceResolver?.Resolve<T>();
        }

        /// <summary>
        /// Convenience wrapper for <see cref="IServiceProvider"/>-based containers.
        /// Use: <c>new ServiceProviderResolver(myServiceProvider)</c> as the
        /// <see cref="IRuntimeBTServiceResolver"/> argument.
        /// </summary>
        public sealed class ServiceProviderResolver : IRuntimeBTServiceResolver
        {
            private readonly IServiceProvider _provider;

            public ServiceProviderResolver(IServiceProvider provider)
            {
                _provider = provider;
            }

            public T Resolve<T>() where T : class
            {
                return _provider?.GetService(typeof(T)) as T;
            }
        }
    }
}
