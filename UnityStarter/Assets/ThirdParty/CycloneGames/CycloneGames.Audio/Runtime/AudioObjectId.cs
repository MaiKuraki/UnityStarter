// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Supplies a session-scoped identity for an audio authoring asset.
    /// </summary>
    internal interface IAudioIdentified
    {
        /// <summary>Non-zero identity, assigned on first access and never reused.</summary>
        int RuntimeId { get; }
    }

    /// <summary>
    /// Resolves session-scoped identities used as audio registry keys.
    /// </summary>
    /// <remarks>
    /// Unity instance IDs are not durable keys: newer editors retire the API and recycle the value
    /// after an object is destroyed. Audio assets cache their identity in a non-serialized field;
    /// every other object resolves through a weak, reference-keyed table. Identities are
    /// process-scoped, are never reused, and must not be persisted.
    /// </remarks>
    internal static class AudioObjectId
    {
        private sealed class ForeignId
        {
            internal readonly int Value;

            internal ForeignId(int value)
            {
                Value = value;
            }
        }

        // ConditionalWeakTable compares keys by reference, so destroyed-object state and Unity's
        // overridden equality never influence resolution.
        private static readonly ConditionalWeakTable<Object, ForeignId> foreignIds =
            new ConditionalWeakTable<Object, ForeignId>();

        private static readonly ConditionalWeakTable<Object, ForeignId>.CreateValueCallback createForeignId =
            _ => new ForeignId(Next());

        private static int lastIssuedId;

        /// <summary>Reserves the next identity. Never returns zero.</summary>
        internal static int Next()
        {
            unchecked
            {
                int id = Interlocked.Increment(ref lastIssuedId);
                if (id == 0)
                {
                    id = Interlocked.Increment(ref lastIssuedId);
                }

                return id;
            }
        }

        /// <summary>Returns the identity in <paramref name="slot"/>, assigning one on first access.</summary>
        internal static int Ensure(ref int slot)
        {
            int id = slot;
            if (id != 0)
            {
                return id;
            }

            id = Next();
            slot = id;
            return id;
        }

        /// <summary>Resolves the identity of <paramref name="value"/>; returns zero for a null reference.</summary>
        internal static int Of(Object value)
        {
            if (ReferenceEquals(value, null))
            {
                return 0;
            }

            if (value is IAudioIdentified identified)
            {
                return identified.RuntimeId;
            }

            return foreignIds.GetValue(value, createForeignId).Value;
        }
    }
}
