// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    internal readonly struct AudioParameterScopeKey : System.IEquatable<AudioParameterScopeKey>
    {
        public readonly int ParameterId;
        public readonly int ScopeId;

        public AudioParameterScopeKey(int parameterId, int scopeId)
        {
            ParameterId = parameterId;
            ScopeId = scopeId;
        }

        public bool Equals(AudioParameterScopeKey other)
        {
            return ParameterId == other.ParameterId && ScopeId == other.ScopeId;
        }

        public override bool Equals(object obj)
        {
            return obj is AudioParameterScopeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ParameterId * 397) ^ ScopeId;
            }
        }
    }

    internal sealed class AudioScopedParameterValue
    {
        private AudioParameter parameter;
        private float currentValue;
        private float targetValue;

        public float CurrentValue => currentValue;

        public void Initialize(AudioParameter newParameter, float value)
        {
            parameter = newParameter;
            if (!IsFinite(value)) value = 0f;
            float clamped = Clamp(value);
            currentValue = clamped;
            targetValue = clamped;
        }

        public void SetTarget(float value)
        {
            if (parameter == null || !IsFinite(value)) return;

            targetValue = Clamp(value);
            if (parameter.InterpolationSpeed <= 0f)
            {
                currentValue = targetValue;
            }
        }

        public void Update(float deltaTime)
        {
            if (parameter == null || !IsFinite(deltaTime) || deltaTime <= 0f ||
                parameter.InterpolationSpeed <= 0f || currentValue == targetValue) return;

            currentValue = Mathf.MoveTowards(currentValue, targetValue, parameter.InterpolationSpeed * deltaTime);
        }

        private float Clamp(float value)
        {
            return parameter != null ? Mathf.Clamp(value, parameter.MinValue, parameter.MaxValue) : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

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
        public float CurrentValue
        {
            get
            {
                EnsureRuntimeInitialized();
                return runtimeCurrentValue;
            }
            private set => runtimeCurrentValue = value;
        }

        /// <summary>
        /// The target value that CurrentValue is interpolating toward.
        /// When interpolationSpeed is 0, CurrentValue == TargetValue always.
        /// </summary>
        public float TargetValue
        {
            get
            {
                EnsureRuntimeInitialized();
                return runtimeTargetValue;
            }
            private set => runtimeTargetValue = value;
        }

        private float runtimeCurrentValue;
        private float runtimeTargetValue;
        private bool runtimeInitialized;

        public void InitializeParameter()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioParameter) + ".InitializeParameter");
            float safeMin = IsFinite(this.minValue) ? this.minValue : 0f;
            float safeMax = IsFinite(this.maxValue) ? this.maxValue : safeMin;
            if (safeMax < safeMin)
            {
                float swap = safeMin;
                safeMin = safeMax;
                safeMax = swap;
            }
            float safeDefault = IsFinite(this.defaultValue) ? this.defaultValue : safeMin;
            runtimeCurrentValue = Mathf.Clamp(safeDefault, safeMin, safeMax);
            runtimeTargetValue = runtimeCurrentValue;
            runtimeInitialized = true;
        }

        public void ResetParameter()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioParameter) + ".ResetParameter");
            InitializeParameter();
        }

        /// <summary>
        /// Set a new target value. Clamped to [minValue, maxValue].
        /// If interpolationSpeed > 0, CurrentValue will smoothly approach the target.
        /// </summary>
        public void SetValue(float newValue)
        {
            EnsureRuntimeInitialized();
            if (this.useGaze || !IsFinite(newValue)) return;

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
            EnsureRuntimeInitialized();
            if (!IsFinite(deltaTime) || deltaTime <= 0f ||
                this.interpolationSpeed <= 0f || this.CurrentValue == this.TargetValue) return;

            this.CurrentValue = Mathf.MoveTowards(this.CurrentValue, this.TargetValue, this.interpolationSpeed * deltaTime);
        }

        public float EvaluateCurrentValue()
        {
            EnsureRuntimeInitialized();
            return runtimeCurrentValue;
        }

        private void OnEnable()
        {
            if (!runtimeInitialized)
            {
                InitializeParameter();
            }
        }

        private void EnsureRuntimeInitialized()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioParameter));
            if (!runtimeInitialized)
            {
                InitializeParameter();
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
