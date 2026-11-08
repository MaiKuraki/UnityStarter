using System;

namespace CycloneGames.BehaviorTree.Core
{
    /// <summary>
    /// Allocation-free callback-based state-change observer for runtime debugging and replay.
    /// </summary>
    public sealed class BTStatusObserver
    {
        public delegate void StatusChangeHandler(string nodeGuid, RuntimeState previousState, RuntimeState newState, double timestamp);

        private StatusChangeHandler _handler;
        private bool _enabled;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public BTStatusObserver(StatusChangeHandler handler)
        {
            _handler = handler;
            _enabled = true;
        }

        public void NotifyStatusChanged(string nodeGuid, RuntimeState previousState, RuntimeState newState, double timestamp)
        {
            if (!_enabled || _handler == null) return;
            _handler(nodeGuid, previousState, newState, timestamp);
        }

        /// <summary>
        /// Wraps a RuntimeNode to observe state changes; zero overhead when not attached.
        /// </summary>
        public static void AttachToTree(RuntimeBehaviorTree tree, StatusChangeHandler handler)
        {
#if DEBUG
            if (tree == null || handler == null) return;
            tree.StatusObserver = new BTStatusObserver(handler);
#endif
        }
    }
}
