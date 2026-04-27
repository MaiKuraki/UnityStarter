namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Strategy interface for applying remote effect mutation commands to local ASC state.
    /// </summary>
    public interface IGASReplicatedEffectMutationHandler
    {
        bool TryRemoveReplicatedEffect(int effectInstanceId);
        bool TryApplyReplicatedStackChange(int effectInstanceId, int newStackCount);
        bool TryApplyReplicatedEffectUpdate(EffectUpdateData data);
    }
}