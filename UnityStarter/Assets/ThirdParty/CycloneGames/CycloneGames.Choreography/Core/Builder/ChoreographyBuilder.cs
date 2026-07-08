using System;
using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Code-first authoring API for programmatically generated choreography assets.
    /// </summary>
    public sealed class ChoreographyBuilder
    {
        private readonly string _assetId;
        private readonly List<SectionBuilder> _sections = new List<SectionBuilder>(4);

        public ChoreographyBuilder(string assetId)
        {
            _assetId = assetId ?? string.Empty;
        }

        public SectionBuilder Section(string id, double duration)
        {
            SectionBuilder section = new SectionBuilder(id, duration);
            _sections.Add(section);
            return section;
        }

        public ChoreographyBuilder Section(string id, double duration, Action<SectionBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            SectionBuilder section = Section(id, duration);
            configure(section);
            return this;
        }

        public IChoreographyAsset Build()
        {
            ChoreographySection[] sections = new ChoreographySection[_sections.Count];
            for (int i = 0; i < _sections.Count; i++)
            {
                sections[i] = _sections[i].Build();
            }

            return new BuiltChoreographyAsset(_assetId, sections);
        }
    }

}
