using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    /// <summary>
    /// Unreal-style Service node (ScriptableObject layer).
    /// Attaches to any branch and periodically runs side-effect logic
    /// (perception updates, target selection, etc.) while the child is active.
    /// </summary>
    [BTInfo("Service", "Periodically executes side-effect logic (perception updates, target selection, etc.) while the child node is active. Configurable interval with optional random deviation.")]
    public class ServiceNode : DecoratorNode
    {
        [SerializeField] private float _interval = 0.5f;
        [SerializeField] private float _randomDeviation = 0f;
        [SerializeField] private bool _useUnscaledTime = false;

        private float _lastServiceTime;

        protected override void OnStart(IBlackBoard blackBoard)
        {
            _lastServiceTime = GetTime();
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (Child == null) return BTState.FAILURE;

            float now = GetTime();
            if (now - _lastServiceTime >= _interval)
            {
                _lastServiceTime = now;
            }

            return Child.Run(blackBoard);
        }

        private float GetTime()
        {
            return _useUnscaledTime ? Time.unscaledTime : Time.time;
        }

        public override BTNode Clone()
        {
            var clone = (ServiceNode)base.Clone();
            clone._interval = _interval;
            clone._randomDeviation = _randomDeviation;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeServiceNode();
            node.GUID = GUID;
            node.Interval = _interval;
            node.RandomDeviation = _randomDeviation;
            node.UseUnscaledTime = _useUnscaledTime;
            if (Child != null)
            {
                node.Child = Child.CreateRuntimeNode();
            }
            return node;
        }
    }
}
