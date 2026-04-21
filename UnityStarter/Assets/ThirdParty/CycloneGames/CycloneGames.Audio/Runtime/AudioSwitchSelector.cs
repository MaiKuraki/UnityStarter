// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Routes playback to the connected branch whose explicit state assignment matches
    /// the current AudioSwitch state. Routing is name-based, not position-based —
    /// connection order and visual layout do not affect which branch plays.
    /// Each branch must be assigned a state via the editor mapping table.
    /// </summary>
    public class AudioSwitchSelector : AudioNode
    {
        [SerializeField]
        private AudioSwitch switchObject;

        /// <summary>
        /// Per-branch state assignment. branchStateAssignments[i] is the state name
        /// that ConnectedNodes[i] responds to. Parallel array to ConnectedNodes.
        /// </summary>
        [SerializeField]
        private string[] branchStateAssignments = new string[0];

        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            if (switchObject == null)
            {
                Debug.LogWarningFormat("No AudioSwitch assigned for {0}", this.name);
                return;
            }

            string currentState = switchObject.CurrentStateName;
            int branchCount = this.input.ConnectedNodes.Length;

            for (int i = 0; i < branchCount; i++)
            {
                if (i < branchStateAssignments.Length &&
                    branchStateAssignments[i] == currentState)
                {
                    ProcessConnectedNode(i, activeEvent);
                    return;
                }
            }

            Debug.LogWarningFormat(
                "[SwitchSelector] '{0}': no branch assigned to state '{1}'.",
                this.name, currentState);
        }

