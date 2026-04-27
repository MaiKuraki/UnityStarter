using System;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// UE GAS-style facade over the core state container.
    /// Runtime adapters should expose familiar ASC methods while delegating state mutation here.
    /// </summary>
    public sealed class GASAbilitySystemFacade
    {
        private readonly GASAbilitySystemState state;
        private readonly IGASCoreNetworkDriver network;

        public GASAbilitySystemState State => state;
        public GASEntityId Entity => state.Entity;

        public GASAbilitySystemFacade(GASAbilitySystemState state, IGASCoreNetworkDriver network = null)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.network = network;
        }

        public GASSpecHandle GiveAbility(
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy = GASInstancingPolicy.InstancedPerActor,
            GASNetExecutionPolicy netExecutionPolicy = GASNetExecutionPolicy.LocalPredicted,
            GASReplicationPolicy replicationPolicy = GASReplicationPolicy.OwnerOnly)
        {
            return state.GrantAbility(
                abilityDefinitionId,
                level,
                instancingPolicy,
                netExecutionPolicy,
                replicationPolicy);
        }

        public bool GiveAbility(in GASAbilityGrantRequest request, out GASSpecHandle handle)
        {
            return state.TryGrantAbility(in request, out handle);
        }

        public bool ClearAbility(GASSpecHandle handle)
        {
            return state.RemoveAbility(handle);
        }

        /// <summary>
        /// Attempts to activate an ability, respecting network execution policies.
        /// 
        /// Dispatch logic:
        /// - LocalPredicted + non-server + locally owned → sends activation RPC, returns Predicted.
        /// - ServerOnly + non-server → sends activation RPC, returns Predicted.
        /// - All other cases → local authority accepts immediately, returns Accepted.
        /// 
        /// Prediction keys must be valid for LocalPredicted; the caller generates one
        /// via <see cref="GASPredictionKey.NewKey()"/> before calling this method.
        /// </summary>
        public GASAbilityActivationResult TryActivateAbility(GASSpecHandle handle, GASPredictionKey predictionKey)
        {
            if (!state.TryGetAbilitySpec(handle, out var spec))
            {
                return new GASAbilityActivationResult(GASAbilityActivationResultCode.MissingSpec, handle, predictionKey);
            }

            if (spec.NetExecutionPolicy == GASNetExecutionPolicy.LocalPredicted)
            {
                if (!predictionKey.IsValid)
                {
                    return new GASAbilityActivationResult(GASAbilityActivationResultCode.InvalidPredictionKey, handle, predictionKey);
                }

                if (network != null && !network.IsServer && network.IsOwner(Entity))
                {
                    network.SendAbilityActivationRequest(Entity, handle, predictionKey);
                    return new GASAbilityActivationResult(GASAbilityActivationResultCode.Predicted, handle, predictionKey);
                }
            }

            if (spec.NetExecutionPolicy == GASNetExecutionPolicy.ServerOnly && network != null && !network.IsServer)
            {
                network.SendAbilityActivationRequest(Entity, handle, predictionKey);
                return new GASAbilityActivationResult(GASAbilityActivationResultCode.Predicted, handle, predictionKey);
            }

            return new GASAbilityActivationResult(GASAbilityActivationResultCode.Accepted, handle, predictionKey);
        }

        public GASAbilityActivationResult ServerReceiveTryActivateAbility(GASSpecHandle handle, GASPredictionKey predictionKey)
        {
            var result = state.TryGetAbilitySpec(handle, out _)
                ? new GASAbilityActivationResult(GASAbilityActivationResultCode.Accepted, handle, predictionKey)
                : new GASAbilityActivationResult(GASAbilityActivationResultCode.MissingSpec, handle, predictionKey);

            network?.SendAbilityActivationResult(Entity, handle, predictionKey, result.Succeeded);
            return result;
        }

        public GASActiveEffectHandle ApplyGameplayEffectSpecToSelf(in GASGameplayEffectSpecData spec)
        {
            var handle = state.ApplyGameplayEffectSpecToSelf(in spec);
            if (network != null)
            {
                var checksum = state.ComputeChecksum();
                network.SendStateDelta(Entity, in checksum);
            }
            return handle;
        }

        public bool RemoveActiveGameplayEffect(GASActiveEffectHandle handle)
        {
            bool removed = state.RemoveActiveEffect(handle);
            if (removed)
            {
                if (network != null)
                {
                    var checksum = state.ComputeChecksum();
                    network.SendStateDelta(Entity, in checksum);
                }
            }

            return removed;
        }

        public void AcceptPrediction(GASPredictionKey predictionKey)
        {
            state.AcceptPrediction(predictionKey);
        }

        public void RejectPrediction(GASPredictionKey predictionKey)
        {
            state.RejectPrediction(predictionKey);
            if (network != null)
            {
                var checksum = state.ComputeChecksum();
                network.SendStateDelta(Entity, in checksum);
            }
        }

        public void SetNumericAttributeBase(GASAttributeId attributeId, float value)
        {
            state.SetAttributeBase(attributeId, value);
            if (network != null)
            {
                var checksum = state.ComputeChecksum();
                network.SendStateDelta(Entity, in checksum);
            }
        }

        public bool GetGameplayAttributeValue(GASAttributeId attributeId, out float currentValue)
        {
            if (state.TryGetAttribute(attributeId, out var attribute))
            {
                currentValue = attribute.CurrentValue;
                return true;
            }

            currentValue = default;
            return false;
        }
    }
}
