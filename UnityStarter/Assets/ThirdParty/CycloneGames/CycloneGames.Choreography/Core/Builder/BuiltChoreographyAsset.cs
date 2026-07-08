using System;
using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Engine-free immutable choreography asset produced by <see cref="ChoreographyBuilder"/>.
    /// </summary>
    public sealed class BuiltChoreographyAsset : IChoreographyAsset
    {
        private static readonly ChoreographySection[] EmptySections = new ChoreographySection[0];
        private static readonly ChoreographyResourceReference[] EmptyResources = new ChoreographyResourceReference[0];

        private readonly ChoreographySection[] _sections;
        private readonly ChoreographyResourceReference[] _resources;

        public BuiltChoreographyAsset(string id, ChoreographySection[] sections)
        {
            Id = id ?? string.Empty;
            _sections = CopyOrEmpty(sections);
            TotalDuration = CalculateTotalDuration(_sections);
            _resources = BuildResourceList(_sections);
        }

        public string Id { get; }

        public double TotalDuration { get; }

        public IReadOnlyList<ChoreographySection> Sections => _sections;

        public int CollectResourceReferences(List<ChoreographyResourceReference> results)
        {
            if (results == null || _resources.Length == 0)
            {
                return 0;
            }

            int added = 0;
            for (int i = 0; i < _resources.Length; i++)
            {
                ChoreographyResourceReference reference = _resources[i];
                if (!Contains(results, reference))
                {
                    results.Add(reference);
                    added++;
                }
            }

            return added;
        }

        private static double CalculateTotalDuration(ChoreographySection[] sections)
        {
            double total = 0d;
            for (int i = 0; i < sections.Length; i++)
            {
                total += sections[i] != null ? sections[i].Duration : 0d;
            }

            return total;
        }

        private static ChoreographyResourceReference[] BuildResourceList(ChoreographySection[] sections)
        {
            List<ChoreographyResourceReference> resources = null;
            for (int s = 0; s < sections.Length; s++)
            {
                ChoreographySection section = sections[s];
                if (section == null)
                {
                    continue;
                }

                ChoreographyTrack[] tracks = section.Tracks;
                for (int t = 0; t < tracks.Length; t++)
                {
                    ChoreographyTrack track = tracks[t];
                    if (track == null)
                    {
                        continue;
                    }

                    ChoreographyClip[] clips = track.Clips;
                    for (int c = 0; c < clips.Length; c++)
                    {
                        ChoreographyClip clip = clips[c];
                        if (clip == null || !clip.Resource.IsValid)
                        {
                            continue;
                        }

                        if (resources == null)
                        {
                            resources = new List<ChoreographyResourceReference>(8);
                        }
                        if (!Contains(resources, clip.Resource))
                        {
                            resources.Add(clip.Resource);
                        }
                    }
                }
            }

            return resources != null ? resources.ToArray() : EmptyResources;
        }

        private static bool Contains(List<ChoreographyResourceReference> references, in ChoreographyResourceReference reference)
        {
            for (int i = 0; i < references.Count; i++)
            {
                if (references[i].Equals(reference))
                {
                    return true;
                }
            }

            return false;
        }

        private static ChoreographySection[] CopyOrEmpty(ChoreographySection[] sections)
        {
            if (sections == null || sections.Length == 0)
            {
                return EmptySections;
            }

            ChoreographySection[] copy = new ChoreographySection[sections.Length];
            Array.Copy(sections, copy, sections.Length);
            return copy;
        }
    }
}
