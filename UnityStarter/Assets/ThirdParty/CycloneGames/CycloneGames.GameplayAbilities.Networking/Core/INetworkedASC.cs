namespace CycloneGames.GameplayAbilities.Networking
{
    using CycloneGames.Networking;

    public interface INetworkedASC
    {
        uint NetworkId { get; }
        int OwnerConnectionId { get; }

        void OnServerConfirmActivation(int abilityDefinitionId, int abilitySpecHandle, int predictionKey);
        void OnServerConfirmActivation(int abilityDefinitionId, int abilitySpecHandle, int predictionKey, int predictionKeyOwner, int predictionInputSequence);
        void OnServerRejectActivation(int abilityDefinitionId, int abilitySpecHandle, int predictionKey);
        void OnServerRejectActivation(int abilityDefinitionId, int abilitySpecHandle, int predictionKey, int predictionKeyOwner, int predictionInputSequence);
        void OnAbilityEnded(int abilityDefinitionId, int abilitySpecHandle);
        void OnAbilityCancelled(int abilityDefinitionId, int abilitySpecHandle);
        void OnAbilityMulticast(AbilityMulticastData data);

        void OnReplicatedEffectApplied(EffectReplicationData data);
        void OnReplicatedEffectRemoved(int effectInstanceId);
        void OnReplicatedStackChanged(int effectInstanceId, int newStackCount);
        void OnReplicatedEffectUpdated(EffectUpdateData data);

        void OnReplicatedAttributeUpdate(AttributeUpdateData data);
        void OnReplicatedTagUpdate(TagUpdateData data);

        GASFullStateData CaptureFullState();
        void OnFullState(GASFullStateData data);
        bool OnStateSyncMetadata(GASStateSyncMetadata metadata);
    }

    public interface INetworkedASCConnectionScopedFullState
    {
        GASFullStateData CaptureFullStateForConnection(INetConnection client);
    }
}
