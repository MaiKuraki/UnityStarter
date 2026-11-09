using System;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>How an <see cref="AnimationClipReference"/> points at its clip.</summary>
    public enum AnimationClipReferenceMode : byte
    {
        /// <summary>A direct Unity asset reference. Simplest for small projects and prototypes.</summary>
        Embedded = 0,

        /// <summary>
        /// An opaque key interpreted by the host's resolver. It may be an asset address, a project-relative
        /// path, a data-table key, or anything else the resolver understands. The runtime never interprets it.
        /// </summary>
        Location = 1
    }

    /// <summary>
    /// Authoring reference to an animation clip: a mode plus the data for that mode, with a fallback to the
    /// location when the embedded clip is missing.
    ///
    /// The location is an opaque key. Its meaning is defined by the installed
    /// <see cref="IAnimationClipResolver"/>; the runtime never loads or interprets it.
    /// </summary>
    [Serializable]
    public struct AnimationClipReference : IEquatable<AnimationClipReference>
    {
        [SerializeField] private AnimationClipReferenceMode _mode;
        [SerializeField] private AnimationClip _embeddedClip;
        [SerializeField] private string _location;

        [Tooltip("Editor-only metadata used to relocate the asset when the location changes. Never read at runtime.")]
        [SerializeField] private string _editorGuid;

        public AnimationClipReference(AnimationClipReferenceMode mode, AnimationClip embeddedClip, string location, string editorGuid = null)
        {
            _mode = mode;
            _embeddedClip = embeddedClip;
            _location = location;
            _editorGuid = editorGuid;
        }

        public AnimationClipReference(AnimationClip embeddedClip)
            : this(AnimationClipReferenceMode.Embedded, embeddedClip, null)
        {
        }

        public AnimationClipReference(string location, string editorGuid = null)
            : this(AnimationClipReferenceMode.Location, null, location, editorGuid)
        {
        }

        public AnimationClipReferenceMode Mode => _mode;

        public AnimationClip EmbeddedClip => _embeddedClip;

        /// <summary>Address, path or data-table key. Meaning is defined by the installed resolver.</summary>
        public string Location => _location ?? string.Empty;

        /// <summary>Editor-only. Must never be used as a runtime key.</summary>
        public string EditorGuid => _editorGuid ?? string.Empty;

        /// <summary>
        /// Mode actually in effect. Mirrors the audio reference behaviour: if the asset is set to
        /// <see cref="AnimationClipReferenceMode.Embedded"/> but the clip is missing while a location exists,
        /// the location wins so a reference whose embedded clip is missing but has a location still resolves.
        /// </summary>
        public AnimationClipReferenceMode EffectiveMode
        {
            get
            {
                if (_mode == AnimationClipReferenceMode.Embedded && _embeddedClip == null && !string.IsNullOrWhiteSpace(_location))
                {
                    return AnimationClipReferenceMode.Location;
                }

                return _mode;
            }
        }

        public bool IsValid
        {
            get
            {
                return EffectiveMode == AnimationClipReferenceMode.Embedded
                    ? _embeddedClip != null
                    : !string.IsNullOrWhiteSpace(_location);
            }
        }

        public bool TryGetEmbeddedClip(out AnimationClip clip)
        {
            clip = _embeddedClip;
            return clip != null;
        }

        public static AnimationClipReference FromClip(AnimationClip clip)
        {
            return new AnimationClipReference(clip);
        }

        public static AnimationClipReference FromLocation(string location, string editorGuid = null)
        {
            return new AnimationClipReference(location, editorGuid);
        }

        public bool Equals(AnimationClipReference other)
        {
            if (EffectiveMode != other.EffectiveMode)
            {
                return false;
            }

            if (EffectiveMode == AnimationClipReferenceMode.Embedded)
            {
                return _embeddedClip == other._embeddedClip;
            }

            return string.Equals(_location, other._location, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationClipReference other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                if (EffectiveMode == AnimationClipReferenceMode.Embedded)
                {
                    return _embeddedClip != null ? _embeddedClip.GetInstanceID() : 0;
                }

                return _location != null ? StringComparer.Ordinal.GetHashCode(_location) : 0;
            }
        }

        public static bool operator ==(AnimationClipReference left, AnimationClipReference right) => left.Equals(right);

        public static bool operator !=(AnimationClipReference left, AnimationClipReference right) => !left.Equals(right);

        public override string ToString()
        {
            return EffectiveMode == AnimationClipReferenceMode.Embedded
                ? "Clip(" + (_embeddedClip != null ? _embeddedClip.name : "null") + ")"
                : "Clip(@" + Location + ")";
        }
    }
}
