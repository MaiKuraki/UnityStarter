using System;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Provider-agnostic, immutable description of a resource or backend cue required by a choreography.
    /// The Core layer never assumes a concrete asset system, Unity type, audio middleware, or VFX runtime.
    /// </summary>
    public readonly struct ChoreographyResourceReference : IEquatable<ChoreographyResourceReference>
    {
        /// <summary>Opaque provider location key (e.g. an addressable key, path, or bank name).</summary>
        public readonly string Address;

        /// <summary>Resource classification used for preload routing and provider selection.</summary>
        public readonly ChoreographyResourceKind Kind;

        /// <summary>Optional grouping tag (e.g. a lifetime bucket). May be null.</summary>
        public readonly string Tag;

        /// <summary>Optional backend id such as UnityAudioClip, CycloneGames.Audio, Wwise, or a project-specific provider.</summary>
        public readonly string Provider;

        /// <summary>Optional backend group such as an audio bank, package, bundle, or collection id.</summary>
        public readonly string Group;

        public ChoreographyResourceReference(
            string address,
            ChoreographyResourceKind kind,
            string tag = null,
            string provider = null,
            string group = null)
        {
            Address = address;
            Kind = kind;
            Tag = tag;
            Provider = provider;
            Group = group;
        }

        public bool IsValid => !string.IsNullOrEmpty(Address);

        public bool Equals(ChoreographyResourceReference other)
        {
            return Kind == other.Kind
                && string.Equals(Address, other.Address, StringComparison.Ordinal)
                && string.Equals(Tag, other.Tag, StringComparison.Ordinal)
                && string.Equals(Provider, other.Provider, StringComparison.Ordinal)
                && string.Equals(Group, other.Group, StringComparison.Ordinal);
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
                hash = (hash * 16777619) ^ (Provider != null ? Provider.GetHashCode() : 0);
                hash = (hash * 16777619) ^ (Group != null ? Group.GetHashCode() : 0);
                return hash;
            }
        }
    }
}
