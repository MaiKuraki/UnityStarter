using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Counts <c>OnAnimatorMove</c> deliveries. The animation message is sent to the GameObject that owns the
    /// Animator, so a probe on the parent is expected to stay at zero.
    /// </summary>
    public sealed class RootMotionProbe : MonoBehaviour
    {
        internal int Calls;
        internal int Frames;

        private void Update()
        {
            Frames++;
        }

        private void OnAnimatorMove()
        {
            Calls++;
        }
    }
}
