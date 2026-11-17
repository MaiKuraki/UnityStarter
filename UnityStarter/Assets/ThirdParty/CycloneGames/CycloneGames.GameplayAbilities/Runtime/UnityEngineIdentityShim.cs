// Package-local copy of the Unity compatibility shims. Unity 6.6 replaced the 32-bit instance id with
// EntityId and later moved scene queries behind sort-free overloads, while 2022 LTS still exposes the
// legacy shape. A package carries this file so that no cross-package assembly reference is introduced;
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
            // which would break equality with the uint-truncated form used elsewhere.
            return unchecked((ulong)(uint)target.GetInstanceID());
#endif
        }
    }
}
