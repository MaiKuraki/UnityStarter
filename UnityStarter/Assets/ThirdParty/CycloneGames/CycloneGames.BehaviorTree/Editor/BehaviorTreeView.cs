using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Linq;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime.Compilation;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BehaviorTreeView : GraphView
    {
        private const float NODE_X_GAP = 160;
        private const float NODE_Y_GAP = 160;
        private const float NODE_MIN_WIDTH = 130;
        private const float NODE_HEIGHT = 80;

        // Cached runners to avoid FindObjectsOfType GC every frame
        private static readonly List<Runtime.Components.BTRunnerComponent> _cachedRunners = new List<Runtime.Components.BTRunnerComponent>(8);
        private static double _lastRunnerCacheTime = double.NegativeInfinity;
        private const double RUNNER_CACHE_INTERVAL = 0.5; // Refresh every 0.5 seconds

        /// <summary>
        /// Gets cached BTRunnerComponents, refreshing the cache periodically to avoid GC allocations.
        /// </summary>
        public static List<Runtime.Components.BTRunnerComponent> GetCachedRunners()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastRunnerCacheTime > RUNNER_CACHE_INTERVAL)
            {
                _cachedRunners.Clear();
                var runners = Runtime.Components.BTRunnerComponent.ActiveRunners;
                for (int i = 0; i < runners.Count; i++)
                {
                    if (runners[i] != null)
                    {
                        _cachedRunners.Add(runners[i]);
                    }
                }

                // Fallback for editor edge-cases (domain reload/scene reload timing).
                if (_cachedRunners.Count == 0)
                {
                    var foundRunners = UnityEngine.Object.FindObjectsOfType<Runtime.Components.BTRunnerComponent>();
                    for (int i = 0; i < foundRunners.Length; i++)
                    {
                        _cachedRunners.Add(foundRunners[i]);
                    }
                }
                _lastRunnerCacheTime = currentTime;
            }
            return _cachedRunners;
        }

        /// <summary>
        /// Forces immediate refresh of cached runners (call when entering play mode).
        /// </summary>
        public static void InvalidateRunnerCache()
        {
            _lastRunnerCacheTime = double.NegativeInfinity;
            _cachedRunners.Clear();
        }

        public static bool AreSameTreeAsset(Runtime.BehaviorTree a, Runtime.BehaviorTree b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;

            string pathA = AssetDatabase.GetAssetPath(a);
            string pathB = AssetDatabase.GetAssetPath(b);
            return !string.IsNullOrEmpty(pathA) && pathA == pathB;
        }

        public new class UxmlFactory : UxmlFactory<BehaviorTreeView, GraphView.UxmlTraits> { }
        public Runtime.BehaviorTree Tree => _tree;
        public Action<BTNodeView> OnNodeSelectionChanged;
        private Runtime.BehaviorTree _tree;
        private Runtime.Components.BTRunnerComponent _boundRunner;
        private double _lastBoundRunnerRefreshTime;
        private const double BOUND_RUNNER_REFRESH_INTERVAL = 0.2;
        private List<BTNode> _copiedNodes = new List<BTNode>();
        private Vector2 _copiedTreePosition;

        private BTState _lastTreeState = BTState.NOT_ENTERED;
        private string _searchFilter = string.Empty;
        private bool _editorEventsSubscribed;
        private bool _isAuthoringReadOnly;

        internal bool IsAuthoringReadOnly => AuthoringWritesBlocked;
        private bool AuthoringWritesBlocked =>
            _isAuthoringReadOnly || EditorApplication.isPlayingOrWillChangePlaymode;

        // Cached lists to avoid per-frame ToList() allocations
        private readonly List<UnityEditor.Experimental.GraphView.Node> _cachedNodeList = new List<UnityEditor.Experimental.GraphView.Node>(64);

        /// <summary>Clears and refills the cached list from the GraphView node enumerator.</summary>
        private void RefreshCachedNodeList()
        {
            _cachedNodeList.Clear();
            foreach (var n in nodes) _cachedNodeList.Add(n);
        }

        /// <summary>
        /// Gets the actual tree state from the matched RuntimeBehaviorTree via BTRunnerComponent.
        /// </summary>
        private BTState GetRuntimeTreeState()
        {
            var runner = GetBoundRunner();
            if (runner == null || runner.RuntimeTree == null) return BTState.NOT_ENTERED;

            switch (runner.RuntimeTree.State)
            {
                case Runtime.Core.RuntimeState.Success: return BTState.SUCCESS;
                case Runtime.Core.RuntimeState.Failure: return BTState.FAILURE;
                case Runtime.Core.RuntimeState.Running: return BTState.RUNNING;
                default: return BTState.NOT_ENTERED;
            }
        }

        public Runtime.Components.BTRunnerComponent GetBoundRunner()
        {
            if (!Application.isPlaying || _tree == null) return null;

            double now = EditorApplication.timeSinceStartup;
            bool shouldRefresh = (now - _lastBoundRunnerRefreshTime) > BOUND_RUNNER_REFRESH_INTERVAL;

            if (!shouldRefresh && _boundRunner != null && _boundRunner.RuntimeTree != null && AreSameTreeAsset(_boundRunner.Tree, _tree))
            {
                return _boundRunner;
            }

            _boundRunner = null;
            var runners = GetCachedRunners();
            for (int i = 0; i < runners.Count; i++)
            {
                var candidate = runners[i];
                if (candidate == null || candidate.RuntimeTree == null) continue;
                if (AreSameTreeAsset(candidate.Tree, _tree))
                {
                    _boundRunner = candidate;
                    break;
                }
            }

            _lastBoundRunnerRefreshTime = now;
            return _boundRunner;
        }

        public Runtime.Core.RuntimeNode GetRuntimeNodeByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;

            var runner = GetBoundRunner();
            if (runner == null || runner.RuntimeTree == null) return null;
            return runner.RuntimeTree.GetNodeByGUID(guid);
        }

        public BehaviorTreeView()
        {
            _isAuthoringReadOnly = EditorApplication.isPlayingOrWillChangePlaymode;
            Insert(0, new GridBackground());
            var contentZoomer = new ContentZoomer();
            contentZoomer.maxScale = 2.5f;
            contentZoomer.minScale = 0.1f;
            this.AddManipulator(contentZoomer);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            StyleSheet styleSheet = BehaviorTreeEditorResources.EditorStyle;
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        private void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            SubscribeEditorEvents();
            SetAuthoringReadOnly(EditorApplication.isPlayingOrWillChangePlaymode);
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            UnsubscribeEditorEvents();
        }

        private void SubscribeEditorEvents()
        {
            if (_editorEventsSubscribed)
            {
                return;
            }

            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _editorEventsSubscribed = true;
        }

        private void UnsubscribeEditorEvents()
        {
            if (!_editorEventsSubscribed)
            {
                return;
            }

            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _editorEventsSubscribed = false;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            SetAuthoringReadOnly(state != PlayModeStateChange.EnteredEditMode);
        }

        private void SetAuthoringReadOnly(bool isReadOnly)
        {
            _isAuthoringReadOnly = isReadOnly;
            RefreshCachedNodeList();
            for (int i = 0; i < _cachedNodeList.Count; i++)
            {
                if (_cachedNodeList[i] is BTNodeView nodeView)
                {
                    nodeView.SetAuthoringReadOnly(isReadOnly);
                }
            }
        }

        /// <summary>
        /// Handles Alt+Click to delete edges connecting nodes.
        /// </summary>
        private void OnMouseDown(MouseDownEvent evt)
        {
            if (!AuthoringWritesBlocked && evt.altKey)
            {
                var target = evt.target as VisualElement;

                VisualElement current = target;
                while (current != null && current != this)
                {
                    if (current is Edge edge)
                    {
                        DeleteElements(new[] { edge });
                        evt.StopPropagation();
                        evt.PreventDefault();
                        return;
                    }
                    current = current.parent;
                }

                Vector2 localMousePos = evt.localMousePosition;
                if (contentViewContainer != null)
                {
                    localMousePos = contentViewContainer.WorldToLocal(evt.mousePosition);
                }

                foreach (var element in graphElements)
                {
                    if (element is Edge edge)
                    {
                        if (IsMouseNearEdge(edge, localMousePos, 15f))
                        {
                            DeleteElements(new[] { edge });
                            evt.StopPropagation();
                            evt.PreventDefault();
                            return;
                        }
                    }
                }
            }
        }

        private bool IsMouseNearEdge(Edge edge, Vector2 mousePos, float threshold)
        {
            if (edge?.input == null || edge.output == null) return false;

            try
            {
                var inputPort = edge.input;
                var outputPort = edge.output;

                var inputLayout = inputPort.layout;
                var outputLayout = outputPort.layout;

                Vector2 inputPos = new Vector2(
                    inputLayout.x + inputLayout.width * 0.5f,
                    inputLayout.y + inputLayout.height * 0.5f
                );
                Vector2 outputPos = new Vector2(
                    outputLayout.x + outputLayout.width * 0.5f,
                    outputLayout.y + outputLayout.height * 0.5f
                );

                if (inputPort.parent != null && inputPort.parent != contentViewContainer)
                {
                    var parentLayout = inputPort.parent.layout;
                    inputPos.x += parentLayout.x;
                    inputPos.y += parentLayout.y;
                }
                if (outputPort.parent != null && outputPort.parent != contentViewContainer)
                {
                    var parentLayout = outputPort.parent.layout;
                    outputPos.x += parentLayout.x;
                    outputPos.y += parentLayout.y;
                }

                float distance = DistanceToLineSegment(mousePos, inputPos, outputPos);
                return distance <= threshold;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Calculates the shortest distance from a point to a line segment.
        /// </summary>
        /// <param name="point">Point to measure distance from</param>
        /// <param name="lineStart">Start of the line segment</param>
        /// <param name="lineEnd">End of the line segment</param>
        /// <returns>Distance from point to line segment</returns>
        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lineLength = line.magnitude;
            if (lineLength < 0.001f) return Vector2.Distance(point, lineStart);

            line /= lineLength;
            Vector2 pointToStart = point - lineStart;
            float projection = Vector2.Dot(pointToStart, line);
            projection = Mathf.Clamp(projection, 0f, lineLength);

            Vector2 closestPoint = lineStart + line * projection;
            return Vector2.Distance(point, closestPoint);
        }
        /// <summary>
        /// Clears all graph elements and unsubscribes from events.
        /// </summary>
        public void ClearView()
        {
            UnsubscribeEditorEvents();
            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements);
            _tree = null;
        }
        /// <summary>
        /// Caches node states by GUID to preserve final states across PopulateView calls.
        /// </summary>
        private Dictionary<string, BTState> _stateCache = new Dictionary<string, BTState>();

        /// <summary>
        /// Populates the view with the given behavior tree. If the tree is the same reference,
        /// only updates states to preserve node view instances and their cached states.
        /// </summary>
        public void PopulateView(Runtime.BehaviorTree tree)
        {
            if (this._tree == tree)
            {
                UpdateNodeStates();
                ApplySearchFilter();
                return;
            }

            SaveStateCache();
            this._tree = tree;
            _boundRunner = null;
            _lastBoundRunnerRefreshTime = 0;
            _lastTreeState = BTState.NOT_ENTERED;
            DrawGraph();
            RestoreStateCache();
            ApplySearchFilter();
        }

        public void SetSearchFilter(string searchFilter)
        {
            _searchFilter = searchFilter ?? string.Empty;
            ApplySearchFilter();
        }

        public int GetNodeCount()
        {
            return _tree?.Nodes?.Count ?? 0;
        }

        public bool HasTree => _tree != null;

        internal bool TryRepairAuthoringData(out string message)
        {
            if (AuthoringWritesBlocked)
            {
                message = "Behavior tree authoring is read-only in Play Mode.";
                return false;
            }

            if (_tree == null)
            {
                message = "No tree selected.";
                return false;
            }

            if (!TryCreateRepairPlan(out AuthoringRepairPlan plan, out message))
            {
                return false;
            }

            const string undoName = "Repair Behavior Tree Authoring Data";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            var attachedNodes = new List<BTNode>(plan.TransientNodes.Count);
            bool assetSynchronizationStarted = false;
            try
            {
                Undo.RegisterCompleteObjectUndo(plan.UndoTargets.ToArray(), undoName);

                if (plan.IsPersistent)
                {
                    for (int i = 0; i < plan.TransientNodes.Count; i++)
                    {
                        BTNode node = plan.TransientNodes[i];
                        attachedNodes.Add(node);
                        AssetDatabase.AddObjectToAsset(node, _tree);
                    }
                }

                _tree.Nodes = new List<BTNode>(plan.OrderedNodes);
                for (int i = 0; i < _tree.Nodes.Count; i++)
                {
                    BTNode node = _tree.Nodes[i];
                    node.Tree = _tree;
                    if (node is CompositeNode composite)
                    {
                        _tree.NormalizeCompositeChildren(composite);
                    }

                    node.OnValidate();
                    EditorUtility.SetDirty(node);
                }

                List<string> compilerErrors = BehaviorTreeCompiler.Validate(_tree);
                if (compilerErrors.Count > 0)
                {
                    var validationFailures = new List<string>(compilerErrors.Count);
                    for (int i = 0; i < compilerErrors.Count; i++)
                    {
                        validationFailures.Add("Compiler: " + compilerErrors[i]);
                    }

                    throw new InvalidOperationException(
                        "Repair validation failed:\n" + string.Join("\n", validationFailures));
                }

                if (plan.IsPersistent)
                {
                    assetSynchronizationStarted = true;
                    BehaviorTreeAuthoringUtility.SynchronizePersistentAsset(_tree);
                }

                List<string> authoringIssues = BehaviorTreeAuthoringUtility.ValidatePersistentAsset(_tree);
                if (authoringIssues.Count > 0)
                {
                    var validationFailures = new List<string>(authoringIssues.Count);
                    for (int i = 0; i < authoringIssues.Count; i++)
                    {
                        validationFailures.Add("Authoring: " + authoringIssues[i]);
                    }

                    throw new InvalidOperationException(
                        "Repair validation failed:\n" + string.Join("\n", validationFailures));
                }

                EditorUtility.SetDirty(_tree);
                if (plan.IsPersistent)
                {
                    for (int i = 0; i < attachedNodes.Count; i++)
                    {
                        Undo.RegisterCreatedObjectUndo(attachedNodes[i], undoName);
                    }
                }

                Undo.CollapseUndoOperations(undoGroup);
                DrawGraph();
                message = $"Authoring data repaired and validated. Registered nodes: {_tree.Nodes.Count}.";
                return true;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                DetachFailedRepairNodes(attachedNodes);
                RestoreFailedRepairNullCollections(plan);
                if (assetSynchronizationStarted)
                {
                    BehaviorTreeAuthoringUtility.SynchronizePersistentAsset(_tree);
                    RestoreFailedRepairNullCollections(plan);
                }

                DrawGraph();
                message = $"Repair was rolled back: {exception.Message}";
                return false;
            }
        }

        internal bool TryRepairMissingRoot(out string message)
        {
            if (AuthoringWritesBlocked)
            {
                message = "Behavior tree authoring is read-only in Play Mode.";
                return false;
            }

            if (_tree == null)
            {
                message = "No tree selected.";
                return false;
            }

            if (_tree.Root != null)
            {
                message = "The tree already has a root node.";
                return false;
            }

            if (!EditorUtility.IsPersistent(_tree))
            {
                message = "The tree must be saved as an asset before it can be repaired.";
                return false;
            }

            if (_tree.Nodes == null)
            {
                message = "The serialized node list is null. Run Repair Asset before repairing the root.";
                return false;
            }

            BTRootNode reusableRoot = null;
            int rootCount = 0;
            for (int i = 0; i < _tree.Nodes.Count; i++)
            {
                if (_tree.Nodes[i] is BTRootNode root)
                {
                    reusableRoot = root;
                    rootCount++;
                }
            }

            if (rootCount > 1)
            {
                message = "The asset contains multiple root nodes. Resolve the duplicates before repairing the root reference.";
                return false;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Repair Behavior Tree Root");
            Undo.RecordObject(_tree, "Repair Behavior Tree Root");

            if (reusableRoot == null)
            {
                reusableRoot = _tree.CreateNode(typeof(BTRootNode)) as BTRootNode;
                if (reusableRoot == null)
                {
                    Undo.CollapseUndoOperations(undoGroup);
                    message = "The root node could not be created.";
                    return false;
                }

                reusableRoot.Position = Vector2.zero;
                EditorUtility.SetDirty(reusableRoot);
            }

            Undo.RecordObject(reusableRoot, "Repair Behavior Tree Root");
            reusableRoot.Tree = _tree;
            EditorUtility.SetDirty(reusableRoot);
            _tree.Root = reusableRoot;
            EditorUtility.SetDirty(_tree);
            Undo.CollapseUndoOperations(undoGroup);

            AssetDatabase.SaveAssetIfDirty(_tree);
            DrawGraph();
            message = rootCount == 1
                ? "The existing root node reference was restored."
                : "A new root node was created. Connect its child before running the tree.";
            return true;
        }

        public string GetValidationReport()
        {
            if (_tree == null)
            {
                return "No tree selected.";
            }

            var issues = new List<string>();
            List<string> compilerErrors = BehaviorTreeCompiler.Validate(_tree);
            for (int i = 0; i < compilerErrors.Count; i++)
            {
                issues.Add("- Compiler: " + compilerErrors[i]);
            }

            List<string> authoringIssues = BehaviorTreeAuthoringUtility.ValidatePersistentAsset(_tree);
            for (int i = 0; i < authoringIssues.Count; i++)
            {
                issues.Add("- Authoring: " + authoringIssues[i]);
            }

            if (_tree.Nodes == null)
            {
                issues.Add("- The serialized node list is null. Run Repair Asset in Edit Mode.");
                return string.Join("\n", issues);
            }

            var guidSet = new HashSet<string>();
            HashSet<BTNode> reachableNodes = CollectReachableNodes();
            int orphanCount = 0;

            for (int i = 0; i < _tree.Nodes.Count; i++)
            {
                var node = _tree.Nodes[i];
                if (node == null)
                {
                    issues.Add($"- Nodes[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrEmpty(node.GUID))
                {
                    issues.Add($"- {node.name}: missing GUID.");
                }
                else if (!guidSet.Add(node.GUID))
                {
                    issues.Add($"- {node.name}: duplicate GUID '{node.GUID}'.");
                }

                if (node != _tree.Root && !reachableNodes.Contains(node))
                {
                    orphanCount++;
                }
            }

            if (orphanCount > 0)
            {
                issues.Add($"- Found {orphanCount} orphan node(s) not connected to the root.");
            }

            return issues.Count == 0
                ? $"Validation passed. Nodes: {GetNodeCount()}."
                : string.Join("\n", issues);
        }

        /// <summary>
        /// Saves final states (SUCCESS/FAILURE) from existing node views before they are destroyed.
        /// </summary>
        private void SaveStateCache()
        {
            _stateCache.Clear();
            RefreshCachedNodeList();
            int nodeCount = _cachedNodeList.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                if (_cachedNodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    string guid = nodeView.Node.GUID;
                    if (!string.IsNullOrEmpty(guid))
                    {
                        BTState lastState = nodeView.GetLastKnownState();
                        if (lastState == BTState.SUCCESS || lastState == BTState.FAILURE)
                        {
                            _stateCache[guid] = lastState;
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Restores cached final states to newly created node views.
        /// </summary>
        private void RestoreStateCache()
        {
            if (_stateCache.Count == 0) return;

            RefreshCachedNodeList();
            int nodeCount = _cachedNodeList.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                if (_cachedNodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    string guid = nodeView.Node.GUID;
                    if (!string.IsNullOrEmpty(guid) && _stateCache.ContainsKey(guid))
                    {
                        nodeView.RestoreLastKnownState(_stateCache[guid]);
                        nodeView.UpdateState();
                    }
                }
            }
        }

        /// <summary>
        /// Updates all node states. If tree is completed, proactively caches final states
        /// before BehaviorTree.Stop() resets them to NOT_ENTERED.
        /// Uses multi-pass inference to ensure all nodes get their correct states.
        /// Clears state cache when tree restarts from a completed state.
        /// </summary>
        public void UpdateNodeStates()
        {
            if (!Application.isPlaying || _tree == null || _tree.Nodes == null) return;

            BTState currentTreeState = GetRuntimeTreeState();
            bool treeRestarted = (_lastTreeState == BTState.SUCCESS || _lastTreeState == BTState.FAILURE)
                                 && currentTreeState == BTState.RUNNING;

            if (treeRestarted)
            {
                ClearAllNodeStateCache();
            }

            _lastTreeState = currentTreeState;

            bool treeCompleted = currentTreeState == BTState.SUCCESS || currentTreeState == BTState.FAILURE;

            RefreshCachedNodeList();
            var nodeList = _cachedNodeList;
            int nodeCount = nodeList.Count;

            if (treeCompleted)
            {
                for (int i = 0; i < nodeCount; i++)
                {
                    if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                    {
                        var authoringNode = nodeView.Node;
                        var runtimeNode = nodeView.RuntimeNode;
                        if (runtimeNode == null) continue;

                        BTState currentState = ToBTState(runtimeNode.State);
                        if (currentState == BTState.SUCCESS || currentState == BTState.FAILURE)
                        {
                            nodeView.RestoreLastKnownState(currentState);
                        }
                        else if (currentState == BTState.NOT_ENTERED && !runtimeNode.IsStarted)
                        {
                            if (authoringNode is BTRootNode)
                            {
                                nodeView.RestoreLastKnownState(currentTreeState);
                            }
                            else if (authoringNode is CompositeNode composite)
                            {
                                BTState inferredState = InferCompositeNodeState(composite, currentTreeState, nodeList, nodeCount);
                                if (inferredState == BTState.SUCCESS || inferredState == BTState.FAILURE)
                                {
                                    nodeView.RestoreLastKnownState(inferredState);
                                }
                            }
                            else
                            {
                                BTState cachedState = nodeView.GetLastKnownState();
                                if (cachedState == BTState.NOT_ENTERED || cachedState == BTState.RUNNING)
                                {
                                    BTState inferredState = InferLeafNodeState(authoringNode, currentTreeState, nodeList, nodeCount);
                                    if (inferredState == BTState.SUCCESS || inferredState == BTState.FAILURE)
                                    {
                                        nodeView.RestoreLastKnownState(inferredState);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    nodeView.UpdateState();
                }
            }

            // Update edge states based on connected node states
            UpdateEdgeStates();
        }

        /// <summary>
        /// Updates edge visual styles based on node state.
        /// Uses GetLastKnownState() as fallback to ensure colors persist after tree completion.
        /// When the parent is Running, uses granular child-state coloring.
        /// Inactive edges are dimmed to highlight the execution path.
        /// </summary>
        private void UpdateEdgeStates()
        {
            if (!Application.isPlaying) return;

            foreach (var element in graphElements)
            {
                if (element is Edge edge)
                {
                    edge.RemoveFromClassList("running-edge");
                    edge.RemoveFromClassList("success-edge");
                    edge.RemoveFromClassList("failure-edge");
                    edge.RemoveFromClassList("inactive-edge");

                    if (edge.output?.node is BTNodeView parentView)
                    {
                        // Resolve parent state: prefer RuntimeNode, fall back to last known
                        BTState parentState = ResolveNodeState(parentView);

                        // When parent is Running, show per-child granularity
                        if (parentState == BTState.RUNNING && edge.input?.node is BTNodeView childView)
                        {
                            BTState childState = ResolveNodeState(childView);
                            ApplyEdgeVisual(edge, childState);
                        }
                        else
                        {
                            ApplyEdgeVisual(edge, parentState);
                        }
                    }
                }
            }
        }

        /// <summary>Resolves the most accurate state for a node view: RuntimeNode first, then last known state.</summary>
        private BTState ResolveNodeState(BTNodeView nodeView)
        {
            var runtimeNode = nodeView.RuntimeNode;
            if (runtimeNode != null)
            {
                return ToBTState(runtimeNode.State);
            }
            return nodeView.GetLastKnownState();
        }

        private static BTState ToBTState(Runtime.Core.RuntimeState state)
        {
            switch (state)
            {
                case Runtime.Core.RuntimeState.Success:
                    return BTState.SUCCESS;
                case Runtime.Core.RuntimeState.Failure:
                    return BTState.FAILURE;
                case Runtime.Core.RuntimeState.Running:
                    return BTState.RUNNING;
                default:
                    return BTState.NOT_ENTERED;
            }
        }

        /// <summary>Applies visual styling to an edge based on BTState.</summary>
        private void ApplyEdgeVisual(Edge edge, BTState state)
        {
            switch (state)
            {
                case BTState.RUNNING:
                    edge.AddToClassList("running-edge");
                    edge.edgeControl.edgeWidth = 4;
                    edge.edgeControl.inputColor = new Color(0.27f, 0.56f, 0.29f, 1f);
                    edge.edgeControl.outputColor = new Color(0.27f, 0.56f, 0.29f, 1f);
                    if (edge is BTAnimatedEdge aeRun)
                    {
                        aeRun.SetAnimating(true);
                        aeRun.SetColors(
                            new Color(0.56f, 1f, 0.56f, 0.9f),
                            new Color(0.27f, 0.56f, 0.29f, 0.4f));
                    }
                    break;

                case BTState.SUCCESS:
                    edge.AddToClassList("success-edge");
                    edge.edgeControl.edgeWidth = 3;
                    edge.edgeControl.inputColor = new Color(0.24f, 0.86f, 0.12f, 0.85f);
                    edge.edgeControl.outputColor = new Color(0.24f, 0.86f, 0.12f, 0.85f);
                    if (edge is BTAnimatedEdge aeSucc) aeSucc.SetAnimating(false);
                    break;

                case BTState.FAILURE:
                    edge.AddToClassList("failure-edge");
                    edge.edgeControl.edgeWidth = 3;
                    edge.edgeControl.inputColor = new Color(0.86f, 0.2f, 0.2f, 0.85f);
                    edge.edgeControl.outputColor = new Color(0.86f, 0.2f, 0.2f, 0.85f);
                    if (edge is BTAnimatedEdge aeFail) aeFail.SetAnimating(false);
                    break;

                default: // NOT_ENTERED
                    edge.AddToClassList("inactive-edge");
                    edge.edgeControl.edgeWidth = 2;
                    edge.edgeControl.inputColor = new Color(0.4f, 0.4f, 0.4f, 0.35f);
                    edge.edgeControl.outputColor = new Color(0.4f, 0.4f, 0.4f, 0.35f);
                    if (edge is BTAnimatedEdge aeIdle) aeIdle.SetAnimating(false);
                    break;
            }
        }

        /// <summary>
        /// Infers the final state of a composite node based on its children's states and tree state.
        /// </summary>
        private BTState InferCompositeNodeState(CompositeNode composite, BTState treeState, List<UnityEditor.Experimental.GraphView.Node> nodeList, int nodeCount)
        {
            if (composite == null || composite.Children == null || composite.Children.Count == 0)
            {
                return BTState.NOT_ENTERED;
            }

            if (composite is SequencerNode)
            {
                bool allChildrenSucceeded = true;
                bool hasChildren = false;
                int childrenCount = composite.Children.Count;

                for (int i = 0; i < childrenCount; i++)
                {
                    var child = composite.Children[i];
                    if (child == null) continue;
                    hasChildren = true;

                    BTState childState = GetRuntimeStateForAuthoringNode(child);
                    BTState childLastKnown = GetChildLastKnownState(child, nodeList, nodeCount);

                    if (childState == BTState.FAILURE || childLastKnown == BTState.FAILURE)
                    {
                        allChildrenSucceeded = false;
                        break;
                    }
                    else if (childState != BTState.SUCCESS && childLastKnown != BTState.SUCCESS)
                    {
                        if (i < childrenCount - 1)
                        {
                            allChildrenSucceeded = false;
                        }
                    }
                }

                if (hasChildren && allChildrenSucceeded && treeState == BTState.SUCCESS)
                {
                    return BTState.SUCCESS;
                }
                else if (hasChildren && !allChildrenSucceeded)
                {
                    return BTState.FAILURE;
                }
            }
            else if (composite is SelectorNode)
            {
                bool anyChildSucceeded = false;
                int childrenCount = composite.Children.Count;

                for (int i = 0; i < childrenCount; i++)
                {
                    var child = composite.Children[i];
                    if (child == null) continue;

                    BTState childState = GetRuntimeStateForAuthoringNode(child);
                    BTState childLastKnown = GetChildLastKnownState(child, nodeList, nodeCount);

                    if (childState == BTState.SUCCESS || childLastKnown == BTState.SUCCESS)
                    {
                        anyChildSucceeded = true;
                        break;
                    }
                }

                if (anyChildSucceeded && treeState == BTState.SUCCESS)
                {
                    return BTState.SUCCESS;
                }
            }

            return BTState.NOT_ENTERED;
        }

        /// <summary>
        /// Infers the final state of a leaf node based on its parent's state.
        /// </summary>
        private BTState InferLeafNodeState(BTNode leafNode, BTState treeState, List<UnityEditor.Experimental.GraphView.Node> nodeList, int nodeCount)
        {
            if (leafNode == null || _tree == null || _tree.Nodes == null) return BTState.NOT_ENTERED;

            int treeNodeCount = _tree.Nodes.Count;
            for (int i = 0; i < treeNodeCount; i++)
            {
                var treeNode = _tree.Nodes[i];
                if (treeNode == null) continue;

                if (treeNode is CompositeNode composite)
                {
                    if (composite.Children == null)
                    {
                        continue;
                    }

                    int childrenCount = composite.Children.Count;
                    for (int j = 0; j < childrenCount; j++)
                    {
                        var child = composite.Children[j];
                        if (child != null && child.GUID == leafNode.GUID)
                        {
                            BTState parentState = GetRuntimeStateForAuthoringNode(treeNode);
                            BTState parentLastKnown = GetChildLastKnownState(treeNode, nodeList, nodeCount);

                            if (composite is SequencerNode)
                            {
                                if ((parentState == BTState.SUCCESS || parentLastKnown == BTState.SUCCESS) && treeState == BTState.SUCCESS)
                                {
                                    bool isLastChild = (j == childrenCount - 1);
                                    if (isLastChild)
                                    {
                                        return BTState.SUCCESS;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }

            return BTState.NOT_ENTERED;
        }

        /// <summary>
        /// Gets the last known state of a child node by finding its corresponding node view.
        /// </summary>
        private BTState GetChildLastKnownState(BTNode childNode, List<UnityEditor.Experimental.GraphView.Node> nodeList, int nodeCount)
        {
            if (childNode == null) return BTState.NOT_ENTERED;

            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    BTNode authoringNode = nodeView.Node;
                    if (authoringNode != null && authoringNode.GUID == childNode.GUID)
                    {
                        return nodeView.GetLastKnownState();
                    }
                }
            }

            return GetRuntimeStateForAuthoringNode(childNode);
        }

        private BTState GetRuntimeStateForAuthoringNode(BTNode authoringNode)
        {
            if (authoringNode == null)
            {
                return BTState.NOT_ENTERED;
            }

            var runtimeNode = GetRuntimeNodeByGuid(authoringNode.GUID);
            return runtimeNode != null ? ToBTState(runtimeNode.State) : BTState.NOT_ENTERED;
        }

        /// <summary>
        /// Clears state cache for all node views when tree restarts.
        /// </summary>
        private void ClearAllNodeStateCache()
        {
            RefreshCachedNodeList();
            int nodeCount = _cachedNodeList.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                if (_cachedNodeList[i] is BTNodeView nodeView)
                {
                    nodeView.ClearStateCache();
                }
            }
        }
        /// <summary>
        /// Returns all ports that are compatible for connection with the start port.
        /// Ports must have opposite directions and belong to different nodes.
        /// </summary>
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();
            if (AuthoringWritesBlocked)
            {
                return compatiblePorts;
            }

            foreach (Port endPort in ports)
            {
                if (CanConnectPorts(startPort, endPort, true, out _))
                {
                    compatiblePorts.Add(endPort);
                }
            }

            return compatiblePorts;
        }

        private bool CanConnectPorts(Port firstPort, Port secondPort, bool enforcePortOccupancy, out string reason)
        {
            reason = null;
            if (_tree == null || firstPort == null || secondPort == null)
            {
                reason = "The tree or one of the ports is unavailable.";
                return false;
            }

            if (firstPort.direction == secondPort.direction || firstPort.node == secondPort.node)
            {
                reason = "Connections require distinct nodes and opposite port directions.";
                return false;
            }

            Port outputPort = firstPort.direction == Direction.Output ? firstPort : secondPort;
            Port inputPort = firstPort.direction == Direction.Input ? firstPort : secondPort;
            if (!(outputPort.node is BTNodeView parentView) || !(inputPort.node is BTNodeView childView))
            {
                reason = "Behavior tree edges must connect behavior tree nodes.";
                return false;
            }

            BTNode parent = parentView.Node;
            BTNode child = childView.Node;
            if (parent == null || child == null || parent == child)
            {
                reason = "A node cannot be connected to itself.";
                return false;
            }

            if (enforcePortOccupancy)
            {
                if (inputPort.capacity == Port.Capacity.Single && PortHasAnyConnection(inputPort))
                {
                    reason = "The child already has a parent.";
                    return false;
                }

                if (outputPort.capacity == Port.Capacity.Single && PortHasAnyConnection(outputPort))
                {
                    reason = "This node accepts only one child.";
                    return false;
                }
            }

            if (HasDirectChild(parent, child))
            {
                reason = "The nodes are already connected.";
                return false;
            }

            if (CountParents(child) > 0)
            {
                reason = "A behavior tree node can have only one parent.";
                return false;
            }

            if ((parent is BTRootNode || parent is DecoratorNode) && GetDirectChildCount(parent) > 0)
            {
                reason = "This node accepts only one child.";
                return false;
            }

            if (IsReachable(child, parent))
            {
                reason = "The connection would create a cycle.";
                return false;
            }

            return true;
        }

        private static bool PortHasAnyConnection(Port port)
        {
            if (port?.connections == null)
            {
                return false;
            }

            foreach (Edge _ in port.connections)
            {
                return true;
            }

            return false;
        }

        public override EventPropagation DeleteSelection()
        {
            if (AuthoringWritesBlocked)
            {
                return EventPropagation.Stop;
            }

            var nodes = selection.OfType<BTNodeView>().ToList();
            if (nodes.Any(n => n.Node is BTRootNode))
            {
                EditorUtility.DisplayDialog("Error", "Cannot delete root node", "OK");
                return EventPropagation.Stop;
            }
            EditorUtility.SetDirty(_tree);
            return base.DeleteSelection();
        }
        private void OnUndoRedo()
        {
            if (_tree == null) return;
            DrawGraph();
        }
        /// <summary>
        /// Draws the entire behavior tree graph by creating node views and connecting edges.
        /// </summary>
        private void DrawGraph()
        {
            if (_tree == null)
            {
                return;
            }

            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements);
            graphViewChanged += OnGraphViewChanged;

            if (_tree.Nodes == null)
            {
                SetAuthoringReadOnly(AuthoringWritesBlocked);
                return;
            }

            int nodeCount = _tree.Nodes.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                var node = _tree.Nodes[i];
                if (node != null)
                {
                    CreateNodeView(node);
                }
            }

            for (int i = 0; i < nodeCount; i++)
            {
                var node = _tree.Nodes[i];
                if (node == null) continue;

                int childrenCount = BehaviorTreeAuthoringUtility.GetChildCount(node);
                var parentView = FindNodeView(node);
                if (parentView == null) continue;

                for (int j = 0; j < childrenCount; j++)
                {
                    BTNode child = BehaviorTreeAuthoringUtility.GetChildAt(node, j);
                    if (child == null) continue;

                    var childView = FindNodeView(child);
                    if (childView == null) continue;

                    if (parentView.OutputPort != null && childView.InputPort != null)
                    {
                        var edge = new BTAnimatedEdge();
                        edge.output = parentView.OutputPort;
                        edge.input = childView.InputPort;
                        parentView.OutputPort.Connect(edge);
                        childView.InputPort.Connect(edge);
                        AddElement(edge);
                        parentView.UpdatePortConnectionState();
                        childView.UpdatePortConnectionState();
                    }
                }
            }

            SetAuthoringReadOnly(AuthoringWritesBlocked);
        }
        private GraphViewChange OnGraphViewChanged(GraphViewChange graphviewChange)
        {
            if (AuthoringWritesBlocked)
            {
                if (graphviewChange.movedElements != null)
                {
                    for (int i = 0; i < graphviewChange.movedElements.Count; i++)
                    {
                        if (graphviewChange.movedElements[i] is BTNodeView nodeView && nodeView.Node != null)
                        {
                            Rect currentPosition = nodeView.GetPosition();
                            currentPosition.position = nodeView.Node.Position;
                            nodeView.SetPosition(currentPosition);
                        }
                    }

                    graphviewChange.movedElements.Clear();
                }

                graphviewChange.elementsToRemove?.Clear();
                graphviewChange.edgesToCreate?.Clear();
                return graphviewChange;
            }

            if (graphviewChange.elementsToRemove != null)
            {
                int removeCount = graphviewChange.elementsToRemove.Count;
                var nodesBeingDeleted = new HashSet<BTNode>();
                for (int i = 0; i < removeCount; i++)
                {
                    if (graphviewChange.elementsToRemove[i] is BTNodeView nodeView && nodeView.Node != null)
                    {
                        nodesBeingDeleted.Add(nodeView.Node);
                    }
                }

                for (int i = 0; i < removeCount; i++)
                {
                    var elem = graphviewChange.elementsToRemove[i];
                    if (elem is Edge edge)
                    {
                        if (edge.input?.node is BTNodeView inputNodeView && edge.output?.node is BTNodeView outputNodeView)
                        {
                            if (!nodesBeingDeleted.Contains(inputNodeView.Node))
                            {
                                _tree.RemoveChild(outputNodeView.Node, inputNodeView.Node);
                            }
                            inputNodeView.UpdatePortConnectionState();
                            outputNodeView.UpdatePortConnectionState();
                        }
                    }
                }

                for (int i = 0; i < removeCount; i++)
                {
                    if (graphviewChange.elementsToRemove[i] is BTNodeView nodeView)
                    {
                        _tree.DeleteNode(nodeView.Node);
                    }
                }
            }
            if (graphviewChange.edgesToCreate != null)
            {
                for (int i = graphviewChange.edgesToCreate.Count - 1; i >= 0; i--)
                {
                    var edge = graphviewChange.edgesToCreate[i];
                    if (!CanConnectPorts(edge.output, edge.input, false, out string reason))
                    {
                        graphviewChange.edgesToCreate.RemoveAt(i);
                        Debug.LogWarning($"[BehaviorTreeEditor] Connection rejected: {reason}", _tree);
                        continue;
                    }

                    if (edge.input.node is BTNodeView inputNodeView && edge.output.node is BTNodeView outputNodeView)
                    {
                        _tree.AddChild(outputNodeView.Node, inputNodeView.Node);
                        inputNodeView.UpdatePortConnectionState();
                        outputNodeView.UpdatePortConnectionState();
                    }
                }
            }

            if (graphviewChange.movedElements != null && graphviewChange.movedElements.Count > 0)
            {
                var movedNodes = new List<BTNodeView>(graphviewChange.movedElements.Count);
                for (int i = 0; i < graphviewChange.movedElements.Count; i++)
                {
                    if (graphviewChange.movedElements[i] is BTNodeView nodeView && nodeView.Node != null)
                    {
                        movedNodes.Add(nodeView);
                    }
                }

                if (movedNodes.Count > 0)
                {
                    if (_tree.Nodes == null)
                    {
                        return graphviewChange;
                    }

                    var movedNodeSet = new HashSet<BTNode>();
                    var affectedParents = new HashSet<CompositeNode>();
                    var undoTargetList = new List<UnityEngine.Object>(movedNodes.Count * 2);
                    for (int i = 0; i < movedNodes.Count; i++)
                    {
                        BTNode movedNode = movedNodes[i].Node;
                        if (movedNodeSet.Add(movedNode))
                        {
                            undoTargetList.Add(movedNode);
                        }
                    }

                    for (int i = 0; i < _tree.Nodes.Count; i++)
                    {
                        if (!(_tree.Nodes[i] is CompositeNode composite))
                        {
                            continue;
                        }

                        if (composite.Children == null)
                        {
                            continue;
                        }

                        for (int childIndex = 0; childIndex < composite.Children.Count; childIndex++)
                        {
                            if (movedNodeSet.Contains(composite.Children[childIndex]))
                            {
                                if (affectedParents.Add(composite))
                                {
                                    if (!movedNodeSet.Contains(composite))
                                    {
                                        undoTargetList.Add(composite);
                                    }
                                }

                                break;
                            }
                        }
                    }

                    Undo.RecordObjects(undoTargetList.ToArray(), "Move Behavior Tree Nodes");
                    for (int i = 0; i < movedNodes.Count; i++)
                    {
                        BTNodeView nodeView = movedNodes[i];
                        nodeView.Node.Position = nodeView.GetPosition().position;
                        EditorUtility.SetDirty(nodeView.Node);
                    }

                    foreach (CompositeNode parent in affectedParents)
                    {
                        _tree.NormalizeCompositeChildren(parent);
                    }

                    EditorUtility.SetDirty(_tree);
                }
            }

            return graphviewChange;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            var mousePosition = viewTransform.matrix.inverse.MultiplyPoint(evt.localMousePosition);

            var actionTypes = TypeCache.GetTypesDerivedFrom<ActionNode>();
            var conditionTypes = TypeCache.GetTypesDerivedFrom<ConditionNode>();
            var compositeTypes = TypeCache.GetTypesDerivedFrom<CompositeNode>();
            var decoratorTypes = TypeCache.GetTypesDerivedFrom<DecoratorNode>();

            var sortedActionTypes = new List<Type>(actionTypes);
            sortedActionTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedActionTypes)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction(
                    $"ActionNode/{category}/{type.Name}",
                    a => CreateNode(type, mousePosition),
                    GetAuthoringActionStatus);
            }

            var sortedConditionTypes = new List<Type>(conditionTypes);
            sortedConditionTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedConditionTypes)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction(
                    $"ConditionNode/{category}/{type.Name}",
                    a => CreateNode(type, mousePosition),
                    GetAuthoringActionStatus);
            }

            var sortedCompositeTypes = new List<Type>(compositeTypes);
            sortedCompositeTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedCompositeTypes)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction(
                    $"CompositeNode/{category}/{type.Name}",
                    a => CreateNode(type, mousePosition),
                    GetAuthoringActionStatus);
            }

            var sortedDecoratorTypes = new List<Type>(decoratorTypes);
            sortedDecoratorTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedDecoratorTypes)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction(
                    $"DecoratorNode/{category}/{type.Name}",
                    a => CreateNode(type, mousePosition),
                    GetAuthoringActionStatus);
            }
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Sort Nodes", a => SortNodes(), GetAuthoringActionStatus);
            evt.menu.AppendAction("Return to Root", a => ReturnToRoot());
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Find Tree", a => EditorGUIUtility.PingObject(_tree));

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Copy", a => CopySelectedNodes(mousePosition));
            if (_copiedNodes.Count > 0)
            {
                evt.menu.AppendAction("Paste", a => PasteCopiedNodes(mousePosition), GetAuthoringActionStatus);
            }
        }

        private DropdownMenuAction.Status GetAuthoringActionStatus(DropdownMenuAction action)
        {
            return AuthoringWritesBlocked
                ? DropdownMenuAction.Status.Disabled
                : DropdownMenuAction.Status.Normal;
        }
        /// <summary>
        /// Copies selected nodes for pasting.
        /// </summary>
        private void CopySelectedNodes(Vector2 mousePosition)
        {
            _copiedNodes.Clear();
            _copiedTreePosition = mousePosition;
            var selectedNodes = selection.OfType<BTNodeView>().Select(n => n.Node).ToList();
            if (selectedNodes.Any(node => node is BTRootNode))
            {
                EditorUtility.DisplayDialog("Error", "Cannot copy root node", "OK");
                return;
            }

            _copiedNodes.AddRange(selectedNodes);
        }

        /// <summary>
        /// Pastes copied nodes to the tree at the specified position.
        /// </summary>
        private void PasteCopiedNodes(Vector2 mousePosition)
        {
            if (AuthoringWritesBlocked || _tree == null || _copiedNodes.Count == 0)
            {
                return;
            }

            Vector2 offset = mousePosition - _copiedTreePosition;
            var nodeMap = new Dictionary<BTNode, BTNode>(_copiedNodes.Count);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paste Behavior Tree Nodes");
            var createdNodes = new List<BTNode>(_copiedNodes.Count);
            bool assetSynchronizationStarted = false;
            try
            {
                Undo.RegisterCompleteObjectUndo(_tree, "Paste Behavior Tree Nodes");

                for (int i = 0; i < _copiedNodes.Count; i++)
                {
                    BTNode copiedNode = _copiedNodes[i];
                    if (copiedNode == null || copiedNode is BTRootNode)
                    {
                        continue;
                    }

                    var newNode = ScriptableObject.CreateInstance(copiedNode.GetType()) as BTNode;
                    if (newNode == null)
                    {
                        throw new InvalidOperationException(
                            $"Unity could not create copied node type '{copiedNode.GetType().FullName}'.");
                    }

                    createdNodes.Add(newNode);
                    EditorUtility.CopySerialized(copiedNode, newNode);
                    newNode.Tree = _tree;
                    newNode.Position = copiedNode.Position + offset;
                    newNode.GUID = Guid.NewGuid().ToString();

                    if (newNode is DecoratorNode newDecorator)
                    {
                        newDecorator.Child = null;
                    }

                    _tree.AddNode(newNode);
                    nodeMap.Add(copiedNode, newNode);
                }

                foreach (KeyValuePair<BTNode, BTNode> pair in nodeMap)
                {
                    Undo.RegisterCompleteObjectUndo(pair.Value, "Paste Behavior Tree Nodes");
                    if (pair.Key is CompositeNode && pair.Value is CompositeNode targetComposite)
                    {
                        if (targetComposite.Children == null)
                        {
                            targetComposite.Children = new List<BTNode>();
                        }
                        else
                        {
                            var externalChildren = new List<BTNode>();
                            for (int i = 0; i < targetComposite.Children.Count; i++)
                            {
                                BTNode sourceChild = targetComposite.Children[i];
                                if (sourceChild != null && nodeMap.TryGetValue(sourceChild, out BTNode targetChild))
                                {
                                    targetComposite.Children[i] = targetChild;
                                }
                                else if (sourceChild != null)
                                {
                                    externalChildren.Add(sourceChild);
                                }
                            }

                            for (int i = 0; i < externalChildren.Count; i++)
                            {
                                _tree.RemoveChild(targetComposite, externalChildren[i]);
                            }
                        }

                        _tree.NormalizeCompositeChildren(targetComposite);
                    }
                    else if (pair.Key is DecoratorNode sourceDecorator &&
                             pair.Value is DecoratorNode targetDecorator &&
                             sourceDecorator.Child != null &&
                             nodeMap.TryGetValue(sourceDecorator.Child, out BTNode targetChild))
                    {
                        targetDecorator.Child = targetChild;
                    }

                    EditorUtility.SetDirty(pair.Value);
                }

                EditorUtility.SetDirty(_tree);
                assetSynchronizationStarted = EditorUtility.IsPersistent(_tree);
                BehaviorTreeAuthoringUtility.SynchronizePersistentAsset(_tree);
                List<string> ownershipIssues = BehaviorTreeAuthoringUtility.ValidatePersistentAsset(_tree);
                if (ownershipIssues.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Pasted nodes failed authoring ownership validation:\n" + string.Join("\n", ownershipIssues));
                }

                Undo.CollapseUndoOperations(undoGroup);
                DrawGraph();
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                CleanupFailedPasteNodes(createdNodes);
                if (assetSynchronizationStarted)
                {
                    BehaviorTreeAuthoringUtility.SynchronizePersistentAsset(_tree);
                }

                DrawGraph();
                Debug.LogError($"[BehaviorTree] Paste transaction was rolled back: {exception.Message}", _tree);
            }
        }

        private static void CleanupFailedPasteNodes(List<BTNode> createdNodes)
        {
            for (int i = 0; i < createdNodes.Count; i++)
            {
                BTNode node = createdNodes[i];
                if (node == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(node)))
                {
                    AssetDatabase.RemoveObjectFromAsset(node);
                }

                if (node != null)
                {
                    UnityEngine.Object.DestroyImmediate(node);
                }
            }
        }
        /// <summary>
        /// Centers the view on the root node and resets zoom level.
        /// </summary>
        public void ReturnToRoot()
        {
            if (_tree == null || _tree.Root == null || string.IsNullOrEmpty(_tree.Root.GUID))
            {
                return;
            }

            var root = GetNodeByGuid(_tree.Root.GUID) as BTNodeView;
            if (root != null)
            {
                viewTransform.scale = Vector3.one;
                viewTransform.position = -root.GetPosition().position;
            }
        }
        internal BTNodeView FindNodeView(BTNode node)
        {
            return GetNodeByGuid(node.GUID) as BTNodeView;
        }

        private void ApplySearchFilter()
        {
            bool hasFilter = !string.IsNullOrWhiteSpace(_searchFilter);

            RefreshCachedNodeList();
            for (int i = 0; i < _cachedNodeList.Count; i++)
            {
                if (_cachedNodeList[i] is BTNodeView nodeView)
                {
                    bool isMatch = nodeView.MatchesSearch(_searchFilter);
                    nodeView.SetSearchState(isMatch, hasFilter);
                }
            }

            foreach (var element in graphElements)
            {
                if (element is Edge edge &&
                    edge.output?.node is BTNodeView parentView &&
                    edge.input?.node is BTNodeView childView)
                {
                    bool parentMatch = parentView.MatchesSearch(_searchFilter);
                    bool childMatch = childView.MatchesSearch(_searchFilter);
                    edge.style.opacity = !hasFilter || parentMatch || childMatch ? 1f : 0.15f;
                }
            }
        }

        private HashSet<BTNode> CollectReachableNodes()
        {
            var visited = new HashSet<BTNode>();
            BehaviorTreeAuthoringUtility.CollectReachableNodes(_tree, visited);
            return visited;
        }

        private bool IsReachable(BTNode start, BTNode target)
        {
            if (start == null || target == null)
            {
                return false;
            }

            var visited = new HashSet<BTNode>();
            var stack = new Stack<BTNode>();
            stack.Push(start);

            while (stack.Count > 0)
            {
                BTNode node = stack.Pop();
                if (node == null || !visited.Add(node))
                {
                    continue;
                }

                if (node == target)
                {
                    return true;
                }

                int childCount = BehaviorTreeAuthoringUtility.GetChildCount(node);
                for (int i = 0; i < childCount; i++)
                {
                    BTNode child = BehaviorTreeAuthoringUtility.GetChildAt(node, i);
                    if (child != null)
                    {
                        stack.Push(child);
                    }
                }
            }

            return false;
        }

        private int CountParents(BTNode child)
        {
            int count = 0;
            if (_tree?.Nodes == null)
            {
                return count;
            }

            for (int i = 0; i < _tree.Nodes.Count; i++)
            {
                BTNode candidate = _tree.Nodes[i];
                if (candidate != null && HasDirectChild(candidate, child))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasDirectChild(BTNode parent, BTNode child)
        {
            if (parent is BTRootNode root)
            {
                return root.Child == child;
            }

            if (parent is DecoratorNode decorator)
            {
                return decorator.Child == child;
            }

            if (parent is CompositeNode composite)
            {
                return composite.Children != null && composite.Children.Contains(child);
            }

            return false;
        }

        private static int GetDirectChildCount(BTNode parent)
        {
            if (parent is BTRootNode root)
            {
                return root.Child == null ? 0 : 1;
            }

            if (parent is DecoratorNode decorator)
            {
                return decorator.Child == null ? 0 : 1;
            }

            if (parent is CompositeNode composite)
            {
                if (composite.Children == null)
                {
                    return 0;
                }

                int count = 0;
                for (int i = 0; i < composite.Children.Count; i++)
                {
                    if (composite.Children[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }

            return 0;
        }

        #region Sort Nodes
        /// <summary>
        /// Sort all nodes in the tree
        /// </summary>
        public void SortNodes()
        {
            if (AuthoringWritesBlocked || _tree == null || _tree.Root == null || _tree.Nodes == null) return;

            var subtreeWidths = new Dictionary<BTNode, float>();
            var visiting = new HashSet<BTNode>();
            var visited = new HashSet<BTNode>();
            if (!TryMeasureSubtree(_tree.Root, subtreeWidths, visiting, visited, 0, out string error))
            {
                EditorUtility.DisplayDialog("Behavior Tree Layout", error, "OK");
                return;
            }

            var undoTargets = new UnityEngine.Object[subtreeWidths.Count];
            int undoIndex = 0;
            foreach (BTNode node in subtreeWidths.Keys)
            {
                undoTargets[undoIndex++] = node;
            }

            Undo.RecordObjects(undoTargets, "Layout Behavior Tree");
            var positioned = new HashSet<BTNode>();
            LayoutSubtree(Vector2.zero, _tree.Root, subtreeWidths, positioned);

            foreach (BTNode node in positioned)
            {
                BTNodeView nodeView = FindNodeView(node);
                if (nodeView != null)
                {
                    nodeView.SetPosition(new Rect(node.Position, Vector2.zero));
                }

                EditorUtility.SetDirty(node);
            }

            foreach (BTNode node in positioned)
            {
                if (node is CompositeNode composite)
                {
                    _tree.NormalizeCompositeChildren(composite);
                }
            }

            EditorUtility.SetDirty(_tree);
        }

        /// <summary>
        /// Measures each reachable subtree once and rejects graphs that are unsafe to lay out.
        /// </summary>
        private bool TryMeasureSubtree(
            BTNode node,
            Dictionary<BTNode, float> subtreeWidths,
            HashSet<BTNode> visiting,
            HashSet<BTNode> visited,
            int depth,
            out string error)
        {
            const int maxLayoutDepth = 1024;
            if (node == null)
            {
                error = "The reachable graph contains a null node reference.";
                return false;
            }

            if (depth > maxLayoutDepth)
            {
                error = $"The graph exceeds the layout depth limit of {maxLayoutDepth}.";
                return false;
            }

            if (!visiting.Add(node))
            {
                error = $"A cycle was detected at '{node.name}'.";
                return false;
            }

            if (visited.Contains(node))
            {
                visiting.Remove(node);
                error = $"'{node.name}' is referenced by more than one parent.";
                return false;
            }

            int childCount = BehaviorTreeAuthoringUtility.GetChildCount(node);
            float totalWidth = 0f;
            for (int i = 0; i < childCount; i++)
            {
                BTNode child = BehaviorTreeAuthoringUtility.GetChildAt(node, i);
                if (!TryMeasureSubtree(
                        child,
                        subtreeWidths,
                        visiting,
                        visited,
                        depth + 1,
                        out error))
                {
                    visiting.Remove(node);
                    return false;
                }

                totalWidth += subtreeWidths[child];
            }

            visiting.Remove(node);
            visited.Add(node);
            subtreeWidths[node] = Mathf.Max(totalWidth, NODE_X_GAP);
            error = null;
            return true;
        }

        private void LayoutSubtree(
            Vector2 position,
            BTNode node,
            Dictionary<BTNode, float> subtreeWidths,
            HashSet<BTNode> positioned)
        {
            if (node == null || !positioned.Add(node))
            {
                return;
            }

            int childCount = BehaviorTreeAuthoringUtility.GetChildCount(node);
            if (childCount == 0)
            {
                node.Position = position;
                return;
            }

            float totalWidth = subtreeWidths[node];
            float currentX = position.x;
            for (int i = 0; i < childCount; i++)
            {
                BTNode child = BehaviorTreeAuthoringUtility.GetChildAt(node, i);
                LayoutSubtree(new Vector2(currentX, position.y + NODE_Y_GAP), child, subtreeWidths, positioned);
                currentX += subtreeWidths[child];
            }

            float parentX = position.x + totalWidth * 0.5f - NODE_X_GAP * 0.5f;
            node.Position = new Vector2(parentX, position.y);
        }
        #endregion

        private bool TryCreateRepairPlan(out AuthoringRepairPlan plan, out string message)
        {
            plan = null;
            string treePath = AssetDatabase.GetAssetPath(_tree);
            bool isPersistent = EditorUtility.IsPersistent(_tree);
            if (isPersistent && string.IsNullOrEmpty(treePath))
            {
                message = "The persistent tree has no AssetDatabase path.";
                return false;
            }

            if (!isPersistent && !string.IsNullOrEmpty(treePath))
            {
                message = $"The tree asset state is inconsistent for path '{treePath}'.";
                return false;
            }

            var orderedNodes = new List<BTNode>();
            var knownNodes = new HashSet<BTNode>();
            AddRepairNode(_tree.Root, orderedNodes, knownNodes);
            if (_tree.Nodes != null)
            {
                for (int i = 0; i < _tree.Nodes.Count; i++)
                {
                    AddRepairNode(_tree.Nodes[i], orderedNodes, knownNodes);
                }
            }

            var reachableNodes = new HashSet<BTNode>();
            var traversalOrder = new List<BTNode>();
            BehaviorTreeAuthoringUtility.CollectReachableNodes(_tree, reachableNodes, traversalOrder);
            for (int i = 0; i < traversalOrder.Count; i++)
            {
                AddRepairNode(traversalOrder[i], orderedNodes, knownNodes);
            }

            if (isPersistent)
            {
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(treePath);
                var discoveredNodes = new List<BTNode>();
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] is BTNode node)
                    {
                        discoveredNodes.Add(node);
                    }
                }

                discoveredNodes.Sort(CompareAuthoringNodes);
                for (int i = 0; i < discoveredNodes.Count; i++)
                {
                    AddRepairNode(discoveredNodes[i], orderedNodes, knownNodes);
                }
            }

            var transientNodes = new List<BTNode>();
            for (int i = 0; i < orderedNodes.Count; i++)
            {
                BTNode node = orderedNodes[i];
                string nodePath = AssetDatabase.GetAssetPath(node);
                if (!isPersistent)
                {
                    if (!string.IsNullOrEmpty(nodePath) || EditorUtility.IsPersistent(node))
                    {
                        message =
                            $"Repair refused foreign persistent node '{DescribeAuthoringNode(node)}' in a transient tree.";
                        return false;
                    }

                    if (node.Tree != null && node.Tree != _tree)
                    {
                        message =
                            $"Repair refused transient node '{DescribeAuthoringNode(node)}' because another tree owns it.";
                        return false;
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(nodePath))
                {
                    if (node.Tree != null && node.Tree != _tree)
                    {
                        message =
                            $"Repair refused transient node '{DescribeAuthoringNode(node)}' because another tree owns it.";
                        return false;
                    }

                    transientNodes.Add(node);
                    continue;
                }

                if (!string.Equals(nodePath, treePath, StringComparison.Ordinal))
                {
                    message =
                        $"Repair refused foreign node '{DescribeAuthoringNode(node)}' from asset '{nodePath}'.";
                    return false;
                }

                // AddObjectToAsset exposes the correct path before IsSubAsset becomes true.
                // The transaction performs a synchronous import before final ownership validation.
            }

            var nullCompositeChildLists = new List<CompositeNode>();
            for (int i = 0; i < orderedNodes.Count; i++)
            {
                if (orderedNodes[i] is CompositeNode composite && composite.Children == null)
                {
                    nullCompositeChildLists.Add(composite);
                }
            }

            var undoTargets = new List<UnityEngine.Object>(orderedNodes.Count + 1) { _tree };
            for (int i = 0; i < orderedNodes.Count; i++)
            {
                undoTargets.Add(orderedNodes[i]);
            }

            plan = new AuthoringRepairPlan(
                isPersistent,
                orderedNodes,
                transientNodes,
                undoTargets,
                _tree.Nodes == null,
                nullCompositeChildLists);
            message = null;
            return true;
        }

        private static void AddRepairNode(
            BTNode node,
            List<BTNode> orderedNodes,
            HashSet<BTNode> knownNodes)
        {
            if (node != null && knownNodes.Add(node))
            {
                orderedNodes.Add(node);
            }
        }

        private static void DetachFailedRepairNodes(List<BTNode> attachedNodes)
        {
            for (int i = 0; i < attachedNodes.Count; i++)
            {
                BTNode node = attachedNodes[i];
                if (node != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(node)))
                {
                    AssetDatabase.RemoveObjectFromAsset(node);
                }
            }
        }

        private void RestoreFailedRepairNullCollections(AuthoringRepairPlan plan)
        {
            if (plan.NodeRegistryWasNull)
            {
                _tree.Nodes = null;
            }

            for (int i = 0; i < plan.NullCompositeChildLists.Count; i++)
            {
                CompositeNode composite = plan.NullCompositeChildLists[i];
                if (composite != null)
                {
                    composite.Children = null;
                }
            }
        }

        private static string DescribeAuthoringNode(BTNode node)
        {
            return node == null || string.IsNullOrEmpty(node.name)
                ? node?.GetType().Name ?? "<null>"
                : node.name;
        }

        private sealed class AuthoringRepairPlan
        {
            internal AuthoringRepairPlan(
                bool isPersistent,
                List<BTNode> orderedNodes,
                List<BTNode> transientNodes,
                List<UnityEngine.Object> undoTargets,
                bool nodeRegistryWasNull,
                List<CompositeNode> nullCompositeChildLists)
            {
                IsPersistent = isPersistent;
                OrderedNodes = orderedNodes;
                TransientNodes = transientNodes;
                UndoTargets = undoTargets;
                NodeRegistryWasNull = nodeRegistryWasNull;
                NullCompositeChildLists = nullCompositeChildLists;
            }

            internal bool IsPersistent { get; }
            internal List<BTNode> OrderedNodes { get; }
            internal List<BTNode> TransientNodes { get; }
            internal List<UnityEngine.Object> UndoTargets { get; }
            internal bool NodeRegistryWasNull { get; }
            internal List<CompositeNode> NullCompositeChildLists { get; }
        }

        private static int CompareAuthoringNodes(BTNode left, BTNode right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            int guidComparison = string.CompareOrdinal(left.GUID ?? string.Empty, right.GUID ?? string.Empty);
            if (guidComparison != 0) return guidComparison;

            int typeComparison = string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
            return typeComparison != 0
                ? typeComparison
                : string.CompareOrdinal(left.name, right.name);
        }

        #region Create Node Methods
        private void CreateNode(Type type, Vector2 position)
        {
            if (AuthoringWritesBlocked || _tree == null)
            {
                return;
            }

            const string undoName = "Create Behavior Tree Node";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            BTNode node = null;
            bool assetSynchronizationStarted = false;
            try
            {
                node = _tree.CreateNode(type);
                if (node == null)
                {
                    throw new InvalidOperationException("The behavior tree node could not be created.");
                }

                Undo.RegisterCompleteObjectUndo(node, undoName);
                node.Position = position;
                EditorUtility.SetDirty(node);
                EditorUtility.SetDirty(_tree);
                assetSynchronizationStarted = EditorUtility.IsPersistent(_tree);
                BehaviorTreeAuthoringUtility.SynchronizePersistentAsset(_tree);
                List<string> ownershipIssues = BehaviorTreeAuthoringUtility.ValidatePersistentAsset(_tree);
                if (ownershipIssues.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Created node failed authoring ownership validation:\n" + string.Join("\n", ownershipIssues));
                }

                CreateNodeView(node);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (node != null)
                {
                    var failedNodes = new List<BTNode>(1) { node };
                    CleanupFailedPasteNodes(failedNodes);
                }

                if (assetSynchronizationStarted)
                {
                    BehaviorTreeAuthoringUtility.SynchronizePersistentAsset(_tree);
                }

                DrawGraph();
                Debug.LogError($"[BehaviorTree] Create transaction was rolled back: {exception.Message}", _tree);
            }
        }
        private void CreateNodeView(BTNode node)
        {
            BTNodeView nodeView = new BTNodeView(node);
            nodeView.SetTreeView(this);
            nodeView.SetAuthoringReadOnly(AuthoringWritesBlocked);
            nodeView.OnNodeSelected += OnNodeSelectionChanged;
            AddElement(nodeView);
        }
        #endregion

        #region Attribute Methods
        /// <summary>
        /// Gets a custom attribute of the specified type from a node type.
        /// </summary>
        private T GetAttribute<T>(Type type) where T : Attribute
        {
            return type.GetCustomAttributes<T>().FirstOrDefault();
        }

        /// <summary>
        /// Gets the category name for a node type from its BTInfoAttribute.
        /// </summary>
        private string GetNodeCategory(Type type)
        {
            var attribute = GetAttribute<BTInfoAttribute>(type);
            return attribute != null ? attribute.Category : "Base";
        }
        #endregion
    }
}
