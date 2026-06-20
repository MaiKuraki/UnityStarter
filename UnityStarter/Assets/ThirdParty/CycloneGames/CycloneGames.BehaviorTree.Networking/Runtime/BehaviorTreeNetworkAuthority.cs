using System;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.Networking;

namespace CycloneGames.BehaviorTree.Networking
{
    public enum BehaviorTreeNetworkAuthorityRole : byte
    {
        None = 0,
        ServerAuthority = 1,
        AutonomousProxy = 2,
        SimulatedProxy = 3
    }

    public readonly struct BehaviorTreeNetworkAuthorityContext
    {
        public readonly bool IsServer;
        public readonly bool IsClient;
        public readonly int LocalConnectionId;
        public readonly uint AuthorityGeneration;

        public BehaviorTreeNetworkAuthorityContext(
            bool isServer,
            bool isClient,
            int localConnectionId,
            uint authorityGeneration = 0u)
        {
            IsServer = isServer;
            IsClient = isClient;
            LocalConnectionId = localConnectionId;
            AuthorityGeneration = authorityGeneration;
        }
    }

    public readonly struct NetworkedBehaviorTreeAgent
    {
        public readonly RuntimeBehaviorTree Tree;
        public readonly uint NetworkId;
        public readonly int OwnerConnectionId;
        public readonly ulong OwnerPlayerId;
        public readonly int TeamId;
        public readonly uint InterestLayerMask;
        public readonly bool AlwaysRelevant;
        public readonly NetworkVector3 InterestPosition;
        public readonly ulong TreeTemplateHash;
        public readonly uint AuthorityGeneration;

        public NetworkedBehaviorTreeAgent(
            RuntimeBehaviorTree tree,
            uint networkId,
            int ownerConnectionId,
            ulong ownerPlayerId,
            int teamId,
            uint interestLayerMask,
            bool alwaysRelevant,
            NetworkVector3 interestPosition,
            ulong treeTemplateHash = 0UL,
            uint authorityGeneration = 0u)
        {
            Tree = tree;
            NetworkId = networkId;
            OwnerConnectionId = ownerConnectionId;
            OwnerPlayerId = ownerPlayerId;
            TeamId = teamId;
            InterestLayerMask = interestLayerMask;
            AlwaysRelevant = alwaysRelevant;
            InterestPosition = interestPosition;
            TreeTemplateHash = treeTemplateHash;
            AuthorityGeneration = authorityGeneration;
        }

        public bool IsValid => NetworkId != 0u && Tree != null && InterestPosition.IsFinite();

        public NetworkInterestTarget ToInterestTarget()
        {
            return new NetworkInterestTarget(NetworkId, InterestPosition, InterestLayerMask, OwnerPlayerId, TeamId);
        }
    }

    public interface IBehaviorTreeNetworkAuthorityResolver
    {
        BehaviorTreeNetworkAuthorityRole GetRole(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent);

        bool CanTickAuthoritativeTree(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent);

        bool CanApplyRemotePayload(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent,
            in BehaviorTreeStatePayloadMessage payload);
    }

    public sealed class ServerAuthoritativeBehaviorTreeAuthorityResolver : IBehaviorTreeNetworkAuthorityResolver
    {
        public BehaviorTreeNetworkAuthorityRole GetRole(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent)
        {
            if (!agent.IsValid)
            {
                return BehaviorTreeNetworkAuthorityRole.None;
            }

            if (context.IsServer)
            {
                return BehaviorTreeNetworkAuthorityRole.ServerAuthority;
            }

            if (context.IsClient && context.LocalConnectionId == agent.OwnerConnectionId)
            {
                return BehaviorTreeNetworkAuthorityRole.AutonomousProxy;
            }

            return BehaviorTreeNetworkAuthorityRole.SimulatedProxy;
        }

        public bool CanTickAuthoritativeTree(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent)
        {
            return context.IsServer && agent.IsValid;
        }

        public bool CanApplyRemotePayload(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent,
            in BehaviorTreeStatePayloadMessage payload)
        {
            if (!agent.IsValid || !payload.IsValid || payload.TargetNetworkId != agent.NetworkId)
            {
                return false;
            }

            return context.IsClient && !context.IsServer;
        }
    }

    public sealed class OwnerPredictedBehaviorTreeAuthorityResolver : IBehaviorTreeNetworkAuthorityResolver
    {
        public BehaviorTreeNetworkAuthorityRole GetRole(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent)
        {
            if (!agent.IsValid)
            {
                return BehaviorTreeNetworkAuthorityRole.None;
            }

            if (context.IsServer)
            {
                return BehaviorTreeNetworkAuthorityRole.ServerAuthority;
            }

            if (context.IsClient && context.LocalConnectionId == agent.OwnerConnectionId)
            {
                return BehaviorTreeNetworkAuthorityRole.AutonomousProxy;
            }

            return BehaviorTreeNetworkAuthorityRole.SimulatedProxy;
        }

        public bool CanTickAuthoritativeTree(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent)
        {
            if (!agent.IsValid)
            {
                return false;
            }

            return context.IsServer ||
                   (context.IsClient && context.LocalConnectionId == agent.OwnerConnectionId);
        }

        public bool CanApplyRemotePayload(
            in BehaviorTreeNetworkAuthorityContext context,
            in NetworkedBehaviorTreeAgent agent,
            in BehaviorTreeStatePayloadMessage payload)
        {
            if (!agent.IsValid || !payload.IsValid || payload.TargetNetworkId != agent.NetworkId)
            {
                return false;
            }

            return context.IsClient || context.IsServer;
        }
    }
}
