#if GAMEPLAY_FRAMEWORK_PRESENT && GAMEPLAY_TAGS_PRESENT
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.GameplayTags
{
    /// <summary>
    /// Optional bridge helpers for using GameplayTags package APIs with GameplayFramework Actor.
    /// </summary>
    public static class ActorGameplayTagExtensions
    {
        public static bool TryGetGameplayTagContainer(this Actor actor, out GameplayTagCountContainer container)
        {
            container = null;
            if (actor == null) return false;

            GameObjectGameplayTagContainer component = actor.GetComponent<GameObjectGameplayTagContainer>();
            if (component == null) return false;

            container = component.GameplayTagContainer;
            return container != null;
        }

        public static bool ActorHasGameplayTag(this Actor actor, GameplayTag tag)
        {
            if (!actor.TryGetGameplayTagContainer(out GameplayTagCountContainer container))
            {
                return false;
            }

            return container.HasTag(tag);
        }

        public static bool AddGameplayTag(this Actor actor, GameplayTag tag)
        {
            if (!actor.TryGetGameplayTagContainer(out GameplayTagCountContainer container))
            {
                return false;
            }

            container.AddTag(tag);
            return true;
        }

        public static bool RemoveGameplayTag(this Actor actor, GameplayTag tag)
        {
            if (!actor.TryGetGameplayTagContainer(out GameplayTagCountContainer container))
            {
                return false;
            }

            container.RemoveTag(tag);
            return true;
        }
    }
}

#endif