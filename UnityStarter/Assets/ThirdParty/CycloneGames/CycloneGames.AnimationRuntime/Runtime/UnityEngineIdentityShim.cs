// Unity 6.6 replaced the 32-bit instance id with EntityId. This package-local shim keeps the
// 2022 LTS and Unity 6 paths in one place without a cross-package assembly reference.
// An assembly with InternalsVisibleTo into another shim-carrying assembly must use inline
// version branches: a second copy of this internal type collides as CS0436.
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
