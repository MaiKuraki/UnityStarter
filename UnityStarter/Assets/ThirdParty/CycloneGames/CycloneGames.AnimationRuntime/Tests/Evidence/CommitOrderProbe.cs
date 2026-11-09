using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Records the commit counter as seen by a script that keeps the default execution order, once in Update and
    /// once in LateUpdate. Comparing the two readings against the previous frame localises the commit to a phase.
    /// </summary>
    public sealed class CommitOrderProbe : MonoBehaviour
    {
        internal AnimationRuntimeBehaviour Behaviour;
        internal int CommitsInUpdate = -1;
        internal int CommitsInLateUpdate = -1;

        private void Update()
        {
            CommitsInUpdate = Read();
        }

        private void LateUpdate()
        {
            CommitsInLateUpdate = Read();
        }

        private int Read()
        {
            AnimationRuntimeGraph graph = Behaviour != null ? Behaviour.Graph : null;
            return graph != null ? (int)graph.GetStats().Commits : 0;
        }
    }
}
