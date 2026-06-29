using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Owns attribute sets, registered attributes, and the dirty attribute queue used by aggregation.
    /// </summary>
    public sealed class AttributeAggregator
    {
        private readonly List<AttributeSet> _attributeSets;
        private readonly Dictionary<string, GameplayAttribute> _attributes;
        private readonly List<GameplayAttribute> _dirtyAttributes;

        public AttributeAggregator(int setCapacity = 4, int attributeCapacity = 32)
        {
            _attributeSets = new List<AttributeSet>(setCapacity);
            _attributes = new Dictionary<string, GameplayAttribute>(attributeCapacity);
            _dirtyAttributes = new List<GameplayAttribute>(attributeCapacity);
        }

        public IReadOnlyList<AttributeSet> AttributeSets => _attributeSets;
        public int AttributeSetCount => _attributeSets.Count;
        public int AttributeCount => _attributes.Count;
        public int DirtyAttributeCount => _dirtyAttributes.Count;

        internal List<AttributeSet> MutableAttributeSets => _attributeSets;
        internal Dictionary<string, GameplayAttribute> MutableAttributes => _attributes;
        internal List<GameplayAttribute> MutableDirtyAttributes => _dirtyAttributes;

        public void Reserve(int setCapacity, int attributeCapacity)
        {
            if (setCapacity > _attributeSets.Capacity)
            {
                _attributeSets.Capacity = setCapacity;
            }

            if (attributeCapacity > _dirtyAttributes.Capacity)
            {
                _dirtyAttributes.Capacity = attributeCapacity;
            }

            if (attributeCapacity > 0)
            {
                _attributes.EnsureCapacity(attributeCapacity);
            }
        }

        public bool TryGetAttribute(string name, out GameplayAttribute attribute)
        {
            return _attributes.TryGetValue(name, out attribute);
        }
    }
}
