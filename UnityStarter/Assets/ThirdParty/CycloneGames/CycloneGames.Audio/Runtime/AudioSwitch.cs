using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public enum AudioStateMixTargetType
    {
        Parameter = 0,
        MixerParameter = 1,
        Snapshot = 2
    }

    [Serializable]
    public sealed class AudioStateMixRule
    {
        [SerializeField]
        private AudioStateGroup stateGroup;
        [SerializeField]
        private string stateName = "Default";
        [SerializeField]
        private AudioStateMixTargetType targetType;
        [SerializeField]
        private AudioParameter parameter;
        [SerializeField]
        private float parameterValue;
        [SerializeField]
        private string mixerParameterName = string.Empty;
        [SerializeField]
        private float mixerParameterValue;
        [SerializeField]
        private AudioMixerSnapshot snapshot;
        [SerializeField]
        private float snapshotTransitionTime = 0.1f;

        public AudioStateGroup StateGroup => stateGroup;
        public string StateName => stateName;
        public AudioStateMixTargetType TargetType => targetType;
        public AudioParameter Parameter => parameter;
        public string MixerParameterName => mixerParameterName;
        public AudioMixerSnapshot Snapshot => snapshot;
        public float SnapshotTransitionTime => snapshotTransitionTime;

        public bool Matches(AudioStateGroup changedGroup)
        {
            if (stateGroup == null) return false;
            if (changedGroup != null && stateGroup != changedGroup) return false;
            return string.Equals(stateGroup.CurrentStateName, stateName, StringComparison.Ordinal);
        }

        public void Apply()
        {
            switch (targetType)
            {
                case AudioStateMixTargetType.Parameter:
                    if (parameter != null)
                    {
                        AudioManager.SetParameterValue(parameter, parameterValue);
                    }
                    break;
                case AudioStateMixTargetType.MixerParameter:
                    AudioManager.SetMixerParameter(mixerParameterName, mixerParameterValue);
                    break;
                case AudioStateMixTargetType.Snapshot:
                    if (snapshot != null)
                    {
                        snapshot.TransitionTo(Mathf.Max(0f, snapshotTransitionTime));
                    }
                    break;
            }
        }

#if UNITY_EDITOR

        public bool DrawRuleEditor(int index)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Rule " + index, EditorStyles.boldLabel);
            stateGroup = EditorGUILayout.ObjectField("State Group", stateGroup, typeof(AudioStateGroup), false) as AudioStateGroup;
            string[] stateNames = stateGroup != null ? stateGroup.StateNames : null;
            if (stateNames != null && stateNames.Length > 0)
            {
                int selectedIndex = stateGroup.GetStateIndex(stateName);
                selectedIndex = Mathf.Clamp(selectedIndex, 0, stateNames.Length - 1);
                int newIndex = EditorGUILayout.Popup("State", selectedIndex, stateNames);
                stateName = stateNames[newIndex];
            }
            else
            {
                stateName = EditorGUILayout.TextField("State", stateName);
            }
            targetType = (AudioStateMixTargetType)EditorGUILayout.EnumPopup("Target", targetType);

            switch (targetType)
            {
                case AudioStateMixTargetType.Parameter:
                    parameter = EditorGUILayout.ObjectField("Parameter", parameter, typeof(AudioParameter), false) as AudioParameter;
                    parameterValue = EditorGUILayout.FloatField("Value", parameterValue);
                    break;
                case AudioStateMixTargetType.MixerParameter:
                    mixerParameterName = EditorGUILayout.TextField("Mixer Parameter", mixerParameterName);
                    mixerParameterValue = EditorGUILayout.FloatField("Value", mixerParameterValue);
                    break;
                case AudioStateMixTargetType.Snapshot:
                    snapshot = EditorGUILayout.ObjectField("Snapshot", snapshot, typeof(AudioMixerSnapshot), false) as AudioMixerSnapshot;
                    snapshotTransitionTime = Mathf.Max(0f, EditorGUILayout.FloatField("Transition Time", snapshotTransitionTime));
                    break;
            }

            return EditorGUI.EndChangeCheck();
        }

