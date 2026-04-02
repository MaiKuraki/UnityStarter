using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Production-grade implementation of IUINavigationService.
    ///
    /// THREADING MODEL:
    ///   Mutation methods (Register, Unregister, Clear) must be called on the Unity main thread.
    ///   Query methods snapshot the graph under a ReaderWriterLockSlim so they are safe from any thread.
    ///
    /// MEMORY:
    ///   Each node is a small class object allocated once per window open.
    ///   The implementation pools nothing — UIManager window lifetimes are long relative to GC pressure.
    ///   Query results are freshly allocated List<T> snapshots; callers may cache them if needed.
    /// </summary>
    public sealed class UINavigationService : IUINavigationService
    {
        private const string LOG_TAG = "[UINavigation]";

        // Directed graph: windowName → Node
        private readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>(16);

        // Insertion-ordered list for GetHistory()
        private readonly List<UINavigationEntry> _insertionOrder = new List<UINavigationEntry>(16);

        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private bool _disposed;

        // ── Internal node ──────────────────────────────────────────────────────

        private sealed class Node
        {
            public string WindowName;
            public string OpenerName;   // may become null after Reparent
            public object Context;
            // Children tracked to support policy-driven teardown
            public readonly List<string> Children = new List<string>(4);
        }

        // ── IUINavigationService ───────────────────────────────────────────────

        public string CurrentWindow
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    // Walk insertion order in reverse to find the last registered live window
                    for (int i = _insertionOrder.Count - 1; i >= 0; i--)
                    {
                        string name = _insertionOrder[i].WindowName;
                        if (_nodes.ContainsKey(name)) return name;
                    }
                    return null;
                }
                finally { _lock.ExitReadLock(); }
            }
        }

        public bool CanNavigateBack
        {
            get
            {
                string current = CurrentWindow;
                if (current == null) return false;
                return ResolveBackTarget(current) != null;
            }
        }

        public void Register(string windowName, string openerName = null, object context = null)
        {
            if (string.IsNullOrEmpty(windowName)) return;

            _lock.EnterWriteLock();
            try
            {
                if (_nodes.ContainsKey(windowName))
                {
                    CLogger.LogWarning($"{LOG_TAG} '{windowName}' is already registered. Skipping.");
                    return;
                }

                var node = new Node
                {
                    WindowName = windowName,
                    OpenerName = openerName,
                    Context    = context,
                };
                _nodes[windowName] = node;
                _insertionOrder.Add(new UINavigationEntry(windowName, openerName, context));

                // Register this window as a child of its opener
                if (!string.IsNullOrEmpty(openerName) && _nodes.TryGetValue(openerName, out var openerNode))
                {
                    if (!openerNode.Children.Contains(windowName))
                        openerNode.Children.Add(windowName);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Unregister(string windowName, ChildClosePolicy policy = ChildClosePolicy.Reparent)
        {
            if (string.IsNullOrEmpty(windowName)) return;

            _lock.EnterWriteLock();
            try
            {
                UnregisterInternal(windowName, policy);
            }
            finally { _lock.ExitWriteLock(); }
        }

        // Recursive internal — must be called while holding the write-lock.
        private void UnregisterInternal(string windowName, ChildClosePolicy policy)
        {
            if (!_nodes.TryGetValue(windowName, out var node)) return;

            string grandparentName = node.OpenerName;

            // Process children before removing this node
            // Iterate over a copy because Cascade recurses and modifies _nodes
            var childrenSnapshot = new List<string>(node.Children);
            foreach (string childName in childrenSnapshot)
            {
                if (!_nodes.ContainsKey(childName)) continue;

                switch (policy)
                {
                    case ChildClosePolicy.Reparent:
                        // Reconnect child to grandparent (may be null → becomes a root)
                        _nodes[childName].OpenerName = grandparentName;
                        if (!string.IsNullOrEmpty(grandparentName) && _nodes.TryGetValue(grandparentName, out var gpNode))
                        {
                            if (!gpNode.Children.Contains(childName))
                                gpNode.Children.Add(childName);
                        }
                        break;

                    case ChildClosePolicy.Cascade:
                        // Recursively destroy all descendants
                        UnregisterInternal(childName, ChildClosePolicy.Cascade);
                        break;

                    case ChildClosePolicy.Detach:
                        // Children become roots (no back target)
                        _nodes[childName].OpenerName = null;
                        break;
                }
            }

            // Remove this node from its opener's child list
            if (!string.IsNullOrEmpty(grandparentName) && _nodes.TryGetValue(grandparentName, out var parentNode))
            {
                parentNode.Children.Remove(windowName);
            }

            _nodes.Remove(windowName);
            // Remove from insertion-order list (linear scan is acceptable — window count is bounded)
            for (int i = _insertionOrder.Count - 1; i >= 0; i--)
            {
                if (_insertionOrder[i].WindowName == windowName)
                {
                    _insertionOrder.RemoveAt(i);
                    break;
                }
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _nodes.Clear();
                _insertionOrder.Clear();
            }
            finally { _lock.ExitWriteLock(); }
        }

        public string GetOpener(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return null;
            _lock.EnterReadLock();
            try
            {
                return _nodes.TryGetValue(windowName, out var n) ? n.OpenerName : null;
            }
            finally { _lock.ExitReadLock(); }
        }

        public object GetContext(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return null;
            _lock.EnterReadLock();
            try
            {
                return _nodes.TryGetValue(windowName, out var n) ? n.Context : null;
            }
            finally { _lock.ExitReadLock(); }
        }

        public List<string> GetAncestors(string windowName)
        {
            var result = new List<string>(8);
            if (string.IsNullOrEmpty(windowName)) return result;

            _lock.EnterReadLock();
            try
            {
                // Walk up the opener chain and collect names, then reverse for oldest-first order
                string current = windowName;
                // Guard against cycles (should never happen, but defensive)
                int guard = _nodes.Count + 1;
                while (!string.IsNullOrEmpty(current) && guard-- > 0)
                {
                    if (!_nodes.TryGetValue(current, out var n)) break;
                    if (!string.IsNullOrEmpty(n.OpenerName))
                        result.Add(n.OpenerName);
                    current = n.OpenerName;
                }
                result.Reverse();
                return result;
            }
            finally { _lock.ExitReadLock(); }
        }

        public List<string> GetChildren(string windowName)
        {
            var result = new List<string>(4);
            if (string.IsNullOrEmpty(windowName)) return result;

            _lock.EnterReadLock();
            try
            {
                if (_nodes.TryGetValue(windowName, out var n))
                {
                    foreach (string c in n.Children)
                    {
                        if (_nodes.ContainsKey(c)) result.Add(c);
                    }
                }
                return result;
            }
            finally { _lock.ExitReadLock(); }
        }

        public string ResolveBackTarget(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return null;

            _lock.EnterReadLock();
            try
            {
                if (!_nodes.TryGetValue(windowName, out var node)) return null;

                // Walk up the opener chain until we find a node that is still alive
                string target = node.OpenerName;
                int guard = _nodes.Count + 1;
                while (!string.IsNullOrEmpty(target) && guard-- > 0)
                {
                    if (_nodes.ContainsKey(target)) return target;
                    // Opener is gone — try its opener (handles multi-level gaps)
                    target = _nodes.TryGetValue(target, out var t) ? t.OpenerName : null;
                }
                return null;
            }
            finally { _lock.ExitReadLock(); }
        }

        public List<UINavigationEntry> GetHistory()
        {
            _lock.EnterReadLock();
            try
            {
                return new List<UINavigationEntry>(_insertionOrder);
            }
            finally { _lock.ExitReadLock(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lock.Dispose();
        }
    }
}
