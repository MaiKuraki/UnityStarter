using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("UtilitySelector", "Scores each child via blackboard float keys, executes the highest-scoring child. Perfect for Utility AI patterns.")]
    public class UtilitySelectorNode : CompositeNode
    {
        [SerializeField, Tooltip("BB key names for child scores. scoreKeys[i] scores Children[i].")]
        private List<string> _scoreKeys = new List<string>();

        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate) return BTState.SUCCESS;
                if (child.Evaluate(blackBoard) == BTState.SUCCESS) return BTState.SUCCESS;
            }
            return BTState.FAILURE;
        }

        protected override void OnStart(IBlackBoard blackBoard)
        {
            base.OnStart(blackBoard);
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;

            // Pick highest-scoring child
            float bestScore = float.MinValue;
            int bestIdx = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                float score = 0f;
                if (i < _scoreKeys.Count && !string.IsNullOrEmpty(_scoreKeys[i]))
                {
                    if (blackBoard.Contains(_scoreKeys[i]))
                    {
                        var val = blackBoard.Get(_scoreKeys[i]);
                        if (val is float f) score = f;
                        else if (val is int intVal) score = intVal;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            var child = Children[bestIdx];
            return child != null ? child.Run(blackBoard) : BTState.FAILURE;
        }

        public override BTNode Clone()
        {
            var clone = (UtilitySelectorNode)base.Clone();
            clone._scoreKeys = new List<string>(_scoreKeys);
            return clone;
        }

        protected override void CheckIntegrity()
        {
            base.CheckIntegrity();
            for (int i = _scoreKeys.Count; i < Children.Count; i++)
                _scoreKeys.Add("");
            if (_scoreKeys.Count > Children.Count)
                _scoreKeys.RemoveRange(Children.Count, _scoreKeys.Count - Children.Count);
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeUtilitySelector();
            node.GUID = GUID;

            int[] keyHashes = new int[_scoreKeys.Count];
            for (int i = 0; i < _scoreKeys.Count; i++)
                keyHashes[i] = string.IsNullOrEmpty(_scoreKeys[i]) ? 0 : UnityEngine.Animator.StringToHash(_scoreKeys[i]);
            node.SetScoreKeys(keyHashes);

            foreach (var child in Children)
            {
                if (child != null)
                    node.AddChild(child.CreateRuntimeNode());
            }
            return node;
        }
    }
}
