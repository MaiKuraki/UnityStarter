using System;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Stable network-visible identity for an AbilitySystemComponent.
    /// Deliberately an int rather than an object reference — int values are safe to
    /// serialize, compare across network boundaries, and use in headless server contexts
    /// where Unity objects do not exist.
    /// Value 0 means invalid/null.
    /// </summary>
    public readonly struct GASEntityId : IEquatable<GASEntityId>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASEntityId(int value)
        {
            Value = value;
        }

        public bool Equals(GASEntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASEntityId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASEntityId left, GASEntityId right) => left.Equals(right);
        public static bool operator !=(GASEntityId left, GASEntityId right) => !left.Equals(right);
    }

    /// <summary>
    /// Numeric identity for a registered ability/effect/cue definition.
    /// Assigned by the <see cref="IGASDefinitionRegistry"/> when a definition is
    /// registered. Value 0 means invalid/unregistered.
    /// </summary>
    public readonly struct GASDefinitionId : IEquatable<GASDefinitionId>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASDefinitionId(int value)
        {
            Value = value;
        }

        public bool Equals(GASDefinitionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASDefinitionId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASDefinitionId left, GASDefinitionId right) => left.Equals(right);
        public static bool operator !=(GASDefinitionId left, GASDefinitionId right) => !left.Equals(right);
    }

    public enum GASDefinitionKind : byte
    {
        Unknown,
        Ability,
        Effect,
        Cue
    }

    /// <summary>
    /// A kind-tagged definition ID with a stable content hash.
    /// The content hash enables version-aware reconciliation: the server can detect
    /// when a client's ability/effect definition differs and trigger a content update
    /// or reject a mismatched predicted activation.
    /// </summary>
    public readonly struct GASDefinitionVersion
    {
        public readonly GASDefinitionKind Kind;
        public readonly GASDefinitionId Id;
        public readonly uint ContentHash;

        public GASDefinitionVersion(GASDefinitionKind kind, GASDefinitionId id, uint contentHash)
        {
            Kind = kind;
            Id = id;
            ContentHash = contentHash;
        }
    }

    /// <summary>
    /// Handle to a granted ability spec within an ASC's state.
    /// Allocated by <see cref="GASAbilitySystemState.GrantAbility"/>,
    /// invalidated on removal. Pass this handle — not the definition — to
    /// activation methods, since the same definition may be granted multiple times.
    /// Value 0 means invalid.
    /// </summary>
    public readonly struct GASSpecHandle : IEquatable<GASSpecHandle>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASSpecHandle(int value)
        {
            Value = value;
        }

        public bool Equals(GASSpecHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASSpecHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASSpecHandle left, GASSpecHandle right) => left.Equals(right);
        public static bool operator !=(GASSpecHandle left, GASSpecHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Handle to an active effect instance on an ASC.
    /// Allocated by <see cref="GASAbilitySystemState.AddActiveEffect"/>,
    /// invalidated when the effect is removed or expires. Use this handle
    /// to query, refresh, or remove a specific active effect.
    /// Value 0 means invalid.
    /// </summary>
    public readonly struct GASActiveEffectHandle : IEquatable<GASActiveEffectHandle>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASActiveEffectHandle(int value)
        {
            Value = value;
        }

        public bool Equals(GASActiveEffectHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASActiveEffectHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASActiveEffectHandle left, GASActiveEffectHandle right) => left.Equals(right);
        public static bool operator !=(GASActiveEffectHandle left, GASActiveEffectHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Stable numeric identity for a gameplay attribute.
    /// Assigned by the <see cref="IGASAttributeRegistry"/> when an attribute is
    /// registered by name. Using int IDs rather than strings in the hot path
    /// avoids heap allocations and enables fast hash-based lookups.
    /// Value 0 means invalid/unregistered.
    /// </summary>
    public readonly struct GASAttributeId : IEquatable<GASAttributeId>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASAttributeId(int value)
        {
            Value = value;
        }

        public bool Equals(GASAttributeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASAttributeId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASAttributeId left, GASAttributeId right) => left.Equals(right);
        public static bool operator !=(GASAttributeId left, GASAttributeId right) => !left.Equals(right);
    }

    /// <summary>
    /// Represents a point in fixed-timestep simulation time.
    /// Separate from floating-point delta time so effect durations, cooldowns, and
    /// periodic ticks are deterministic across clients and server — critical for
    /// lockstep or replay-based networking models.
    /// </summary>
    public readonly struct GASFixedTime
    {
        public readonly int Tick;
        public readonly int TickRate;

        public GASFixedTime(int tick, int tickRate)
        {
            Tick = tick;
            TickRate = tickRate > 0 ? tickRate : 1;
        }
    }
}
