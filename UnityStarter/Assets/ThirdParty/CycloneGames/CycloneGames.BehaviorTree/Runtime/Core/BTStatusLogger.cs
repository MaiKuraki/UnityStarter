using System;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Lightweight status change observer for runtime debugging and replay.
    /// Designed for 0GC in release builds: callback-based, no allocation.
    /// Use #if UNITY_EDITOR || DEVELOPMENT_BUILD to gate logging in shipping builds.
    /// </summary>
    public class BTStatusLogger
    {
        public delegate void StatusChangeHandler(string nodeGuid, RuntimeState previousState, RuntimeState newState, float timestamp);

        private StatusChangeHandler _handler;
        private bool _enabled;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public BTStatusLogger(StatusChangeHandler handler)
        {
            _handler = handler;
            _enabled = true;
        }

        public void Log(string nodeGuid, RuntimeState previousState, RuntimeState newState, float timestamp)
        {
            if (!_enabled || _handler == null) return;
            _handler(nodeGuid, previousState, newState, timestamp);
        }

        /// <summary>
        /// Wraps a RuntimeNode to intercept state changes for logging.
        /// Use for development builds only; zero overhead when not attached.
        /// </summary>
        public static void AttachToTree(RuntimeBehaviorTree tree, StatusChangeHandler handler)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (tree == null || handler == null) return;
            tree.StatusLogger = new BTStatusLogger(handler);
#endif
        }
    }
}
