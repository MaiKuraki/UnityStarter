using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class ProbabilityBranch : CompositeNode
    {
        [SerializeField] private List<float> _probabilities = new List<float>();

        public override BTNode Clone()
        {
            var clone = (ProbabilityBranch)base.Clone();
            clone._probabilities = new List<float>(_probabilities);
            return clone;
        }

        protected override void CheckIntegrity()
        {
            base.CheckIntegrity();
            for (int i = _probabilities.Count; i < Children.Count; i++)
            {
                _probabilities.Add(1f);
            }

            if (_probabilities.Count > Children.Count)
            {
                _probabilities.RemoveRange(Children.Count, _probabilities.Count - Children.Count);
            }
        }

    }
}
