using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample.Editor
{
    /// <summary>
    /// Provides non-persistent Play Mode diagnostics controls for sample ASC hosts.
    /// </summary>
    [CustomEditor(typeof(AbilitySystemComponentHolder), true)]
    [CanEditMultipleObjects]
    internal sealed class AbilitySystemComponentHolderEditor : UnityEditor.Editor
    {
        private readonly List<AbilitySystemComponent> liveTargets = new List<AbilitySystemComponent>(8);
        private readonly List<GameObject> liveOwners = new List<GameObject>(8);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawRemainingSerializedProperties();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("GAS Runtime Overlay", EditorStyles.boldLabel);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Overlay controls are available in Play Mode and do not modify serialized authoring data.",
                    MessageType.Info);
                return;
            }

            CollectLiveTargets();
            if (liveTargets.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "The selected holder does not expose a live AbilitySystemComponent. Ensure its composition owner has initialized it.",
                    MessageType.Warning);
                return;
            }

            int registeredSelectedCount = CountRegisteredSelectedTargets();
            int boundTargetCount = GASDebugOverlay.BoundTargetCount;
            int targetCapacity = GASDebugOverlay.TargetCapacity;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Selected Live ASCs", liveTargets.Count);
                EditorGUILayout.IntField("Selected Registered", registeredSelectedCount);
                EditorGUILayout.IntField("Overlay Targets", boundTargetCount);
                EditorGUILayout.IntField("Overlay Capacity", targetCapacity);
                EditorGUILayout.Toggle("Overlay Visible", GASDebugOverlay.IsActive);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add / Update Selected & Show"))
            {
                AddSelectedTargetsAndShow();
            }

            using (new EditorGUI.DisabledScope(registeredSelectedCount == 0))
            {
                if (GUILayout.Button("Remove Selected"))
                {
                    RemoveSelectedTargets();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GASDebugOverlay.IsActive)
            {
                if (GUILayout.Button("Hide Overlay"))
                {
                    GASDebugOverlay.SetEnabled(false);
                    Repaint();
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(boundTargetCount == 0))
                {
                    if (GUILayout.Button("Show Overlay"))
                    {
                        GASDebugOverlay.SetEnabled(true);
                        Repaint();
                    }
                }
            }
        }

        private void DrawRemainingSerializedProperties()
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        private void CollectLiveTargets()
        {
            liveTargets.Clear();
            liveOwners.Clear();

            for (int i = 0; i < targets.Length; i++)
            {
                var holder = targets[i] as AbilitySystemComponentHolder;
                if (holder == null)
                {
                    continue;
                }

                AbilitySystemComponent asc = holder.AbilitySystemComponent;
                if (asc == null || asc.IsDisposed || ContainsByReference(asc))
                {
                    continue;
                }

                liveTargets.Add(asc);
                liveOwners.Add(holder.gameObject);
            }
        }

        private bool ContainsByReference(AbilitySystemComponent targetASC)
        {
            for (int i = 0; i < liveTargets.Count; i++)
            {
                if (ReferenceEquals(liveTargets[i], targetASC))
                {
                    return true;
                }
            }

            return false;
        }

        private int CountRegisteredSelectedTargets()
        {
            int count = 0;
            for (int i = 0; i < liveTargets.Count; i++)
            {
                if (GASDebugOverlay.IsTargetRegistered(liveTargets[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private void AddSelectedTargetsAndShow()
        {
            int acceptedCount = 0;
            int rejectedCount = 0;
            for (int i = 0; i < liveTargets.Count; i++)
            {
                GameObject owner = liveOwners[i];
                if (GASDebugOverlay.TryAddTarget(
                        liveTargets[i],
                        owner,
                        owner != null ? owner.transform : null))
                {
                    acceptedCount++;
                }
                else
                {
                    rejectedCount++;
                }
            }

            if (acceptedCount > 0)
            {
                GASDebugOverlay.SetEnabled(true);
            }

            if (rejectedCount > 0)
            {
                Debug.LogWarningFormat(
                    "[GAS Overlay] {0} selected ASC target(s) were not registered. The current capacity is {1}.",
                    rejectedCount,
                    GASDebugOverlay.TargetCapacity);
            }

            Repaint();
        }

        private void RemoveSelectedTargets()
        {
            for (int i = 0; i < liveTargets.Count; i++)
            {
                GASDebugOverlay.RemoveTarget(liveTargets[i]);
            }

            Repaint();
        }
    }
}
