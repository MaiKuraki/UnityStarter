using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class ProbabilityBranch : CompositeNode
    {
        [SerializeField] private List<float> _probabilities = new List<float>();
#if UNITY_EDITOR
        [System.NonSerialized] private Dictionary<BTNode, float> _probabilityByChild;
#endif

        public IReadOnlyList<float> Probabilities => _probabilities;

        public override BTNode Clone()
        {
            var clone = (ProbabilityBranch)base.Clone();
            clone._probabilities = _probabilities != null
                ? new List<float>(_probabilities)
                : new List<float>();
            return clone;
        }

#if UNITY_EDITOR
        protected override void CaptureChildMetadata()
        {
            CaptureMetadataByChild(Children, _probabilities, ref _probabilityByChild);
        }

        protected override void RestoreChildMetadata()
        {
            RestoreMetadataByChild(Children, ref _probabilities, _probabilityByChild, 1f);
        }
#endif
    }
}
