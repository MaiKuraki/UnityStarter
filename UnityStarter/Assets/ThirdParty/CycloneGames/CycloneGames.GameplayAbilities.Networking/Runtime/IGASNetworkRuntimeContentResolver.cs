using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Resolves explicitly registered process-local GAS definitions and names to stable network
    /// content identities, and resolves those identities back to the registered runtime values.
    /// </summary>
    public interface IGASNetworkRuntimeContentResolver
    {
        bool TryGetAbilityId(GameplayAbility ability, out GASNetworkContentId id);
        bool TryResolveAbility(GASNetworkContentId id, out GameplayAbility ability);

        bool TryGetEffectId(GameplayEffect effect, out GASNetworkContentId id);
        bool TryResolveEffect(GASNetworkContentId id, out GameplayEffect effect);

        bool TryGetAttributeId(string attributeName, out GASNetworkContentId id);
        bool TryResolveAttributeName(GASNetworkContentId id, out string attributeName);

        bool TryGetSetByCallerNameId(string setByCallerName, out GASNetworkContentId id);
        bool TryResolveSetByCallerName(GASNetworkContentId id, out string setByCallerName);

        bool TryGetTargetSurfaceId(object targetSurface, out GASNetworkContentId id);
        bool TryResolveTargetSurface(GASNetworkContentId id, out object targetSurface);
    }
}
