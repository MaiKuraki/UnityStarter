using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("UtilitySelector", "Scores each child via blackboard float keys, executes the highest-scoring child. Perfect for Utility AI patterns.")]
    public class UtilitySelectorNode : CompositeNode
    {
        [SerializeField, Tooltip("BB key names for child scores. scoreKeys[i] scores Children[i].")]
        private List<string> _scoreKeys = new List<string>();

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
            {
                _scoreKeys.Add("");
            }

            if (_scoreKeys.Count > Children.Count)
            {
                _scoreKeys.RemoveRange(Children.Count, _scoreKeys.Count - Children.Count);
            }
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeUtilitySelector();
            node.GUID = GUID;

            var keyHashes = new int[_scoreKeys.Count];
            for (int i = 0; i < _scoreKeys.Count; i++)
            {
                keyHashes[i] = string.IsNullOrEmpty(_scoreKeys[i]) ? 0 : Animator.StringToHash(_scoreKeys[i]);
            }

            node.SetScoreKeys(keyHashes);
            AddRuntimeChildren(node);
            return node;
        }
    }
}
