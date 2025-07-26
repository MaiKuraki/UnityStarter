using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A configuration struct that defines the GAMEPLAY LOGIC parameters for a targeting query.
    /// It is used to filter targets based on gameplay rules like factions and states, AFTER a physical trace has been performed.
    /// </summary>
    public struct TargetingQuery
    {
        public GameplayAbility OwningAbility;
        public bool IgnoreCaster;

        [Tooltip("Target must have ALL of these tags.")]
        public GameplayTagContainer RequiredTags;
        [Tooltip("Target must have NONE of these tags.")]
        public GameplayTagContainer ForbiddenTags;
    }
}