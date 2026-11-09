using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Process-wide counters for live graphs and voices, plus the one-graph-per-Animator ownership table.
    /// Diagnostics only; it never owns or frees animation state. Main-thread only.
    /// </summary>
    public static class AnimationRuntimeRegistry
    {
        private static int _liveGraphs;
        private static int _liveVoices;

        // Weak keys: a destroyed Animator must not keep its graph alive through this table.
        private static ConditionalWeakTable<Animator, AnimationRuntimeGraph> _owners =
            new ConditionalWeakTable<Animator, AnimationRuntimeGraph>();

        /// <summary>Graphs that have been initialized and not yet disposed.</summary>
        public static int LiveGraphCount => _liveGraphs;

        /// <summary>Voices currently in use across every live graph.</summary>
        public static int LiveVoiceCount => _liveVoices;

        /// <summary>Whether an Animator is already driven by a graph. One Animator supports one animation output.</summary>
        public static bool IsAnimatorOwned(Animator animator)
        {
            return animator != null && _owners.TryGetValue(animator, out AnimationRuntimeGraph existing) && existing != null;
        }

        internal static bool TryClaimAnimator(Animator animator, AnimationRuntimeGraph graph)
        {
            if (animator == null || graph == null)
            {
                return false;
            }

            if (_owners.TryGetValue(animator, out AnimationRuntimeGraph existing) && existing != null)
            {
                return false;
            }

            _owners.Add(animator, graph);
            return true;
        }

        internal static void ReleaseAnimator(Animator animator, AnimationRuntimeGraph graph)
        {
            if (animator == null)
            {
                return;
            }

            if (_owners.TryGetValue(animator, out AnimationRuntimeGraph existing) && ReferenceEquals(existing, graph))
            {
                _owners.Remove(animator);
            }
        }

        internal static void OnGraphCreated()
        {
            _liveGraphs++;
        }

        internal static void OnGraphDisposed()
        {
            if (_liveGraphs > 0)
            {
                _liveGraphs--;
            }
        }

        internal static void OnVoiceCountChanged(int delta)
        {
            _liveVoices += delta;
            if (_liveVoices < 0)
            {
                _liveVoices = 0;
            }
        }

        /// <summary>
        /// Zeroes the counters on every play session. Without this, disabling Domain Reload leaves stale counts
        /// from the previous session and every leak assertion fails for the wrong reason.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSession()
        {
            _liveGraphs = 0;
            _liveVoices = 0;
            _owners = new ConditionalWeakTable<Animator, AnimationRuntimeGraph>();
        }
    }
}

