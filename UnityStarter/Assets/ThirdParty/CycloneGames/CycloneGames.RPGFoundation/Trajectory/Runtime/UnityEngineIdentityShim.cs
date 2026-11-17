// Package-local copy of the Unity compatibility shims. The instance id is 32-bit on 2022 LTS and an
// EntityId from 6.6 onward, and scene queries gain sort-free overloads on the same boundary. A package
// carries this file so that no cross-package assembly reference is introduced;
// it only contains the shims that package actually uses (runtime packages must not ship scene-wide
// queries, which the CG0011 analyzer rejects). CompatShimConsistencyTests fails when the version
// boundaries drift between copies. An assembly that has InternalsVisibleTo into another assembly
// carrying this shim must use inline version branches instead of a second copy (duplicate internal
// types collide as CS0436).
using UnityEngine;

namespace CycloneGames.Compat
{
    /// <summary>
    /// Maps a Unity object to a 64-bit identity token for dictionaries, comparisons and contracts that
    /// must stay engine-independent. Tokens are session-scoped: never persist or replicate them.
    /// </summary>
    internal static class UnityObjectId
    {
        internal const ulong None = 0UL;

        internal static ulong Of(Object target)
        {
            if (target == null)
            {
                return None;
            }

#if UNITY_6000_6_OR_NEWER
            return EntityId.ToULong(target.GetEntityId());
#else
            // Zero-extend: a negative instance id must not sign-extend into 0xFFFF_FFFF_xxxx_xxxx,
            // which would render as a huge token and break equality with the uint-truncated form
            // used elsewhere in the repository.
            return unchecked((ulong)(uint)target.GetInstanceID());
#endif
        }
    }
}
