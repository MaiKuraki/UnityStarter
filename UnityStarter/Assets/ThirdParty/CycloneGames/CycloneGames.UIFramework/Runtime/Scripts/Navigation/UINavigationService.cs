using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Main-thread-confined navigation graph for active UI windows.
    /// Reads do not allocate when caller-provided buffers have sufficient capacity.
    /// </summary>
    public sealed class UINavigationService : IUINavigationService
    {
        private const int DefaultInitialCapacity = 16;
        private const int DefaultChildCapacity = 2;

        private readonly Dictionary<string, Node> _nodes;
        // Registration order is a linked list with a node map: removal is O(1) and leaves no stale
        // entry, which CopyHistory would otherwise count twice once an id is registered again.
        private readonly LinkedList<string> _registrationOrder;
        private readonly Dictionary<string, LinkedListNode<string>> _registrationNodes;
        private readonly List<string> _traversalStack;
        private readonly int _ownerThreadId;

        private string _currentWindow;
        private long _lastSequence;

        private struct Node
        {
            public string OpenerId;
            public object Context;
            public List<string> Children;
            public long Sequence;
        }

        public UINavigationService(int initialCapacity = DefaultInitialCapacity)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _nodes = new Dictionary<string, Node>(initialCapacity, StringComparer.Ordinal);
            _registrationOrder = new LinkedList<string>();
            _registrationNodes =
                new Dictionary<string, LinkedListNode<string>>(initialCapacity, StringComparer.Ordinal);
            _traversalStack = new List<string>(initialCapacity);
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public string CurrentWindow
        {
            get
            {
                EnsureOwnerThread();
                return _currentWindow;
            }
        }

        public bool CanNavigateBack
        {
            get
            {
                EnsureOwnerThread();
                return ResolveBackTargetCore(_currentWindow) != null;
            }
        }

        public bool Register(string windowId, string openerId = null, object context = null)
        {
            EnsureOwnerThread();
            if (string.IsNullOrEmpty(windowId) || _nodes.ContainsKey(windowId))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(openerId))
            {
                if (StringComparer.Ordinal.Equals(windowId, openerId) || !_nodes.ContainsKey(openerId))
                {
                    return false;
                }
            }

            var node = new Node
            {
                OpenerId = openerId,
                Context = context,
                Children = null,
                Sequence = NextSequence(),
            };

            _nodes.Add(windowId, node);
            _registrationNodes[windowId] = _registrationOrder.AddLast(windowId);

            if (!string.IsNullOrEmpty(openerId))
            {
                AddChildInSequenceOrder(openerId, windowId, node.Sequence);
            }

            _currentWindow = windowId;
            return true;
        }

        public bool Unregister(
            string windowId,
            ChildClosePolicy policy,
            List<string> affectedWindowIds)
        {
            EnsureOwnerThread();
            if (affectedWindowIds == null)
            {
                throw new ArgumentNullException(nameof(affectedWindowIds));
            }

            // Contiguous, ascending enum domain (Reparent=0, Cascade=1, Detach=2): an
            // unsigned range check rejects undefined values without the boxing that
            // Enum.IsDefined performs.
            if ((uint)policy > (uint)ChildClosePolicy.Detach)
            {
                // An undefined policy fails the Try-style call without touching the caller buffer or the graph.
                return false;
            }

            affectedWindowIds.Clear();

            if (string.IsNullOrEmpty(windowId) || !_nodes.TryGetValue(windowId, out Node node))
            {
                return false;
            }

            switch (policy)
            {
                case ChildClosePolicy.Reparent:
                    affectedWindowIds.Add(windowId);
                    ReparentChildren(windowId, node);
                    RemoveRegistration(windowId);
                    break;

                case ChildClosePolicy.Cascade:
                    CollectCascadeDepthFirst(windowId, affectedWindowIds);
                    RemoveCascade(affectedWindowIds);
                    for (int i = 0; i < affectedWindowIds.Count; i++)
                    {
                        RemoveRegistration(affectedWindowIds[i]);
                    }

                    break;

                case ChildClosePolicy.Detach:
                    affectedWindowIds.Add(windowId);
                    DetachChildren(windowId, node);
                    RemoveRegistration(windowId);
                    break;

                default:
                    return false;
            }

            _currentWindow = _registrationOrder.Count > 0
                ? _registrationOrder.Last.Value
                : null;
            return true;
        }

        public void Clear()
        {
            EnsureOwnerThread();
            _nodes.Clear();
            _registrationOrder.Clear();
            _registrationNodes.Clear();
            _traversalStack.Clear();
            _currentWindow = null;
            _lastSequence = 0;
        }

        public string GetOpener(string windowId)
        {
            EnsureOwnerThread();
            if (string.IsNullOrEmpty(windowId))
            {
                return null;
            }

            return _nodes.TryGetValue(windowId, out Node node) ? node.OpenerId : null;
        }

        public object GetContext(string windowId)
        {
            EnsureOwnerThread();
            if (string.IsNullOrEmpty(windowId))
            {
                return null;
            }

            return _nodes.TryGetValue(windowId, out Node node) ? node.Context : null;
        }

        public string ResolveBackTarget(string windowId)
        {
            EnsureOwnerThread();
            return ResolveBackTargetCore(windowId);
        }

        private string ResolveBackTargetCore(string windowId)
        {
            if (string.IsNullOrEmpty(windowId) || !_nodes.TryGetValue(windowId, out Node node))
            {
                return null;
            }

            return !string.IsNullOrEmpty(node.OpenerId) && _nodes.ContainsKey(node.OpenerId)
                ? node.OpenerId
                : null;
        }

        public int CopyAncestors(string windowId, List<string> destination)
        {
            EnsureOwnerThread();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (string.IsNullOrEmpty(windowId) || !_nodes.TryGetValue(windowId, out Node node))
            {
                return 0;
            }

            string openerId = node.OpenerId;
            while (!string.IsNullOrEmpty(openerId) && _nodes.TryGetValue(openerId, out node))
            {
                destination.Add(openerId);
                openerId = node.OpenerId;
            }

            destination.Reverse();
            return destination.Count;
        }

        public int CopyChildren(string windowId, List<string> destination)
        {
            EnsureOwnerThread();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (string.IsNullOrEmpty(windowId) ||
                !_nodes.TryGetValue(windowId, out Node node) ||
                node.Children == null)
            {
                return 0;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                string childId = node.Children[i];
                if (_nodes.ContainsKey(childId))
                {
                    destination.Add(childId);
                }
            }

            return destination.Count;
        }

        public int CopyHistory(List<UINavigationEntry> destination)
        {
            EnsureOwnerThread();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            for (LinkedListNode<string> cursor = _registrationOrder.First;
                 cursor != null;
                 cursor = cursor.Next)
            {
                string windowId = cursor.Value;
                if (_nodes.TryGetValue(windowId, out Node node))
                {
                    destination.Add(new UINavigationEntry(
                        windowId,
                        node.OpenerId,
                        node.Context,
                        node.Sequence));
                }
            }

            return destination.Count;
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "UINavigationService may only be used from its owning thread.");
            }
        }

        private long NextSequence()
        {
            if (_lastSequence == long.MaxValue)
            {
                RenumberActiveNodes();
            }

            _lastSequence++;
            return _lastSequence;
        }

        private void RenumberActiveNodes()
        {
            long sequence = 0;
            for (LinkedListNode<string> cursor = _registrationOrder.First;
                 cursor != null;
                 cursor = cursor.Next)
            {
                string windowId = cursor.Value;
                if (_nodes.TryGetValue(windowId, out Node node))
                {
                    node.Sequence = ++sequence;
                    _nodes[windowId] = node;
                }
            }

            _lastSequence = sequence;
        }

        private void ReparentChildren(string windowId, Node node)
        {
            RemoveChild(node.OpenerId, windowId);

            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    string childId = node.Children[i];
                    if (!_nodes.TryGetValue(childId, out Node child))
                    {
                        continue;
                    }

                    child.OpenerId = node.OpenerId;
                    _nodes[childId] = child;

                    if (!string.IsNullOrEmpty(node.OpenerId))
                    {
                        AddChildInSequenceOrder(node.OpenerId, childId, child.Sequence);
                    }
                }
            }

            _nodes.Remove(windowId);
        }

        private void DetachChildren(string windowId, Node node)
        {
            RemoveChild(node.OpenerId, windowId);

            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    string childId = node.Children[i];
                    if (_nodes.TryGetValue(childId, out Node child))
                    {
                        child.OpenerId = null;
                        _nodes[childId] = child;
                    }
                }
            }

            _nodes.Remove(windowId);
        }

        private void CollectCascadeDepthFirst(string rootId, List<string> destination)
        {
            _traversalStack.Clear();
            _traversalStack.Add(rootId);

            while (_traversalStack.Count > 0)
            {
                int lastIndex = _traversalStack.Count - 1;
                string windowId = _traversalStack[lastIndex];
                _traversalStack.RemoveAt(lastIndex);
                destination.Add(windowId);

                if (!_nodes.TryGetValue(windowId, out Node node) || node.Children == null)
                {
                    continue;
                }

                for (int i = node.Children.Count - 1; i >= 0; i--)
                {
                    string childId = node.Children[i];
                    if (_nodes.ContainsKey(childId))
                    {
                        _traversalStack.Add(childId);
                    }
                }
            }

            _traversalStack.Clear();
        }

        private void RemoveCascade(List<string> affectedWindowIds)
        {
            for (int i = affectedWindowIds.Count - 1; i >= 0; i--)
            {
                string windowId = affectedWindowIds[i];
                if (_nodes.TryGetValue(windowId, out Node node))
                {
                    RemoveChild(node.OpenerId, windowId);
                    _nodes.Remove(windowId);
                }
            }
        }

        private void AddChildInSequenceOrder(string parentId, string childId, long childSequence)
        {
            if (!_nodes.TryGetValue(parentId, out Node parent))
            {
                return;
            }

            if (parent.Children == null)
            {
                parent.Children = new List<string>(DefaultChildCapacity);
                parent.Children.Add(childId);
                _nodes[parentId] = parent;
                return;
            }

            int insertionIndex = parent.Children.Count;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (_nodes.TryGetValue(parent.Children[i], out Node sibling) &&
                    sibling.Sequence > childSequence)
                {
                    insertionIndex = i;
                    break;
                }
            }

            parent.Children.Insert(insertionIndex, childId);
        }

        private void RemoveChild(string parentId, string childId)
        {
            if (string.IsNullOrEmpty(parentId) ||
                !_nodes.TryGetValue(parentId, out Node parent) ||
                parent.Children == null)
            {
                return;
            }

            if (!parent.Children.Remove(childId) || parent.Children.Count > 0)
            {
                return;
            }

            parent.Children = null;
            _nodes[parentId] = parent;
        }

        /// <summary>
        /// Unlinks one id in O(1) via its cached node. Removal must be eager: a stale entry is
        /// counted twice by <see cref="CopyHistory"/> once the id is registered again.
        /// </summary>
        private void RemoveRegistration(string windowId)
        {
            if (_registrationNodes.TryGetValue(windowId, out LinkedListNode<string> node))
            {
                _registrationOrder.Remove(node);
                _registrationNodes.Remove(windowId);
            }
        }
    }
}
