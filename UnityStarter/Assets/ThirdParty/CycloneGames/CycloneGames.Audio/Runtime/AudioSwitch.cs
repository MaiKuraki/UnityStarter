using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public class AudioSwitch : ScriptableObject
    {
        private int defaultValue = 0;
        public int CurrentValue { get; private set; }

        public void InitializeSwitch()
        {
            this.CurrentValue = this.defaultValue;
        }

        public void ResetSwitch()
        {
            this.CurrentValue = this.defaultValue;
        }

        public void SetValue(int newValue)
        {
            if (newValue == this.CurrentValue)
            {
                return;
            }

            this.CurrentValue = newValue;
        }

#if UNITY_EDITOR

        public bool DrawSwitchEditor()
        {
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField("Name", this.name);
            int newDefaultValue = EditorGUILayout.IntField("Default Value", this.defaultValue);
            if (EditorGUI.EndChangeCheck())
            {
                this.name = newName;
                this.defaultValue = newDefaultValue;
                return true;
            }
            return false;
        }

#endif
    }
}
