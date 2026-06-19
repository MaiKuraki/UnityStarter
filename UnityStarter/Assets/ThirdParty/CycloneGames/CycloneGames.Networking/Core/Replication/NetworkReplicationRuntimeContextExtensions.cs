using System;

namespace CycloneGames.Networking.Replication
{
    public static class NetworkReplicationRuntimeContextExtensions
    {
        public static INetworkRuntimeContextBuilder AddDefaultReplicationServices(
            this INetworkRuntimeContextBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            INetworkInterestEvaluator evaluator = DefaultNetworkInterestEvaluator.Instance;
            return builder
                .AddService(evaluator)
                .AddService(new NetworkReplicationPlanner(evaluator));
        }
    }
}
