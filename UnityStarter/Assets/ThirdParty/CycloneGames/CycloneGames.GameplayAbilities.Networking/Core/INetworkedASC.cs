namespace CycloneGames.GameplayAbilities.Networking
{
    public interface INetworkedASC
    {
        uint NetworkId { get; }
        int OwnerConnectionId { get; }

        void OnServerConfirmActivation(int abilityIndex, int predictionKey);
        void OnServerConfirmActivation(int abilityIndex, int predictionKey, int predictionKeyOwner, int predictionInputSequence);
        void OnServerRejectActivation(int abilityIndex, int predictionKey);
        void OnServerRejectActivation(int abilityIndex, int predictionKey, int predictionKeyOwner, int predictionInputSequence);
        void OnAbilityEnded(int abilityIndex);
        void OnAbilityCancelled(int abilityIndex);
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
}
