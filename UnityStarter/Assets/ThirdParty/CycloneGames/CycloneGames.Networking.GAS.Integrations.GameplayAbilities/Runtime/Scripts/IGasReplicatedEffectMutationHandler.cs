namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Strategy interface for applying remote effect mutation commands to local ASC state.
    /// </summary>
    public interface IGasReplicatedEffectMutationHandler
    {
        bool TryRemoveReplicatedEffect(int effectInstanceId);
        bool TryApplyReplicatedStackChange(int effectInstanceId, int newStackCount);
        bool TryApplyReplicatedEffectUpdate(EffectUpdateData data);
    }
}