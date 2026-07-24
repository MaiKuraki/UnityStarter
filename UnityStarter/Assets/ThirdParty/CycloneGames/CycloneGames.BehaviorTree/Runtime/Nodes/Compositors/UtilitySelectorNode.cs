using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("UtilitySelector", "Scores each child through blackboard float keys and executes the highest-scoring child.")]
    public class UtilitySelectorNode : CompositeNode
    {
        [SerializeField, Tooltip("BB key names for child scores. scoreKeys[i] scores Children[i].")]
        [BehaviorTreeBlackboardKey(RuntimeBlackboardValueType.Float)]
        private List<string> _scoreKeys = new List<string>();
#if UNITY_EDITOR
        [System.NonSerialized] private Dictionary<BTNode, string> _scoreKeyByChild;
#endif

        public IReadOnlyList<string> ScoreKeys => _scoreKeys;

        public override BTNode Clone()
        {
            var clone = (UtilitySelectorNode)base.Clone();
            clone._scoreKeys = _scoreKeys != null
                ? new List<string>(_scoreKeys)
                : new List<string>();
            return clone;
        }

#if UNITY_EDITOR
        protected override void CaptureChildMetadata()
        {
            CaptureMetadataByChild(Children, _scoreKeys, ref _scoreKeyByChild);
        }

        protected override void RestoreChildMetadata()
        {
            RestoreMetadataByChild(Children, ref _scoreKeys, _scoreKeyByChild, string.Empty);
        }
#endif
    }
}
