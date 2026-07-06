using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Authoring container for a choreography. Designers edit sections/tracks/clips/events on this
    /// <see cref="ScriptableObject"/>; at runtime it builds an immutable, engine-free Core model once and caches
    /// it. The asset is treated as read-only playback data: the built model is never mutated during playback, and
    /// gameplay/runtime state must live in <see cref="ChoreographyPlayer"/>/<see cref="ChoreographyScheduler"/>,
    /// not on this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "ChoreographyAsset", menuName = "CycloneGames/Choreography/Choreography Asset")]
    public sealed class ChoreographyAsset : ScriptableObject, IChoreographyAsset
    {
        [Tooltip("Stable id used for diagnostics and lookups. Falls back to the asset name when empty.")]
        [SerializeField] private string AssetId;

        [SerializeField] private List<ChoreographySectionAuthoring> Sections = new List<ChoreographySectionAuthoring>();

        private ChoreographySection[] _runtimeSections;
        private readonly HashSet<ChoreographyResourceReference> _dedupeScratch = new HashSet<ChoreographyResourceReference>();
        private double _totalDuration;
        private bool _built;

        public string Id => string.IsNullOrEmpty(AssetId) ? name : AssetId;

        public double TotalDuration
        {
            get
            {
                EnsureBuilt();
                return _totalDuration;
            }
        }

        IReadOnlyList<ChoreographySection> IChoreographyAsset.Sections
        {
            get
            {
                EnsureBuilt();
                return _runtimeSections;
            }
        }

        public int CollectResourceReferences(List<ChoreographyResourceReference> results)
        {
            if (results == null)
            {
                return 0;
            }

            EnsureBuilt();

            _dedupeScratch.Clear();
            for (int i = 0; i < results.Count; i++)
            {
                _dedupeScratch.Add(results[i]);
            }

            int added = 0;
            for (int s = 0; s < _runtimeSections.Length; s++)
            {
                ChoreographyTrack[] tracks = _runtimeSections[s].Tracks;
                for (int t = 0; t < tracks.Length; t++)
                {
                    ChoreographyClip[] clips = tracks[t].Clips;
                    for (int c = 0; c < clips.Length; c++)
                    {
                        ChoreographyResourceReference reference = clips[c].Resource;
                        if (!reference.IsValid)
                        {
                            continue;
                        }
                        if (_dedupeScratch.Add(reference))
                        {
                            results.Add(reference);
                            added++;
                        }
                    }
                }
            }
            return added;
        }

        /// <summary>Forces a rebuild of the cached runtime model. Call after editing the asset at runtime (rare).</summary>
        public void RebuildRuntimeModel()
        {
            _built = false;
            EnsureBuilt();
        }

        private void OnEnable()
        {
            _built = false;
        }

        private void OnValidate()
        {
            _built = false;
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            int count = Sections != null ? Sections.Count : 0;
            _runtimeSections = count == 0 ? System.Array.Empty<ChoreographySection>() : new ChoreographySection[count];
            double total = 0d;
            for (int i = 0; i < count; i++)
            {
                ChoreographySection section = Sections[i].ToRuntime();
                _runtimeSections[i] = section;
                total += section.Duration;
            }
            _totalDuration = total;
            _built = true;
        }
    }
}
