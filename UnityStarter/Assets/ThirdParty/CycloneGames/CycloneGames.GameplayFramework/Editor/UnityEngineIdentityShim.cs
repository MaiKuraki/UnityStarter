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
            return unchecked((ulong)target.GetInstanceID());
#endif
        }
    }

    /// <summary>
    /// Returns every loaded object of a type, optionally including inactive ones. Results are unordered.
    /// </summary>
    internal static class UnityObjectQuery
    {
        internal static T[] FindAll<T>(bool includeInactive) where T : Object
        {
            FindObjectsInactive inactive = includeInactive
                ? FindObjectsInactive.Include
                : FindObjectsInactive.Exclude;

#if UNITY_6000_6_OR_NEWER
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }
    }
}