#if UNITY_EDITOR

        private const float NodeWidth = 260f;

        [SerializeField]
        private bool autoSortByNodeY = true;

        private bool NeedsSortByNodeY()
        {
            if (this.input == null || this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length < 2)
                return false;

            float prevY = float.NegativeInfinity;
            for (int i = 0; i < this.input.ConnectedNodes.Length; i++)
            {
                AudioNodeOutput output = this.input.ConnectedNodes[i];
                float y = output != null && output.ParentNode != null
                    ? output.ParentNode.NodeRect.y
                    : prevY;
                if (y < prevY)
                    return true;
                prevY = y;
            }
            return false;
        }

        private void AutoSortConnectionsIfNeeded()
        {
            if (!autoSortByNodeY || this.input == null || !NeedsSortByNodeY()) return;

            // Preserve branch-state mapping across the connection reorder.
            Dictionary<AudioNodeOutput, string> assignmentByOutput = new Dictionary<AudioNodeOutput, string>();
            AudioNodeOutput[] before = this.input.ConnectedNodes;
            for (int i = 0; i < before.Length; i++)
            {
                string assigned = i < branchStateAssignments.Length ? branchStateAssignments[i] : "";
                assignmentByOutput[before[i]] = assigned;
            }

            this.input.SortConnections();

            AudioNodeOutput[] after = this.input.ConnectedNodes;
            string[] reordered = new string[after.Length];
            for (int i = 0; i < after.Length; i++)
            {
                if (after[i] != null && assignmentByOutput.TryGetValue(after[i], out string assignedState))
                    reordered[i] = assignedState;
                else
                    reordered[i] = "";
            }

            branchStateAssignments = reordered;
            EditorUtility.SetDirty(this.input);
            EditorUtility.SetDirty(this);
        }

        // Ensure branchStateAssignments stays in sync with current branch count.
        private void SyncAssignmentsArray(int branchCount)
        {
            if (branchStateAssignments == null)
                branchStateAssignments = new string[0];

            if (branchStateAssignments.Length == branchCount) return;

            string[] resized = new string[branchCount];
            for (int i = 0; i < branchCount; i++)
                resized[i] = i < branchStateAssignments.Length ? branchStateAssignments[i] : "";
            branchStateAssignments = resized;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private float CalcHeight()
        {
            const float TitleBarH = 18f;
            const float RowH = 19f;
            const float RowGap = 2f;
            const float BottomPad = 8f;

            // title bar + auto sort row + switch object field
            float h = TitleBarH + RowH + RowGap + RowH + RowGap;

            if (switchObject == null)
            {
                h += EditorStyles.wordWrappedMiniLabel.CalcHeight(
                    new GUIContent("Assign an AudioSwitch to configure branch assignments."), NodeWidth - 16f);
                h += BottomPad;
                return h;
            }

            int branchCount = this.input?.ConnectedNodes != null ? this.input.ConnectedNodes.Length : 0;

            // Section header
            h += RowH;

            if (branchCount == 0)
            {
                h += RowH; // "No branches" label
            }
            else
            {
                // Row per branch: label line + popup line
                h += branchCount * (RowH + RowGap + RowH + RowGap);

                // Warning row (only one is shown in DrawProperties)
                bool hasDuplicate = false;
                bool hasUnassigned = false;
                HashSet<string> seen = new HashSet<string>();

                for (int i = 0; i < branchCount; i++)
                {
                    string assigned = (branchStateAssignments != null && i < branchStateAssignments.Length)
                        ? branchStateAssignments[i]
                        : "";

                    if (string.IsNullOrEmpty(assigned))
                    {
                        hasUnassigned = true;
                        continue;
                    }

                    if (!seen.Add(assigned))
                        hasDuplicate = true;
                }

                if (hasDuplicate || hasUnassigned)
                    h += RowH + RowGap;
            }

            h += BottomPad;
            return h;
        }

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Switch Selector";
            this.nodeRect.height = 80;
            this.nodeRect.width = NodeWidth;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        public override void DrawNode(int id)
        {
            AutoSortConnectionsIfNeeded();
            this.nodeRect.height = CalcHeight();
            base.DrawNode(id);
        }

        protected override void DrawProperties()
        {
            EditorGUI.BeginChangeCheck();
            autoSortByNodeY = EditorGUILayout.ToggleLeft("Auto Sort by Node Y", autoSortByNodeY);
            if (EditorGUI.EndChangeCheck())
                UnityEditor.EditorUtility.SetDirty(this);

            EditorGUI.BeginChangeCheck();
            var newSwitch = EditorGUILayout.ObjectField(
                this.switchObject, typeof(AudioSwitch), false) as AudioSwitch;
            if (EditorGUI.EndChangeCheck())
            {
                this.switchObject = newSwitch;
                // Clear assignments when switch changes — old names are stale
                this.branchStateAssignments = new string[0];
                UnityEditor.EditorUtility.SetDirty(this);
            }

            if (switchObject == null)
            {
                EditorGUILayout.LabelField(
                    "Assign an AudioSwitch to configure branch assignments.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            string[] states = switchObject.StateNames;   // e.g. ["Wood","Concrete","Metal"]
            int branchCount = this.input?.ConnectedNodes != null ? this.input.ConnectedNodes.Length : 0;

            SyncAssignmentsArray(branchCount);

            // Build popup options: "(unassigned)" + all state names
            string[] popupOptions = new string[states.Length + 1];
            popupOptions[0] = "(unassigned)";
            for (int i = 0; i < states.Length; i++)
                popupOptions[i + 1] = states[i];

            // ── Per-branch assignment ───────────────────────────────────
            EditorGUILayout.LabelField("Branch Assignments", EditorStyles.boldLabel);

            if (branchCount == 0)
            {
                EditorGUILayout.LabelField("No branches connected.", EditorStyles.miniLabel);
                return;
            }

            string currentState = Application.isPlaying
                ? switchObject.CurrentStateName
                : (states != null && switchObject.DefaultValue < states.Length
                    ? states[switchObject.DefaultValue] : "");

            Color prevColor = GUI.color;
            bool hasDuplicate = false;
            bool hasUnassigned = false;

            for (int i = 0; i < branchCount; i++)
            {
                string assignment = branchStateAssignments[i];
                bool isActive = assignment == currentState && !string.IsNullOrEmpty(assignment);

                // Show the connected node's name so the user knows which audio node this branch is
                string nodeName = (this.input.ConnectedNodes[i]?.ParentNode != null)
                    ? this.input.ConnectedNodes[i].ParentNode.name
                    : $"Branch {i}";

                GUI.color = isActive ? new Color(0.45f, 0.80f, 0.45f) : Color.white;
                EditorGUILayout.LabelField(
                    $"{(isActive ? "▶ " : "   ")}{nodeName}",
                    EditorStyles.miniLabel);
                GUI.color = Color.white;

                // Find current selection index in popup
                int selected = 0;
                for (int s = 0; s < states.Length; s++)
                {
                    if (states[s] == assignment) { selected = s + 1; break; }
                }

                EditorGUI.BeginChangeCheck();
                int newSelected = EditorGUILayout.Popup(selected, popupOptions);
                if (EditorGUI.EndChangeCheck())
                {
                    branchStateAssignments[i] = newSelected == 0 ? "" : states[newSelected - 1];
                    UnityEditor.EditorUtility.SetDirty(this);
                }

                if (string.IsNullOrEmpty(branchStateAssignments[i])) hasUnassigned = true;
            }

            GUI.color = prevColor;

            // Check for duplicates
            for (int i = 0; i < branchCount && !hasDuplicate; i++)
                for (int j = i + 1; j < branchCount && !hasDuplicate; j++)
                    if (!string.IsNullOrEmpty(branchStateAssignments[i]) &&
                        branchStateAssignments[i] == branchStateAssignments[j])
                        hasDuplicate = true;

            // ── Warnings ───────────────────────────────────────────────
            if (hasDuplicate)
                EditorGUILayout.LabelField("⚠ Duplicate state assignments", EditorStyles.miniLabel);
            else if (hasUnassigned)
                EditorGUILayout.LabelField("⚠ Some branches are unassigned", EditorStyles.miniLabel);
        }

#endif
    }
}