using UnityEngine;
using CycloneGames.BehaviorTree.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public abstract class BTPriorityMarkerBase : MonoBehaviour, IBTPriorityMarker
    {
        [SerializeField] protected int _priority = 0;
        [SerializeField] protected int _tickInterval = 1;

        public int Priority => _priority;
        public int TickInterval => _tickInterval;
    }

    public class BossAIMarker : BTPriorityMarkerBase
    {
        private void Reset()
        {
            _priority = 0;
            _tickInterval = 1;
        }
    }

    public class EliteAIMarker : BTPriorityMarkerBase
    {
        private void Reset()
        {
            _priority = 0;
            _tickInterval = 1;
        }
    }

    public class VIPNPCMarker : BTPriorityMarkerBase
    {
        private void Reset()
        {
            _priority = 1;
            _tickInterval = 2;
        }
    }
}
