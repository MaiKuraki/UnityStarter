using CycloneGames.BehaviorTree.Runtime.Attributes;
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

        public override BTNode Clone()
        {
            var clone = (ServiceNode)base.Clone();
            clone._interval = _interval;
            clone._randomDeviation = _randomDeviation;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

    }
}
