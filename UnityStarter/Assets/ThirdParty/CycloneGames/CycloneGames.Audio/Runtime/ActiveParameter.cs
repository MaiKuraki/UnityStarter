// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// The runtime instance of an AudioParameter on an ActiveEvent
    /// </summary>
    public class ActiveParameter
    {
        /// <summary>
        /// The value of the parameter
        /// </summary>
        private float currentValue = 0;
        /// <summary>
        /// The result of the parameter's curve on the currentValue
        /// </summary>
        private float currentResult = 0;
        /// <summary>
        /// Has the ActiveParameter been set to a value independent from the main AudioParameter
        /// </summary>
        private bool isDirty = false;

        /// <summary>
        /// Constructor: Create a new ActiveParameter from the EventParameter
        /// </summary>
        /// <param name="root">The EventParameter to apply to this event</param>
        public ActiveParameter(AudioEventParameter root)
        {
            this.rootParameter = root;
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
                if (this.isDirty)
                {
                    return this.currentValue;
                }
                else
                {
                    return this.rootParameter.parameter.CurrentValue;
                }
            }
            set
            {
                this.currentValue = value;
                this.currentResult = this.rootParameter.ProcessParameter(this.currentValue);
                this.isDirty = true;
            }
        }

        /// <summary>
        /// The result of the current value applied to the response curve
        /// </summary>
        public float CurrentResult
        {
            get {
                if (this.isDirty)
                {
                    return this.currentResult;
                }
                else
                {
                    return this.rootParameter.CurrentResult;
                }
            }
        }

        /// <summary>
        /// Clear the modified value and use the global parameter's value
        /// </summary>
        public void Reset()
        {
            this.currentValue = this.rootParameter.parameter.CurrentValue;
            this.currentResult = this.rootParameter.CurrentResult;
            this.isDirty = false;
        }
    }
}