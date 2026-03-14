using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class ProbabilityBranch : CompositeNode
    {
        [SerializeField] private List<float> _probabilities = new List<float>();
        private int _currentBranch = 0;

        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            if (Children.Count == 0 || _currentBranch < 0 || _currentBranch >= Children.Count)
                return BTState.FAILURE;

            if (!Children[_currentBranch].CanReEvaluate) return BTState.SUCCESS;
            return Children[_currentBranch].Evaluate(blackBoard);
        }

        protected override void OnStart(IBlackBoard blackBoard)
        {
            base.OnStart(blackBoard);
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].IsStarted = false;
            }

            var totalProbability = 0f;
            for (int i = 0; i < _probabilities.Count; i++)
            {
                totalProbability += _probabilities[i];
            }
            var random = Random.Range(0f, totalProbability);
            var currentProbability = 0f;
            for (int i = 0; i < _probabilities.Count; i++)
            {
                currentProbability += _probabilities[i];
                if (random <= currentProbability)
                {
                    _currentBranch = i;
                    break;
                }
            }
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            if (Children.Count == 0 || _currentBranch < 0 || _currentBranch >= Children.Count)
                return BTState.FAILURE;

            return Children[_currentBranch].Run(blackBoard);
        }

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

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeProbabilityBranch();
            node.GUID = GUID;

            // Pass weights so runtime can re-select each activation
            float[] weights = new float[_probabilities.Count];
            for (int i = 0; i < _probabilities.Count; i++)
                weights[i] = _probabilities[i];
            node.SetWeights(weights);

            foreach (var child in Children)
            {
                if (child != null)
                {
                    node.AddChild(child.CreateRuntimeNode());
                }
            }
            return node;
        }
    }
}