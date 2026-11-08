using System;
using System.Diagnostics;

namespace CycloneGames.BehaviorTree.Core
{
    public class RuntimeBTContext : IRuntimeBTContext
    {
        public object Owner { get; set; }
        public IRuntimeBTServiceResolver ServiceResolver { get; set; }

        public RuntimeBTContext(object owner = null, IRuntimeBTServiceResolver serviceResolver = null)
        {
            Owner = owner;
            ServiceResolver = serviceResolver;
        }

        /// <summary>
        /// Returns the owner when it is directly assignable to <typeparamref name="T"/>.
        /// The Unity layer overrides this to resolve components from a GameObject owner.
        /// </summary>
        public virtual T GetOwner<T>() where T : class
        {
            return Owner as T;
        }

        public virtual T GetService<T>() where T : class
        {
            return ServiceResolver?.Resolve<T>();
        }

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

    public static class RuntimeBTTime
    {
        private static readonly double OriginSeconds =
            Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>
        /// Reads time from the blackboard service when one is bound, otherwise from a
        /// monotonic stopwatch. The Unity layer binds <c>UnityBTTimeProvider</c>
        /// so nodes follow <c>Time.time</c> and <c>Time.unscaledTime</c>.
        /// </summary>
        public static double GetTime(RuntimeBlackboard blackboard, bool useUnscaledTime)
        {
            IRuntimeBTTimeProvider timeProvider = blackboard?.GetService<IRuntimeBTTimeProvider>();
            if (timeProvider != null)
            {
                return useUnscaledTime ? timeProvider.UnscaledTimeAsDouble : timeProvider.TimeAsDouble;
            }

            return GetMonotonicTime();
        }

        /// <summary>Monotonic seconds since process start. Never affected by time scale.</summary>
        public static double GetMonotonicTime()
        {
            return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency - OriginSeconds;
        }
    }
}
