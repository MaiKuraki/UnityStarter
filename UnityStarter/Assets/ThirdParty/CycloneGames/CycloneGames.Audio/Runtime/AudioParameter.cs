// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// A runtime value that affects audio properties on an AudioEvent.
    /// Supports min/max range enforcement and optional value interpolation (smoothing).
    /// </summary>
    public class AudioParameter : ScriptableObject
    {
        [SerializeField]
        private float defaultValue = 0;
        [SerializeField]
        private float minValue = 0f;
        [SerializeField]
        private float maxValue = 1f;
        /// <summary>
        /// Speed of interpolation toward the target value (units/sec). 0 = instant.
        /// </summary>
        [SerializeField]
        private float interpolationSpeed = 0f;
        [SerializeField]
        private bool useGaze = false;

        public bool UseGaze => this.useGaze;
        public float MinValue => this.minValue;
        public float MaxValue => this.maxValue;
        public float InterpolationSpeed => this.interpolationSpeed;

        /// <summary>
        /// The current (possibly interpolated) value of the parameter.
        /// </summary>
        public float CurrentValue { get; private set; }

        /// <summary>
        /// The target value that CurrentValue is interpolating toward.
        /// When interpolationSpeed is 0, CurrentValue == TargetValue always.
        /// </summary>
        public float TargetValue { get; private set; }

        public void InitializeParameter()
        {
            this.CurrentValue = Mathf.Clamp(this.defaultValue, this.minValue, this.maxValue);
            this.TargetValue = this.CurrentValue;
        }

        public void ResetParameter()
        {
            this.CurrentValue = Mathf.Clamp(this.defaultValue, this.minValue, this.maxValue);
            this.TargetValue = this.CurrentValue;
        }

        /// <summary>
        /// Set a new target value. Clamped to [minValue, maxValue].
        /// If interpolationSpeed > 0, CurrentValue will smoothly approach the target.
        /// </summary>
        public void SetValue(float newValue)
        {
            if (this.useGaze) return;

            newValue = Mathf.Clamp(newValue, this.minValue, this.maxValue);
            if (newValue == this.TargetValue) return;

            this.TargetValue = newValue;

            if (this.interpolationSpeed <= 0f)
            {
                this.CurrentValue = newValue;
            }
        }

        /// <summary>
        /// Advance interpolation. Called once per frame by AudioManager.
        /// </summary>
        public void UpdateInterpolation(float deltaTime)
        {
            if (this.interpolationSpeed <= 0f || this.CurrentValue == this.TargetValue) return;

            this.CurrentValue = Mathf.MoveTowards(this.CurrentValue, this.TargetValue, this.interpolationSpeed * deltaTime);
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Draw the properties for the parameter in the graph
        /// </summary>
        public bool DrawParameterEditor()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string newName = EditorGUILayout.TextField("Name", this.name);
            float newMinValue = EditorGUILayout.FloatField("Min Value", this.minValue);
            float newMaxValue = EditorGUILayout.FloatField("Max Value", this.maxValue);
            float newDefaultValue = EditorGUILayout.Slider("Default Value", this.defaultValue, newMinValue, newMaxValue);
            float newInterpSpeed = EditorGUILayout.FloatField("Interpolation Speed", this.interpolationSpeed);
            if (newInterpSpeed < 0) newInterpSpeed = 0;
            bool newUseGaze = EditorGUILayout.Toggle("Use Gaze", this.useGaze);
            EditorGUILayout.EndVertical();
            if (EditorGUI.EndChangeCheck())
            {
                this.name = newName;
                this.minValue = newMinValue;
                this.maxValue = Mathf.Max(newMaxValue, newMinValue);
                this.defaultValue = Mathf.Clamp(newDefaultValue, this.minValue, this.maxValue);
                this.interpolationSpeed = newInterpSpeed;
                this.useGaze = newUseGaze;
                return true;
            }
            return false;
        }

#endif
    }
}
