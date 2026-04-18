using System;
using System.Reflection;
using System.Text;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    /// <summary>
    /// Visual representation of a behavior tree node in the GraphView editor.
    /// Handles node state visualization, port connections, and runtime state synchronization.
    /// </summary>
    public class BTNodeView : Node
    {
        public Action<BTNodeView> OnNodeSelected;
        public BTNode Node => _node;
        private BTNode _node;
        public Port InputPort;
        public Port OutputPort;

        private BehaviorTreeView _treeView;

        // ── Static FieldInfo cache (avoid per-frame reflection) ──
        private static FieldInfo s_debugLogMsgField;
        private static FieldInfo s_waitDurationField;
        private static FieldInfo s_msgPassKeyField;
        private static FieldInfo s_msgPassMsgField;
        private static FieldInfo s_msgRecvKeyField;
        private static FieldInfo s_msgRecvMsgField;
        private static FieldInfo s_retryMaxField;
        private static FieldInfo s_timeoutSecondsField;
        private static FieldInfo s_delaySecondsField;
        private static FieldInfo s_switchKeyField;
        private static FieldInfo s_parallelSuccessField;
        private static FieldInfo s_msgRemoveKeyField;
        private static FieldInfo s_subTreeAssetField;
        private static FieldInfo s_serviceIntervalField;
        private static FieldInfo s_serviceDeviationField;
        private static FieldInfo s_bbCompKeyField;
        private static FieldInfo s_bbCompOpField;
        private static FieldInfo s_bbCompTypeField;
        private static FieldInfo s_utilityScoreKeysField;

        // ── Info text throttle (avoid per-frame string allocs) ──
        private string _cachedInfoText = "";
        private double _lastInfoUpdateTime;
        private const double INFO_UPDATE_INTERVAL = 0.1; // ~10 updates/sec
        private static readonly StringBuilder s_sb = new StringBuilder(128);

        internal void SetTreeView(BehaviorTreeView treeView)
        {
            _treeView = treeView;
        }

        /// <summary>
        /// Gets the runtime node instance if tree is cloned, otherwise returns the original node.
        /// Uses GUID-based matching to find the corresponding runtime node.
        /// </summary>
        private CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode GetRuntimeNode()
        {
            if (_node == null) return null;

            if (!Application.isPlaying) return null;

            if (_treeView != null)
            {
                var runtimeNode = _treeView.GetRuntimeNodeByGuid(_node.GUID);
                if (runtimeNode != null)
                {
                    return runtimeNode;
                }
            }

            // Use cached runners to avoid FindObjectsOfType GC
            var runners = BehaviorTreeView.GetCachedRunners();
            int runnersCount = runners.Count;

            // First, try to get from the tree view if it has a reference to the current runner
            if (_treeView != null && _treeView.Tree != null)
            {
                for (int i = 0; i < runnersCount; i++)
                {
                    var runner = runners[i];
                    if (runner == null || runner.RuntimeTree == null) continue;

                    var runtimeNode = runner.RuntimeTree.GetNodeByGUID(_node.GUID);
                    if (runtimeNode != null)
                    {
                        if (BehaviorTreeView.AreSameTreeAsset(runner.Tree, _treeView.Tree))
                        {
                            return runtimeNode;
                        }
                    }
                }
            }

            // Fallback: Try finding by node tree reference directly
            for (int i = 0; i < runnersCount; i++)
            {
                var runner = runners[i];
                if (runner != null && runner.RuntimeTree != null)
                {
                    if (BehaviorTreeView.AreSameTreeAsset(runner.Tree, _node.Tree))
                    {
                        var runtimeNode = runner.RuntimeTree.GetNodeByGUID(_node.GUID);
                        if (runtimeNode != null) return runtimeNode;
                    }
                }
            }

            return null;
        }
        public CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode RuntimeNode => GetRuntimeNode();

        /// <summary>Returns the runtime state of the tree root (used to distinguish
        /// "tree completed" from "node not yet reached in current iteration").</summary>
        private CycloneGames.BehaviorTree.Runtime.Core.RuntimeState GetTreeRootRuntimeState()
        {
            if (_treeView == null) return CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.NotEntered;
            var runner = _treeView.GetBoundRunner();
            if (runner == null || runner.RuntimeTree == null) return CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.NotEntered;
            return runner.RuntimeTree.State;
        }

        // Helper to avoid repeated lookups if we cache it per frame? 
        // For now calling GetRuntimeNode() is safer as Runner might change.


        private Label _stateLabel;
        private Label _infoLabel;
        private Label _badgeLabel;
        private VisualElement _infoContainer;

        // Progress bar elements for WaitNode and Sequencer/Selector
        private VisualElement _progressBarContainer;
        private VisualElement _progressBarFill;
        private Label _progressLabel;
        private bool _matchesSearch = true;

        /// <summary>
        /// Caches the last known final state (SUCCESS/FAILURE) to preserve node state
        /// even after BehaviorTree.Stop() resets all node states to NOT_ENTERED.
        /// </summary>
        private BTState _lastKnownState = BTState.NOT_ENTERED;

        public BTState GetLastKnownState() => _lastKnownState;

        /// <summary>
        /// Restores a cached final state. Only final states (SUCCESS/FAILURE) are restored
        /// to prevent overwriting completed node states.
        /// </summary>
        public void RestoreLastKnownState(BTState state)
        {
            if (state == BTState.SUCCESS || state == BTState.FAILURE)
            {
                _lastKnownState = state;
            }
        }

        public void ClearStateCache()
        {
            _lastKnownState = BTState.NOT_ENTERED;
        }

        public string SearchText
        {
            get
            {
                string titleText = title ?? string.Empty;
                string typeText = _node != null ? _node.GetType().Name : string.Empty;
                string tooltipText = tooltip ?? string.Empty;
                return $"{titleText} {typeText} {tooltipText}";
            }
        }

        public bool MatchesSearch(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return true;
            return SearchText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void SetSearchState(bool matchesSearch, bool hasActiveFilter)
        {
            _matchesSearch = matchesSearch || !hasActiveFilter;
            style.opacity = _matchesSearch ? 1f : 0.2f;
        }



        public static string ConvertToReadableName(string name)
        {
            name = name.Replace("Node", "").Replace("(Clone)", "").Replace("BT", "");
            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]))
                {
                    name = name.Insert(i, "#");
                    i++;
                }
            }
            name = name.Replace("#", "<br>");
            return name;
        }

        public BTNodeView(BTNode node) : base(AssetDatabase.GetAssetPath(Resources.Load<VisualTreeAsset>("BT_Node_Layout")))
        {
            this._node = node;
            this.title = ConvertToReadableName(node.name);
            this.viewDataKey = node.GUID;
            style.left = node.Position.x;
            style.top = node.Position.y;
            CreateInputPorts();
            CreateOutputPorts();
            SetUpClasses();
            CreateInfoElements();
            SetupTooltip();
            CreateTypeBadge();
        }

        public BTNodeView(BTNode node, Vector2 position) : base(AssetDatabase.GetAssetPath(Resources.Load<VisualTreeAsset>("BT_Node_Layout")))
        {
            this._node = node;
            this.title = ConvertToReadableName(node.name);
            this.viewDataKey = node.GUID;
            node.Position = position;

            style.left = position.x;
            style.top = position.y;

            CreateInputPorts();
            CreateOutputPorts();
            SetUpClasses();
            CreateInfoElements();
            SetupTooltip();
            CreateTypeBadge();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            OnNodeSelected?.Invoke(this);
        }

        /// <summary>
        /// Updates the node position and records it for undo/redo.
        /// </summary>
        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Undo.RecordObject(_node, "Behavior Tree(Set Position)");
            _node.Position = new Vector2(newPos.xMin, newPos.yMin);
            EditorUtility.SetDirty(_node);
        }

        private BTState ToBTState(CycloneGames.BehaviorTree.Runtime.Core.RuntimeState state)
        {
            switch (state)
            {
                case CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success: return BTState.SUCCESS;
                case CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Failure: return BTState.FAILURE;
                case CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running: return BTState.RUNNING;
                default: return BTState.NOT_ENTERED;
            }
        }

        /// <summary>
        /// Updates the visual state of the node based on runtime node state.
        /// Caches final states (SUCCESS/FAILURE) to preserve them after tree stops.
        /// </summary>
        public void UpdateState()
        {
            if (_node == null) return;

            RemoveFromClassList("success");
            RemoveFromClassList("failure");
            RemoveFromClassList("running");
            RemoveFromClassList("not-entered");

            if (!Application.isPlaying)
            {
                if (_stateLabel != null)
                {
                    _stateLabel.text = "";
                    _stateLabel.style.display = DisplayStyle.None;
                }
                if (_infoLabel != null)
                {
                    UpdateInfoLabel();
                }
                return;
            }

            var runtimeNode = GetRuntimeNode();
            if (runtimeNode == null) return;

            BTState currentState = ToBTState(runtimeNode.State);
            bool isStarted = runtimeNode.IsStarted;

            // Note: Composite restart detection logic simplified for now
            // We focus on current state

            if (currentState == BTState.SUCCESS || currentState == BTState.FAILURE)
            {
                _lastKnownState = currentState;
            }
            else if (currentState == BTState.RUNNING && isStarted)
            {
                _lastKnownState = BTState.RUNNING;
            }
            else if (currentState == BTState.NOT_ENTERED && !isStarted)
            {
                // Node not entered: if the tree root is still running, this node
                // hasn't been reached yet in the current iteration (e.g. Repeat
                // restarted children). Reset cached state so stale SUCCESS/FAILURE
                // from a previous iteration doesn't linger.
                var treeRootState = GetTreeRootRuntimeState();
                if (treeRootState == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running)
                {
                    _lastKnownState = BTState.NOT_ENTERED;
                }
                // else: tree completed — keep last known state for post-run display
            }

            UpdateInfoLabel();

            string stateText = "";
            switch (currentState)
            {
                case BTState.SUCCESS:
                    AddToClassList("success");
                    stateText = "SUCCESS";
                    break;
                case BTState.FAILURE:
                    AddToClassList("failure");
                    stateText = "FAILURE";
                    break;
                case BTState.RUNNING:
                    if (isStarted)
                    {
                        AddToClassList("running");
                        stateText = "RUNNING";
                    }
                    else if (_lastKnownState == BTState.SUCCESS || _lastKnownState == BTState.FAILURE)
                    {
                        AddToClassList(_lastKnownState == BTState.SUCCESS ? "success" : "failure");
                        stateText = _lastKnownState == BTState.SUCCESS ? "SUCCESS" : "FAILURE";
                    }
                    else
                    {
                        AddToClassList("not-entered");
                        stateText = "NOT ENTERED";
                    }
                    break;
                default:
                    AddToClassList("not-entered");
                    stateText = "NOT ENTERED";
                    break;
            }

            if (_stateLabel != null)
            {
                _stateLabel.text = stateText;
                _stateLabel.style.display = Application.isPlaying ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateInfoLabel()
        {
            if (_infoLabel == null) return;

            string infoText = GetNodeSpecificInfo();
            if (!string.IsNullOrEmpty(infoText))
            {
                _infoLabel.text = infoText;
                _infoLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _infoLabel.style.display = DisplayStyle.None;
            }

            // Update progress bar for WaitNode
            UpdateProgressBar();
        }

        /// <summary>
        /// Updates the progress bar fill and label for WaitNode during runtime.
        /// </summary>
        private void UpdateProgressBar()
        {
            if (_progressBarContainer == null || _progressBarFill == null || _progressLabel == null)
                return;

            if (!Application.isPlaying)
            {
                _progressBarFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));

                if (_node is WaitNode waitNode)
                {
                    s_waitDurationField ??= typeof(WaitNode).GetField("_duration", BindingFlags.NonPublic | BindingFlags.Instance);
                    var duration = s_waitDurationField != null ? (float)s_waitDurationField.GetValue(waitNode) : 0f;
                    s_sb.Clear(); s_sb.AppendFormat("{0:F1}", duration); s_sb.Append('s');
                    _progressLabel.text = s_sb.ToString();
                    _progressBarContainer.style.display = DisplayStyle.Flex;
                }
                return;
            }

            var runtimeNode = GetRuntimeNode();
            if (runtimeNode is CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeWaitNode waitRuntimeNode)
            {
                float duration = waitRuntimeNode.Duration;
                float elapsed = Time.time - waitRuntimeNode.StartTime;
                float progress = duration > 0 ? Mathf.Clamp01(elapsed / duration) : 0f;

                _progressBarFill.style.width = new StyleLength(new Length(progress * 100f, LengthUnit.Percent));

                var state = waitRuntimeNode.State;
                if (state == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running)
                {
                    _progressBarFill.style.backgroundColor = new Color(0.15f, 0.35f, 0.17f, 0.9f);
                    float remaining = Mathf.Max(0f, duration - elapsed);
                    s_sb.Clear(); s_sb.AppendFormat("{0:F1}", remaining); s_sb.Append("s / "); s_sb.AppendFormat("{0:F1}", duration); s_sb.Append('s');
                    _progressLabel.text = s_sb.ToString();
                }
                else if (state == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                {
                    _progressBarFill.style.backgroundColor = new Color(0.12f, 0.6f, 0.12f, 0.9f);
                    _progressBarFill.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
                    s_sb.Clear(); s_sb.Append("Done ("); s_sb.AppendFormat("{0:F1}", duration); s_sb.Append("s)");
                    _progressLabel.text = s_sb.ToString();
                }
                else
                {
                    _progressBarFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
                    s_sb.Clear(); s_sb.AppendFormat("{0:F1}", duration); s_sb.Append('s');
                    _progressLabel.text = s_sb.ToString();
                }

                _progressBarContainer.style.display = DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// Gets node-specific runtime information for display.
        /// Throttled to avoid per-frame string allocations.
        /// </summary>
        private string GetNodeSpecificInfo()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastInfoUpdateTime < INFO_UPDATE_INTERVAL)
                return _cachedInfoText;
            _lastInfoUpdateTime = now;

            if (!Application.isPlaying)
            {
                _cachedInfoText = GetEditorModeInfo();
                return _cachedInfoText;
            }

            var runtimeNode = GetRuntimeNode();
            _cachedInfoText = runtimeNode == null ? GetEditorModeInfo() : GetRuntimeModeInfo(runtimeNode);
            return _cachedInfoText;
        }

        private string GetEditorModeInfo()
        {
            if (_node == null) return "";

            // Show static configuration based on node type
            switch (_node)
            {
                case DebugLogNode logNode:
                    s_debugLogMsgField ??= typeof(DebugLogNode).GetField("_message", BindingFlags.NonPublic | BindingFlags.Instance);
                    var msg = s_debugLogMsgField?.GetValue(logNode) as string ?? "";
                    s_sb.Clear(); s_sb.Append('"'); s_sb.Append(TruncateText(msg, 20)); s_sb.Append('"');
                    return s_sb.ToString();

                case WaitNode waitNode:
                    s_waitDurationField ??= typeof(WaitNode).GetField("_duration", BindingFlags.NonPublic | BindingFlags.Instance);
                    var duration = s_waitDurationField != null ? (float)s_waitDurationField.GetValue(waitNode) : 0f;
                    s_sb.Clear(); s_sb.Append("Duration: "); s_sb.AppendFormat("{0:F2}", duration); s_sb.Append('s');
                    return s_sb.ToString();

                case Runtime.Nodes.Actions.BlackBoards.MessagePassNode passNode:
                    s_msgPassKeyField ??= typeof(Runtime.Nodes.Actions.BlackBoards.MessagePassNode).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance);
                    s_msgPassMsgField ??= typeof(Runtime.Nodes.Actions.BlackBoards.MessagePassNode).GetField("_message", BindingFlags.NonPublic | BindingFlags.Instance);
                    var keyP = s_msgPassKeyField?.GetValue(passNode) as string ?? "";
                    var msgP = s_msgPassMsgField?.GetValue(passNode) as string ?? "";
                    s_sb.Clear(); s_sb.Append('['); s_sb.Append(keyP); s_sb.Append("] = \""); s_sb.Append(TruncateText(msgP, 15)); s_sb.Append('"');
                    return s_sb.ToString();

                case Runtime.Conditions.BlackBoards.MessageReceiveNode receiveNode:
                    s_msgRecvKeyField ??= typeof(Runtime.Conditions.BlackBoards.MessageReceiveNode).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance);
                    s_msgRecvMsgField ??= typeof(Runtime.Conditions.BlackBoards.MessageReceiveNode).GetField("_message", BindingFlags.NonPublic | BindingFlags.Instance);
                    var keyR = s_msgRecvKeyField?.GetValue(receiveNode) as string ?? "";
                    var msgR = s_msgRecvMsgField?.GetValue(receiveNode) as string ?? "";
                    s_sb.Clear(); s_sb.Append('['); s_sb.Append(keyR); s_sb.Append("] == \""); s_sb.Append(TruncateText(msgR, 15)); s_sb.Append('"');
                    return s_sb.ToString();

                case Runtime.Nodes.Actions.BlackBoards.MessageRemoveNode removeNode:
                    s_msgRemoveKeyField ??= typeof(Runtime.Nodes.Actions.BlackBoards.MessageRemoveNode).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance);
                    var keyRm = s_msgRemoveKeyField?.GetValue(removeNode) as string ?? "";
                    s_sb.Clear(); s_sb.Append("Remove [" ); s_sb.Append(keyRm); s_sb.Append(']');
                    return s_sb.ToString();

                case BBComparisonNode bbCompNode:
                    s_bbCompKeyField ??= typeof(BBComparisonNode).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance);
                    s_bbCompOpField ??= typeof(BBComparisonNode).GetField("_operator", BindingFlags.NonPublic | BindingFlags.Instance);
                    s_bbCompTypeField ??= typeof(BBComparisonNode).GetField("_valueType", BindingFlags.NonPublic | BindingFlags.Instance);
                    var bbKey = s_bbCompKeyField?.GetValue(bbCompNode) as string ?? "";
                    var bbOp = s_bbCompOpField != null ? (CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.BBComparisonOp)s_bbCompOpField.GetValue(bbCompNode) : CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.BBComparisonOp.IsSet;
                    var bbType = s_bbCompTypeField != null ? (CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.BBValueType)s_bbCompTypeField.GetValue(bbCompNode) : CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.BBValueType.Int;
                    s_sb.Clear(); s_sb.Append('['); s_sb.Append(bbKey); s_sb.Append("] "); s_sb.Append(bbOp); s_sb.Append(" ("); s_sb.Append(bbType); s_sb.Append(')');
                    return s_sb.ToString();

                case ServiceNode serviceNode:
                    s_serviceIntervalField ??= typeof(ServiceNode).GetField("_interval", BindingFlags.NonPublic | BindingFlags.Instance);
                    s_serviceDeviationField ??= typeof(ServiceNode).GetField("_randomDeviation", BindingFlags.NonPublic | BindingFlags.Instance);
                    var svcInterval = s_serviceIntervalField != null ? (float)s_serviceIntervalField.GetValue(serviceNode) : 0.5f;
                    var svcDeviation = s_serviceDeviationField != null ? (float)s_serviceDeviationField.GetValue(serviceNode) : 0f;
                    s_sb.Clear(); s_sb.Append("Every "); s_sb.AppendFormat("{0:F2}", svcInterval); s_sb.Append('s');
                    if (svcDeviation > 0f) { s_sb.Append(" ±"); s_sb.AppendFormat("{0:F2}", svcDeviation); }
                    return s_sb.ToString();

                case BlackBoardNode _:
                    return "BB Scope";

                case RepeatNode repeatNode:
                    if (repeatNode.RepeatForever)
                        return "Repeat: Forever";
                    else if (repeatNode.UseRandomRepeatCount)
                        return "Repeat: Random";
                    else
                        return $"Repeat: (configured)";

                case RetryNode retryNode:
                    s_retryMaxField ??= typeof(RetryNode).GetField("_maxAttempts", BindingFlags.NonPublic | BindingFlags.Instance);
                    var maxAttempts = s_retryMaxField != null ? (int)s_retryMaxField.GetValue(retryNode) : 3;
                    s_sb.Clear(); s_sb.Append("Max Attempts: "); s_sb.Append(maxAttempts);
                    return s_sb.ToString();

                case TimeoutNode timeoutNode:
                    s_timeoutSecondsField ??= typeof(TimeoutNode).GetField("_timeoutSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
                    var timeout = s_timeoutSecondsField != null ? (float)s_timeoutSecondsField.GetValue(timeoutNode) : 5f;
                    s_sb.Clear(); s_sb.Append("Timeout: "); s_sb.AppendFormat("{0:F1}", timeout); s_sb.Append('s');
                    return s_sb.ToString();

                case DelayNode delayNode:
                    s_delaySecondsField ??= typeof(DelayNode).GetField("_delaySeconds", BindingFlags.NonPublic | BindingFlags.Instance);
                    var delay = s_delaySecondsField != null ? (float)s_delaySecondsField.GetValue(delayNode) : 1f;
                    s_sb.Clear(); s_sb.Append("Delay: "); s_sb.AppendFormat("{0:F1}", delay); s_sb.Append('s');
                    return s_sb.ToString();

                case ForceFailureNode _:
                    return "→ FAILURE";

                case RunOnceNode _:
                    return "Run Once";

                case KeepRunningUntilFailureNode _:
                    return "Until Failure";

                case SubTreeNode subTreeNode:
                    s_subTreeAssetField ??= typeof(SubTreeNode).GetField("_subTreeAsset", BindingFlags.NonPublic | BindingFlags.Instance);
                    var subAsset = s_subTreeAssetField?.GetValue(subTreeNode) as Runtime.BehaviorTree;
                    if (subAsset != null)
                    { s_sb.Clear(); s_sb.Append("Tree: "); s_sb.Append(subAsset.name); return s_sb.ToString(); }
                    return "SubTree: (none)";

                case SwitchNode switchNode:
                    s_switchKeyField ??= typeof(SwitchNode).GetField("_variableKey", BindingFlags.NonPublic | BindingFlags.Instance);
                    var key = s_switchKeyField?.GetValue(switchNode) as string ?? "";
                    if (string.IsNullOrEmpty(key)) return "Switch";
                    s_sb.Clear(); s_sb.Append("Key: "); s_sb.Append(key);
                    return s_sb.ToString();

                case ParallelAllNode parallelAll:
                    s_parallelSuccessField ??= typeof(ParallelAllNode).GetField("_successThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
                    var st = s_parallelSuccessField != null ? (int)s_parallelSuccessField.GetValue(parallelAll) : -1;
                    if (st < 0) return "All must succeed";
                    s_sb.Clear(); s_sb.Append("Need "); s_sb.Append(st); s_sb.Append(" success");
                    return s_sb.ToString();

                case UtilitySelectorNode utilNode:
                    s_utilityScoreKeysField ??= typeof(UtilitySelectorNode).GetField("_scoreKeys", BindingFlags.NonPublic | BindingFlags.Instance);
                    var scoreKeys = s_utilityScoreKeysField?.GetValue(utilNode) as System.Collections.Generic.List<string>;
                    int keysCnt = scoreKeys != null ? scoreKeys.Count : 0;
                    s_sb.Clear(); s_sb.Append("Utility ("); s_sb.Append(keysCnt); s_sb.Append(" keys)");
                    return s_sb.ToString();

                case CompositeNode composite:
                    s_sb.Clear(); s_sb.Append("Children: "); s_sb.Append(composite.Children.Count);
                    return s_sb.ToString();

                case DecoratorNode decorator:
                    return decorator.Child != null ? "Has Child" : "No Child";
            }

            return "";
        }

        private string GetRuntimeModeInfo(CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode node)
        {
            switch (node)
            {
                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeWaitNode waitNode:
                    float currentTime = waitNode.UseUnscaledTime ? Time.unscaledTime : Time.time;
                    float time = (currentTime - waitNode.StartTime);
                    float actualDuration = waitNode.ActualDuration;

                    if (waitNode.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running)
                    {
                        float remaining = Mathf.Max(0f, actualDuration - time);
                        float progress = actualDuration > 0 ? Mathf.Clamp01(time / actualDuration) * 100f : 0f;
                        s_sb.Clear(); s_sb.Append("Remaining: "); s_sb.AppendFormat("{0:F2}", remaining); s_sb.Append("s ("); s_sb.AppendFormat("{0:F0}", progress); s_sb.Append("%)");
                        return s_sb.ToString();
                    }
                    else if (waitNode.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                    {
                        s_sb.Clear(); s_sb.Append("Completed: "); s_sb.AppendFormat("{0:F2}", actualDuration); s_sb.Append('s');
                        return s_sb.ToString();
                    }
                    s_sb.Clear(); s_sb.Append("Duration: "); s_sb.AppendFormat("{0:F2}", actualDuration); s_sb.Append('s');
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeDebugLogNode logNode:
                    s_sb.Clear(); s_sb.Append('"'); s_sb.Append(TruncateText(logNode.Message, 20)); s_sb.Append('"');
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeMessagePassNode passNode:
                    s_sb.Clear(); s_sb.Append('['); s_sb.Append(passNode.KeyHash); s_sb.Append("] = \""); s_sb.Append(TruncateText(passNode.Message, 15)); s_sb.Append('"');
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRepeatNode repeatNode:
                    s_sb.Clear();
                    if (repeatNode.RepeatForever)
                    { s_sb.Append("Count: "); s_sb.Append(repeatNode.CurrentRepeatCount + 1); }
                    else if (repeatNode.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                    { s_sb.Append(repeatNode.RepeatCount); s_sb.Append('/'); s_sb.Append(repeatNode.RepeatCount); }
                    else
                    { s_sb.Append(repeatNode.CurrentRepeatCount + 1); s_sb.Append('/'); s_sb.Append(repeatNode.RepeatCount); }
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeWaitSuccessNode waitSuccessNode:
                    s_sb.Clear(); s_sb.Append("Fail After: "); s_sb.AppendFormat("{0:F2}", waitSuccessNode.ActualWaitTime); s_sb.Append('s');
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSequencer sequencer:
                    s_sb.Clear();
                    if (sequencer.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                    { s_sb.Append("Done "); s_sb.Append(sequencer.ChildCount); s_sb.Append('/'); s_sb.Append(sequencer.ChildCount); }
                    else if (sequencer.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Failure)
                    { s_sb.Append("Failed "); s_sb.Append(sequencer.CurrentIndex + 1); s_sb.Append('/'); s_sb.Append(sequencer.ChildCount); }
                    else
                    { s_sb.Append("Current: "); s_sb.Append(sequencer.CurrentIndex + 1); s_sb.Append('/'); s_sb.Append(sequencer.ChildCount); }
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSelector selector:
                    s_sb.Clear();
                    if (selector.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                    { s_sb.Append("Found "); s_sb.Append(selector.CurrentIndex + 1); s_sb.Append('/'); s_sb.Append(selector.ChildCount); }
                    else if (selector.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Failure)
                    { s_sb.Append("Exhausted "); s_sb.Append(selector.ChildCount); s_sb.Append('/'); s_sb.Append(selector.ChildCount); }
                    else
                    { s_sb.Append("Trying: "); s_sb.Append(selector.CurrentIndex + 1); s_sb.Append('/'); s_sb.Append(selector.ChildCount); }
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRetryNode retryNode:
                    s_sb.Clear(); s_sb.Append("Attempt: "); s_sb.Append(retryNode.MaxAttempts);
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeTimeoutNode timeoutNode:
                    s_sb.Clear(); s_sb.Append("Timeout: "); s_sb.AppendFormat("{0:F1}", timeoutNode.TimeoutSeconds); s_sb.Append('s');
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeDelayNode delayNode:
                    s_sb.Clear(); s_sb.Append("Delay: "); s_sb.AppendFormat("{0:F1}", delayNode.DelaySeconds); s_sb.Append('s');
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeForceFailureNode _:
                    return "\u2192 FAILURE";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRunOnceNode _:
                    return "Run Once";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeKeepRunningUntilFailureNode _:
                    return "Until Failure";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeParallelAllNode parallelAll:
                    if (parallelAll.SuccessThreshold < 0) return "All must succeed";
                    s_sb.Clear(); s_sb.Append("Need "); s_sb.Append(parallelAll.SuccessThreshold); s_sb.Append(" success");
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeBBComparisonNode bbCompRt:
                    s_sb.Clear(); s_sb.Append(bbCompRt.Operator.ToString());
                    if (bbCompRt.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Failure)
                        s_sb.Append(" ✗");
                    else if (bbCompRt.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                        s_sb.Append(" ✓");
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeServiceNode serviceRt:
                    s_sb.Clear(); s_sb.Append("Service "); s_sb.AppendFormat("{0:F2}", serviceRt.Interval); s_sb.Append('s');
                    return s_sb.ToString();

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeUtilitySelector utilRt:
                    s_sb.Clear();
                    if (utilRt.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running)
                    { s_sb.Append("Best: "); s_sb.Append(utilRt.CurrentIndex + 1); s_sb.Append('/'); s_sb.Append(utilRt.ChildCount); }
                    else
                    { s_sb.Append("Children: "); s_sb.Append(utilRt.ChildCount); }
                    return s_sb.ToString();
            }

            return GetEditorModeInfo();
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Creates visual elements for displaying node state and runtime information.
        /// </summary>
        private void CreateInfoElements()
        {
            var topContainer = this.Q<VisualElement>("top");
            var titleContainer = this.Q<VisualElement>("title");
            if (topContainer == null || titleContainer == null) return;

            // State label goes into #top between #input and #title (its own row)
            _stateLabel = new Label
            {
                name = "state-label",
                text = ""
            };
            _stateLabel.AddToClassList("state-label");
            _stateLabel.style.display = DisplayStyle.None;
            int titleIndex = topContainer.IndexOf(titleContainer);
            topContainer.Insert(titleIndex, _stateLabel);

            _infoContainer = new VisualElement
            {
                name = "info-container"
            };
            _infoContainer.AddToClassList("info-container");

            _infoLabel = new Label
            {
                name = "info-label",
                text = ""
            };
            _infoLabel.AddToClassList("info-label");
            _infoContainer.Add(_infoLabel);

            // Create progress bar for WaitNode
            if (_node is WaitNode)
            {
                CreateProgressBar();
            }

            // Add info container to appropriate location
            var contents = this.Q<VisualElement>("contents");
            if (contents != null)
            {
                contents.Add(_infoContainer);
            }

            // Initial info update for editor mode
            UpdateInfoLabel();
        }

        /// <summary>
        /// Creates a stylish progress bar for WaitNode with background, fill, and label.
        /// </summary>
        private void CreateProgressBar()
        {
            _progressBarContainer = new VisualElement { name = "progress-bar-container" };
            _progressBarContainer.style.height = 16;
            _progressBarContainer.style.marginTop = 4;
            _progressBarContainer.style.marginBottom = 4;
            _progressBarContainer.style.marginLeft = 6;
            _progressBarContainer.style.marginRight = 6;
            _progressBarContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            _progressBarContainer.style.borderTopLeftRadius = 6;
            _progressBarContainer.style.borderTopRightRadius = 6;
            _progressBarContainer.style.borderBottomLeftRadius = 6;
            _progressBarContainer.style.borderBottomRightRadius = 6;
            _progressBarContainer.style.borderTopWidth = 1;
            _progressBarContainer.style.borderBottomWidth = 1;
            _progressBarContainer.style.borderLeftWidth = 1;
            _progressBarContainer.style.borderRightWidth = 1;
            _progressBarContainer.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _progressBarContainer.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _progressBarContainer.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _progressBarContainer.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _progressBarContainer.style.overflow = Overflow.Hidden;

            _progressBarFill = new VisualElement { name = "progress-bar-fill" };
            _progressBarFill.style.position = Position.Absolute;
            _progressBarFill.style.left = 0;
            _progressBarFill.style.top = 0;
            _progressBarFill.style.bottom = 0;
            _progressBarFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
            _progressBarFill.style.backgroundColor = new Color(0.27f, 0.56f, 0.29f, 1f); // Green
            _progressBarFill.style.borderTopLeftRadius = 3;
            _progressBarFill.style.borderBottomLeftRadius = 3;

            _progressLabel = new Label { name = "progress-label", text = "0%" };
            _progressLabel.style.position = Position.Absolute;
            _progressLabel.style.left = 0;
            _progressLabel.style.right = 0;
            _progressLabel.style.top = 0;
            _progressLabel.style.bottom = 0;
            _progressLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _progressLabel.style.fontSize = 10;
            _progressLabel.style.color = new Color(1f, 1f, 1f, 0.9f);
            _progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _progressBarContainer.Add(_progressBarFill);
            _progressBarContainer.Add(_progressLabel);
            _infoContainer.Add(_progressBarContainer);
        }

        /// <summary>
        /// Sets up the tooltip for the node using BTInfoAttribute if available.
        /// </summary>
        private void SetupTooltip()
        {
            var attribute = _node.GetType().GetCustomAttribute<BTInfoAttribute>();
            if (attribute != null && !string.IsNullOrEmpty(attribute.Description))
            {
                this.tooltip = $"{attribute.Category}\n{attribute.Description}";
            }
            else
            {
                this.tooltip = _node.GetType().Name;
            }
        }

        private void CreateTypeBadge()
        {
            var attribute = _node.GetType().GetCustomAttribute<BTInfoAttribute>();
            if (attribute == null || string.IsNullOrEmpty(attribute.Category) || attribute.Category == "Base") return;
            if (_infoContainer == null) return;

            _badgeLabel = new Label
            {
                text = attribute.Category
            };
            _badgeLabel.AddToClassList("node-type-badge");
            _infoContainer.Insert(0, _badgeLabel);
        }

        /// <summary>
        /// Creates input port for child nodes (circular connector at top).
        /// </summary>
        private void CreateInputPorts()
        {
            if (_node is DecoratorNode or CompositeNode or ActionNode or ConditionNode)
            {
                InputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
            }
            if (InputPort != null)
            {
                InputPort.portName = "";
                //InputPort.style.flexDirection = FlexDirection.Column;
                InputPort.style.alignItems = Align.Center;
                InputPort.style.justifyContent = Justify.Center;
                InputPort.style.marginLeft = StyleKeyword.Auto;
                InputPort.style.marginRight = StyleKeyword.Auto;
                InputPort.style.marginTop = StyleKeyword.Auto;
                InputPort.style.marginBottom = StyleKeyword.Auto;
                InputPort.style.width = 12;
                InputPort.style.height = 12;
                InputPort.style.minWidth = 12;
                InputPort.style.minHeight = 12;
                InputPort.style.maxWidth = 12;
                InputPort.style.maxHeight = 12;
                InputPort.style.flexShrink = 0;

                InputPort.AddToClassList("bt-port");
                InputPort.AddToClassList("bt-input-port");

                //  Note: In uss file, there is a padding-left: 8px for center the Input point
                var portElement = InputPort.Q("connector");
                if (portElement != null)
                {
                    portElement.style.width = 8;
                    portElement.style.height = 8;
                    portElement.style.minWidth = 8;
                    portElement.style.minHeight = 8;
                    portElement.style.maxWidth = 8;
                    portElement.style.maxHeight = 8;
                    // portElement.style.borderTopLeftRadius = 4;
                    // portElement.style.borderTopRightRadius = 4;
                    // portElement.style.borderBottomLeftRadius = 4;
                    // portElement.style.borderBottomRightRadius = 4;
                    portElement.style.alignSelf = Align.Center;
                    portElement.style.marginLeft = StyleKeyword.Auto;
                    portElement.style.marginRight = StyleKeyword.Auto;
                    portElement.style.marginTop = StyleKeyword.Auto;
                    portElement.style.marginBottom = StyleKeyword.Auto;
                }

                inputContainer.Add(InputPort);
                InputPort.RegisterCallback<AttachToPanelEvent>(evt => UpdatePortConnectionState());
            }
        }

        /// <summary>
        /// Creates output port for parent nodes (rounded rectangle connector at bottom).
        /// Composite nodes support multiple connections, others support single connection.
        /// </summary>
        private void CreateOutputPorts()
        {
            if (_node is CompositeNode)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
            }
            else if (_node is DecoratorNode or BTRootNode)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Single, typeof(bool));
            }
            if (OutputPort != null)
            {
                OutputPort.portName = "";
                OutputPort.style.flexDirection = FlexDirection.ColumnReverse;
                OutputPort.style.alignItems = Align.Center;
                OutputPort.style.justifyContent = Justify.Center;
                OutputPort.style.width = 20;
                OutputPort.style.height = 20;
                OutputPort.style.minWidth = 20;
                OutputPort.style.minHeight = 20;
                OutputPort.style.maxWidth = 20;
                OutputPort.style.maxHeight = 20;

                OutputPort.AddToClassList("bt-port");
                OutputPort.AddToClassList("bt-output-port");

                var portElement = OutputPort.Q("connector");
                if (portElement != null)
                {
                    portElement.style.width = 8;
                    portElement.style.height = 8;
                    portElement.style.minWidth = 8;
                    portElement.style.minHeight = 8;
                    portElement.style.maxWidth = 8;
                    portElement.style.maxHeight = 8;
                    // portElement.style.borderTopLeftRadius = 4;
                    // portElement.style.borderTopRightRadius = 4;
                    // portElement.style.borderBottomLeftRadius = 4;
                    // portElement.style.borderBottomRightRadius = 4;
                    portElement.style.alignSelf = Align.Center;
                    portElement.style.marginLeft = StyleKeyword.Auto;
                    portElement.style.marginRight = StyleKeyword.Auto;
                    portElement.style.marginTop = StyleKeyword.Auto;
                    portElement.style.marginBottom = StyleKeyword.Auto;
                }

                if (_node is CompositeNode)
                {
                    outputContainer.Add(OutputPort);
                    if (_infoContainer != null && _infoContainer.parent == null)
                    {
                        outputContainer.Add(_infoContainer);
                    }
                }
                else
                {
                    outputContainer.Add(OutputPort);
                }
                OutputPort.RegisterCallback<AttachToPanelEvent>(evt => UpdatePortConnectionState());
            }
        }

        /// <summary>
        /// Updates the visual state of ports to reflect their connection status.
        /// </summary>
        public void UpdatePortConnectionState()
        {
            if (InputPort != null)
            {
                bool isConnected = HasAnyConnection(InputPort);
                if (isConnected)
                    InputPort.AddToClassList("connected");
                else
                    InputPort.RemoveFromClassList("connected");
            }

            if (OutputPort != null)
            {
                bool isConnected = HasAnyConnection(OutputPort);
                if (isConnected)
                    OutputPort.AddToClassList("connected");
                else
                    OutputPort.RemoveFromClassList("connected");
            }
        }

        /// <summary>0GC check for port connections (avoids LINQ Count() enumerator alloc).</summary>
        private static bool HasAnyConnection(Port port)
        {
            if (port.connections == null) return false;
            foreach (var _ in port.connections) return true;
            return false;
        }

        /// <summary>
        /// Adds CSS classes to the node based on its type for styling.
        /// </summary>
        private void SetUpClasses()
        {
            if (_node is CompositeNode)
            {
                AddToClassList("composite");
                return;
            }
            if (_node is BTRootNode)
            {
                AddToClassList("root");
                return;
            }
            if (_node is ActionNode)
            {
                AddToClassList("action");
                return;
            }
            if (_node is ConditionNode)
            {
                AddToClassList("condition");
                return;
            }
            if (_node is DecoratorNode)
            {
                AddToClassList("decorator");
                return;
            }
        }
    }
}
