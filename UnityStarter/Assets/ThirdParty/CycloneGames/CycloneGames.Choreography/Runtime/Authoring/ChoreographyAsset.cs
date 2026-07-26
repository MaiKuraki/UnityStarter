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
    public sealed class ChoreographyAsset : ScriptableObject, IChoreographyAsset, IBoundedChoreographyResourceCollector
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

            TryCollectResourceReferences(
                results,
                int.MaxValue,
                int.MaxValue,
                out int addedCount,
                out _);
            return addedCount;
        }

        /// <inheritdoc />
        public bool TryCollectResourceReferences(
            List<ChoreographyResourceReference> results,
            int maximumResultCount,
            int maximumNodeScanCount,
            out int addedCount,
            out int scannedNodeCount)
        {
            addedCount = 0;
            scannedNodeCount = 0;
            if (results == null || maximumResultCount < 0 || maximumNodeScanCount < 0
                || results.Count > maximumResultCount)
            {
                return false;
            }

            _dedupeScratch.Clear();
            for (int i = 0; i < results.Count; i++)
            {
                _dedupeScratch.Add(results[i]);
            }

            if (!_built)
            {
                return TryCollectAuthoringReferences(
                    results,
                    maximumResultCount,
                    maximumNodeScanCount,
                    ref addedCount,
                    ref scannedNodeCount);
            }

            return TryCollectRuntimeReferences(
                results,
                maximumResultCount,
                maximumNodeScanCount,
                ref addedCount,
                ref scannedNodeCount);
        }

        private bool TryCollectAuthoringReferences(
            List<ChoreographyResourceReference> results,
            int maximumResultCount,
            int maximumNodeScanCount,
            ref int addedCount,
            ref int scannedNodeCount)
        {
            int sectionCount = Sections != null ? Sections.Count : 0;
            for (int s = 0; s < sectionCount; s++)
            {
                if (!TryConsumeNodeBudget(maximumNodeScanCount, ref scannedNodeCount))
                {
                    return false;
                }

                ChoreographySectionAuthoring section = Sections[s];
                if (section == null)
                {
                    continue;
                }

                for (int t = 0; t < section.TrackCount; t++)
                {
                    if (!TryConsumeNodeBudget(maximumNodeScanCount, ref scannedNodeCount))
                    {
                        return false;
                    }

                    ChoreographyTrackAuthoring track = section.GetTrack(t);
                    if (track == null)
                    {
                        continue;
                    }

                    for (int c = 0; c < track.ClipCount; c++)
                    {
                        if (!TryConsumeNodeBudget(maximumNodeScanCount, ref scannedNodeCount))
                        {
                            return false;
                        }

                        ChoreographyClipAuthoring clip = track.GetClip(c);
                        if (clip == null)
                        {
                            continue;
                        }

                        ChoreographyResourceReference reference = clip.ToRuntimeResourceReference();
                        if (!TryAppendReference(results, maximumResultCount, in reference, ref addedCount))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private bool TryCollectRuntimeReferences(
            List<ChoreographyResourceReference> results,
            int maximumResultCount,
            int maximumNodeScanCount,
            ref int addedCount,
            ref int scannedNodeCount)
        {
            for (int s = 0; s < _runtimeSections.Length; s++)
            {
                if (!TryConsumeNodeBudget(maximumNodeScanCount, ref scannedNodeCount))
                {
                    return false;
                }

                ChoreographySection section = _runtimeSections[s];
                if (section == null)
                {
                    continue;
                }

                ChoreographyTrack[] tracks = section.Tracks;
                for (int t = 0; t < tracks.Length; t++)
                {
                    if (!TryConsumeNodeBudget(maximumNodeScanCount, ref scannedNodeCount))
                    {
                        return false;
                    }

                    ChoreographyTrack track = tracks[t];
                    if (track == null)
                    {
                        continue;
                    }

                    ChoreographyClip[] clips = track.Clips;
                    for (int c = 0; c < clips.Length; c++)
                    {
                        if (!TryConsumeNodeBudget(maximumNodeScanCount, ref scannedNodeCount))
                        {
                            return false;
                        }

                        ChoreographyClip clip = clips[c];
                        if (clip == null)
                        {
                            continue;
                        }

                        ChoreographyResourceReference reference = clip.Resource;
                        if (!TryAppendReference(results, maximumResultCount, in reference, ref addedCount))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private bool TryAppendReference(
            List<ChoreographyResourceReference> results,
            int maximumResultCount,
            in ChoreographyResourceReference reference,
            ref int addedCount)
        {
            if (!reference.IsValid || !_dedupeScratch.Add(reference))
            {
                return true;
            }

            if (results.Count >= maximumResultCount)
            {
                return false;
            }

            results.Add(reference);
            addedCount++;
            return true;
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

        private static bool TryConsumeNodeBudget(int maximumNodeScanCount, ref int scannedNodeCount)
        {
            if (scannedNodeCount >= maximumNodeScanCount)
            {
                return false;
            }

            scannedNodeCount++;
            return true;
        }
    }
}
