// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// The runtime instance of an AudioParameter on an ActiveEvent
    /// </summary>
    public class ActiveParameter
    {
        private float localCurrentValue;
        private float localTargetValue;
        private float localCurrentResult;
        private bool useLocalOverride;
        private float interpolationSpeed;

        /// <summary>
        /// Constructor: Create a new ActiveParameter (used for initial pool allocation)
        /// </summary>
        public ActiveParameter() { }

        /// <summary>
        /// Re-initialize this instance with a new EventParameter, avoiding heap allocation.
        /// </summary>
        public void ReInitialize(AudioEventParameter root)
        {
            this.rootParameter = root;
            this.interpolationSpeed = root?.parameter != null ? root.parameter.InterpolationSpeed : 0f;
            Reset();
        }

        /// <summary>
        /// The EventParameter being used
        /// </summary>
        public AudioEventParameter rootParameter { get; private set; }
        /// <summary>
        /// The value of the root parameter, unless the ActiveParameter has been independently set
        /// </summary>
        public float CurrentValue
        {
            get
            {
                return useLocalOverride ? localCurrentValue : GetGlobalValue();
            }
            set
            {
                float clamped = ClampToParameter(value);
                useLocalOverride = true;
                localTargetValue = clamped;

                if (interpolationSpeed <= 0f)
                {
                    localCurrentValue = clamped;
                    localCurrentResult = Evaluate(localCurrentValue);
                }
            }
        }

        /// <summary>
        /// The result of the current value applied to the response curve
        /// </summary>
        public float CurrentResult
        {
            get => useLocalOverride ? localCurrentResult : Evaluate(GetGlobalValue());
        }

        public void Update(float deltaTime)
        {
            if (!useLocalOverride) return;

            if (interpolationSpeed > 0f && !UnityEngine.Mathf.Approximately(localCurrentValue, localTargetValue))
            {
                localCurrentValue = UnityEngine.Mathf.MoveTowards(localCurrentValue, localTargetValue, interpolationSpeed * deltaTime);
            }
            else
            {
                localCurrentValue = localTargetValue;
            }

            localCurrentResult = Evaluate(localCurrentValue);
        }

        public void Reset()
        {
            float globalValue = GetGlobalValue();
            localCurrentValue = globalValue;
            localTargetValue = globalValue;
            localCurrentResult = Evaluate(globalValue);
            useLocalOverride = false;
        }

        private float GetGlobalValue()
        {
            if (rootParameter?.parameter == null) return 0f;
            return rootParameter.parameter.EvaluateCurrentValue();
        }

        private float ClampToParameter(float value)
        {
            if (rootParameter?.parameter == null) return value;
            return UnityEngine.Mathf.Clamp(value, rootParameter.parameter.MinValue, rootParameter.parameter.MaxValue);
        }

        private float Evaluate(float value)
        {
            return rootParameter != null ? rootParameter.Evaluate(value) : value;
        }
    }
}
