using System;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Provider-agnostic, immutable description of a resource required by a choreography.
    /// The <see cref="Address"/> is an opaque location key resolved by an <see cref="IResourceProvider"/>;
    /// the Core layer never assumes a concrete asset system or Unity type.
    /// </summary>
    public readonly struct ChoreographyResourceReference : IEquatable<ChoreographyResourceReference>
    {
        /// <summary>Opaque provider location key (e.g. an addressable key, path, or bank name).</summary>
        public readonly string Address;

        /// <summary>Resource classification used for preload routing and provider selection.</summary>
        public readonly ChoreographyResourceKind Kind;

        /// <summary>Optional grouping tag (e.g. a lifetime bucket). May be null.</summary>
        public readonly string Tag;

        public ChoreographyResourceReference(string address, ChoreographyResourceKind kind, string tag = null)
        {
            Address = address;
            Kind = kind;
            Tag = tag;
        }

        public bool IsValid => !string.IsNullOrEmpty(Address);

        public bool Equals(ChoreographyResourceReference other)
        {
            return Kind == other.Kind
                && string.Equals(Address, other.Address, StringComparison.Ordinal)
                && string.Equals(Tag, other.Tag, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ChoreographyResourceReference other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)2166136261;
                hash = (hash * 16777619) ^ (Address != null ? Address.GetHashCode() : 0);
                hash = (hash * 16777619) ^ (int)Kind;
                hash = (hash * 16777619) ^ (Tag != null ? Tag.GetHashCode() : 0);
                return hash;
            }
        }
    }
}
