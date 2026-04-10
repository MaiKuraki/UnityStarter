#if GAMEPLAY_FRAMEWORK_PRESENT && GAMEPLAY_ABILITIES_PRESENT

using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities
{
    /// <summary>
    /// Optional provider contract for exposing AbilitySystemComponent from gameplay actors.
    /// Keep this in integration assembly to avoid core package dependency on GameplayAbilities.
    /// </summary>
    public interface IAbilitySystemProvider
    {
        AbilitySystemComponent AbilitySystem { get; }
    }

    /// <summary>
    /// Optional bridge helpers for wiring GameplayAbilities actor info from GameplayFramework actors.
    /// </summary>
    public static class ActorAbilitySystemExtensions
    {
        public static bool TryGetAbilitySystem(this Actor actor, out AbilitySystemComponent abilitySystem)
        {
            abilitySystem = null;
            if (actor == null) return false;

            if (actor is IAbilitySystemProvider provider && provider.AbilitySystem != null)
            {
                abilitySystem = provider.AbilitySystem;
                return true;
            }

            IAbilitySystemProvider componentProvider = actor.GetComponent<IAbilitySystemProvider>();
            if (componentProvider?.AbilitySystem != null)
            {
                abilitySystem = componentProvider.AbilitySystem;
                return true;
            }

            return false;
        }

        public static bool InitializeAbilityActorInfo(this Actor actor, Actor avatarOverride = null)
        {
            if (!actor.TryGetAbilitySystem(out AbilitySystemComponent abilitySystem))
            {
                return false;
            }

            Actor avatar = avatarOverride != null ? avatarOverride : actor;
            abilitySystem.InitAbilityActorInfo(actor, avatar);
            return true;
        }
    }
}

#endif
