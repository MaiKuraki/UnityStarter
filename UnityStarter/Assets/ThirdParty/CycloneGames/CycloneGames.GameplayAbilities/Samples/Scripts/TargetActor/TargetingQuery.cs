using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// A configuration struct that defines the parameters for a targeting query.
    /// This makes TargetActors more configurable and reusable.
    /// </summary>
    public struct TargetingQuery
    {
        public GameplayAbility OwningAbility;
        public LayerMask HitLayerMask;
        public bool IgnoreCaster;

        [Tooltip("Target must have ALL of these tags.")]
        public GameplayTagContainer RequiredTags;

        [Tooltip("Target must have NONE of these tags.")]
        public GameplayTagContainer ForbiddenTags;
    }
}