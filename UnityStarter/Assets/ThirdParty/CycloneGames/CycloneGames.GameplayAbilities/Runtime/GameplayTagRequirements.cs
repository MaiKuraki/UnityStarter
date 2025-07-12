using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A structure that defines tag requirements for an operation.
    /// It checks for tags that are required to be present and tags that must be absent.
    /// </summary>
    public struct GameplayTagRequirements
    {
        /// <summary>
        /// The actor must have ALL of these tags.
        /// </summary>
        public GameplayTagContainer RequiredTags { get; }

        /// <summary>
        /// The actor must have NONE of these tags.
        /// </summary>
        public GameplayTagContainer ForbiddenTags { get; }

        public GameplayTagRequirements(GameplayTagContainer required = null, GameplayTagContainer ignored = null)
        {
            RequiredTags = required ?? new GameplayTagContainer();
            ForbiddenTags = ignored ?? new GameplayTagContainer();
        }

        public bool IsEmpty() => RequiredTags.IsEmpty && ForbiddenTags.IsEmpty;

        /// <summary>
        /// Checks if the provided container meets these requirements.
        /// </summary>
        public bool MeetsRequirements(GameplayTagCountContainer container)
        {
            if (container.HasAny(ForbiddenTags))
            {
                return false;
            }
            if (!container.HasAll(RequiredTags))
            {
                return false;
            }
            return true;
        }
    }
}