#endif
    }

    public sealed class AudioStateMixProfile : ScriptableObject
    {
        [SerializeField]
        private List<AudioStateMixRule> rules = new List<AudioStateMixRule>(4);

        public IReadOnlyList<AudioStateMixRule> Rules => rules;

        public void Apply(AudioStateGroup changedGroup)
        {
            if (rules == null) return;

            for (int i = 0; i < rules.Count; i++)
            {
                AudioStateMixRule rule = rules[i];
                if (rule != null && rule.Matches(changedGroup))
                {
                    rule.Apply();
                }
            }
        }

#if UNITY_EDITOR

        public bool DrawStateMixProfileEditor()
        {
            EditorGUI.BeginChangeCheck();

            string newName = EditorGUILayout.TextField("Name", name);
            if (rules == null)
            {
                rules = new List<AudioStateMixRule>(4);
            }

            int removeIndex = -1;
            bool structureChanged = false;
            for (int i = 0; i < rules.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (rules[i] == null)
                {
                    rules[i] = new AudioStateMixRule();
                    structureChanged = true;
                }

                rules[i].DrawRuleEditor(i);
                if (GUILayout.Button("Delete Rule"))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
            {
                rules.RemoveAt(removeIndex);
                structureChanged = true;
            }

            if (GUILayout.Button("Add Rule"))
            {
                rules.Add(new AudioStateMixRule());
                structureChanged = true;
            }

            if (EditorGUI.EndChangeCheck() || structureChanged)
            {
                name = newName;
                return true;
            }

            return false;
        }

#endif
    }

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

    public sealed class AudioStateGroup : ScriptableObject
    {
        [SerializeField]
        private int defaultValue = 0;
        [SerializeField]
        private string[] stateNames = new string[] { "Default" };

        public int CurrentValue { get; private set; }
        public int DefaultValue => defaultValue;
        public string[] StateNames => stateNames;
        public string CurrentStateName =>
            stateNames != null && CurrentValue >= 0 && CurrentValue < stateNames.Length
                ? stateNames[CurrentValue]
                : CurrentValue.ToString();

        public void InitializeStateGroup()
        {
            CurrentValue = ClampStateIndex(defaultValue);
        }

        public void ResetStateGroup()
        {
            InitializeStateGroup();
        }

        public void SetValue(int newValue)
        {
            int clamped = ClampStateIndex(newValue);
            if (clamped == CurrentValue) return;

            CurrentValue = clamped;
        }

        public bool SetValue(string stateName)
        {
            int index = GetStateIndex(stateName);
            if (index < 0) return false;

            SetValue(index);
            return true;
        }

        public int GetStateIndex(string stateName)
        {
            if (stateNames == null || string.IsNullOrEmpty(stateName)) return -1;

            for (int i = 0; i < stateNames.Length; i++)
            {
                if (string.Equals(stateNames[i], stateName, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private int ClampStateIndex(int value)
        {
            int count = stateNames != null ? stateNames.Length : 0;
            return count > 0 ? Mathf.Clamp(value, 0, count - 1) : 0;
        }

#if UNITY_EDITOR

        public bool DrawStateGroupEditor()
        {
            EditorGUI.BeginChangeCheck();

            string newName = EditorGUILayout.TextField("Name", name);
            int newDefault = EditorGUILayout.IntField("Default State", defaultValue);

            EditorGUILayout.LabelField("States", EditorStyles.boldLabel);
            int stateCount = EditorGUILayout.IntField("Count", stateNames != null ? stateNames.Length : 0);
            stateCount = Mathf.Max(1, stateCount);

            if (stateNames == null || stateNames.Length != stateCount)
            {
                string[] resized = new string[stateCount];
                for (int i = 0; i < stateCount; i++)
                {
                    resized[i] = stateNames != null && i < stateNames.Length ? stateNames[i] : "State" + i;
                }

                stateNames = resized;
            }

            for (int i = 0; i < stateNames.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("[" + i + "]", GUILayout.Width(32f));
                stateNames[i] = EditorGUILayout.TextField(stateNames[i]);
                EditorGUILayout.EndHorizontal();
            }

            if (EditorGUI.EndChangeCheck())
            {
                name = newName;
                defaultValue = Mathf.Clamp(newDefault, 0, stateNames.Length - 1);
                CurrentValue = ClampStateIndex(CurrentValue);
                return true;
            }

            return false;
        }

#endif
    }
}
