using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public class AudioSwitch : ScriptableObject
    {
        [SerializeField]
        private int defaultValue = 0;

        /// <summary>
        /// Named states for this switch. Index in this array corresponds to the integer value.
        /// e.g. stateNames[0]="Wood", stateNames[1]="Concrete" → SetValue("Concrete") sets CurrentValue=1.
        /// </summary>
        [SerializeField]
        private string[] stateNames = new string[] { "State0" };

        public int CurrentValue { get; private set; }

        /// <summary>The serialized default value — stable in both editor and runtime.</summary>
        public int DefaultValue => defaultValue;

        /// <summary>Read-only access to the state name list.</summary>
        public string[] StateNames => stateNames;

        /// <summary>Returns the name of the current state, or the integer as string if no names are defined.</summary>
        public string CurrentStateName =>
            stateNames != null && CurrentValue >= 0 && CurrentValue < stateNames.Length
                ? stateNames[CurrentValue]
                : CurrentValue.ToString();

        public void InitializeSwitch()
        {
            this.CurrentValue = this.defaultValue;
        }

        public void ResetSwitch()
        {
            this.CurrentValue = this.defaultValue;
        }

        /// <summary>Set switch by integer index.</summary>
        public void SetValue(int newValue)
        {
            if (newValue == this.CurrentValue) return;
            this.CurrentValue = newValue;
        }

        /// <summary>
        /// Set switch by state name. Logs a warning if the name is not found.
        /// This is the preferred API — mirrors Wwise's SetSwitch(group, state).
        /// </summary>
        public void SetValue(string stateName)
        {
            if (stateNames == null) return;
            for (int i = 0; i < stateNames.Length; i++)
            {
                if (string.Equals(stateNames[i], stateName, StringComparison.Ordinal))
                {
                    SetValue(i);
                    return;
                }
            }
            Debug.LogWarning($"[AudioSwitch] State '{stateName}' not found in switch '{this.name}'.");
        }

#if UNITY_EDITOR

        public bool DrawSwitchEditor()
        {
            EditorGUI.BeginChangeCheck();

            string newName = EditorGUILayout.TextField("Name", this.name);
            int newDefault = EditorGUILayout.IntField("Default Value", this.defaultValue);

            // States list
            EditorGUILayout.LabelField("States", EditorStyles.boldLabel);
            int stateCount = EditorGUILayout.IntField("Count", stateNames != null ? stateNames.Length : 0);
            stateCount = Mathf.Max(1, stateCount);

            if (stateNames == null || stateNames.Length != stateCount)
            {
                string[] resized = new string[stateCount];
                for (int i = 0; i < stateCount; i++)
                    resized[i] = (stateNames != null && i < stateNames.Length) ? stateNames[i] : $"State{i}";
                stateNames = resized;
            }

            for (int i = 0; i < stateNames.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  [{i}]", GUILayout.Width(32f));
                stateNames[i] = EditorGUILayout.TextField(stateNames[i]);
                EditorGUILayout.EndHorizontal();
            }

            if (EditorGUI.EndChangeCheck())
            {
                this.name = newName;
                this.defaultValue = Mathf.Clamp(newDefault, 0, stateNames.Length - 1);
                return true;
            }
            return false;
        }

#endif
    }
}
