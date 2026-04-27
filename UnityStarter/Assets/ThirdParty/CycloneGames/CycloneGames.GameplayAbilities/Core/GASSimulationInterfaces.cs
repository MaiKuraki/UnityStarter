using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Core contract for AbilitySystemComponent simulation.
    /// All members use plain C# types (object, interfaces) so the entire GAS core can run
    /// without Unity Engine linkage — enabling headless unit tests, server-side authority,
    /// and future Godot portability.
    /// </summary>
    public interface IAbilitySystemComponent
    {
        /// <summary>
        /// An opaque reference to the owning actor (e.g., player controller).
        /// Typed as object to avoid coupling the core to a concrete actor class.
        /// </summary>
        object OwnerActor { get; }

        /// <summary>
        /// An opaque reference to the physical avatar actor (e.g., character mesh).
        /// Typed as object for the same decoupling reason as OwnerActor.
        /// </summary>
        object AvatarActor { get; }

        ITagCountContainer CombinedTags { get; }

        IGameplayAttribute GetAttribute(string name);

        void Tick(float deltaTime, bool isServer);
    }

    /// <summary>
    /// Read-write view over an ASC's aggregated GameplayTag state.
    /// Separate from IAbilitySystemComponent so tag queries can be injected in isolation
    /// (e.g., into targeting filters or effect application checks).
    /// </summary>
    public interface ITagCountContainer
    {
        bool HasTag(GameplayTag tag);
        bool HasAny(IEnumerable<GameplayTag> tags);
        bool HasAll(IEnumerable<GameplayTag> tags);

        /// <summary>
        /// Generic overload -- avoids struct enumerator boxing when T is a concrete IGameplayTagContainer.
        /// Uses GetTags() struct enumerator path for zero-allocation iteration.
        /// C# 8 default interface method: callers with a concrete ITagCountContainer reference get this for free.
        /// </summary>
        bool HasAny<T>(in T tags) where T : IGameplayTagContainer
        {
            if (tags.IsEmpty) return false;
            var en = tags.GetTags();
            while (en.MoveNext())
                if (HasTag(en.Current)) return true;
            return false;
        }

        /// <summary>
        /// Generic overload -- avoids struct enumerator boxing when T is a concrete IGameplayTagContainer.
        /// </summary>
        bool HasAll<T>(in T tags) where T : IGameplayTagContainer
        {
            if (tags.IsEmpty) return true;
            var en = tags.GetTags();
            while (en.MoveNext())
                if (!HasTag(en.Current)) return false;
            return true;
        }

        void AddTag(GameplayTag tag);
        void RemoveTag(GameplayTag tag);
        void Clear();
    }

    /// <summary>
    /// Read-only view of a single gameplay attribute.
    /// 
    /// Attributes have a two-layer value model:
    /// - BaseValue is the permanent value set by instant effects or direct assignment.
    /// - CurrentValue is BaseValue plus all active duration/infinite modifiers applied on top.
    /// Separating the two enables modifier removal without losing the underlying base.
    /// </summary>
    public interface IGameplayAttribute
    {
        string Name { get; }
        float BaseValue { get; }
        float CurrentValue { get; }
    }

    /// <summary>
    /// A stamped-out instance of a GameplayEffect carrying runtime context
    /// (level, predicted duration, and source metadata).
    /// Multiple specs can reference the same effect definition with different parameters.
    /// </summary>
    public interface IGameplayEffectSpec
    {
        int Level { get; }
        float Duration { get; }
        IGameplayEffectContext Context { get; }
    }

    /// <summary>
    /// Carries source and prediction metadata alongside an applied effect spec.
    /// Implementations may extend this with additional fields (instigator, hit result, etc.).
    /// </summary>
    public interface IGameplayEffectContext
    {
        GASPredictionKey PredictionKey { get; set; }
    }
}
