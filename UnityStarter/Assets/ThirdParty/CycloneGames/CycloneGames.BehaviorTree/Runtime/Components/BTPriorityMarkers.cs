using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    /// <summary>
    /// Base class for AI priority markers. Attach to GameObjects with BTRunnerComponent
    /// to override distance-based LOD with fixed priority.
    /// </summary>
    public abstract class BTPriorityMarkerBase : MonoBehaviour, IBTPriorityMarker
    {
        [SerializeField] protected int _priority = 0;
        [SerializeField] protected int _tickInterval = 1;

        public int Priority => _priority;
        public int TickInterval => _tickInterval;
    }

    /// <summary>
    /// Boss AI marker: Always uses highest priority (P0), ticks every frame.
    /// </summary>
    public class BossAIMarker : BTPriorityMarkerBase
    {
        private void Reset()
        {
            _priority = 0;
            _tickInterval = 1;
        }
    }

    /// <summary>
    /// Elite AI marker: Uses high priority (P0), ticks every frame.
    /// </summary>
    public class EliteAIMarker : BTPriorityMarkerBase
    {
        private void Reset()
        {
            _priority = 0;
            _tickInterval = 1;
        }
    }

    /// <summary>
    /// VIP NPC marker: Uses medium-high priority (P1), ticks every 2 frames.
    /// </summary>
    public class VIPNPCMarker : BTPriorityMarkerBase
    {
        private void Reset()
        {
            _priority = 1;
            _tickInterval = 2;
        }
    }
}
