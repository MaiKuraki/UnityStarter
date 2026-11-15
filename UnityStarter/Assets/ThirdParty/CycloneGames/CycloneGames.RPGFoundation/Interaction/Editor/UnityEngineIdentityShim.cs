// Package-local Unity compatibility shims. Each package keeps its own copy so no cross-package
// assembly reference is introduced, and includes only the shims it uses.
//
// Call sites branch on UNITY_6000_0_OR_NEWER: Unity 6 replaced the 32-bit instance id with EntityId
// and moved scene queries to sort-free overloads. A package with InternalsVisibleTo into another
// copy must use inline branches instead of a second copy, since duplicate internal types collide
// as CS0436.
using UnityEngine;

namespace CycloneGames.Compat
{
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
