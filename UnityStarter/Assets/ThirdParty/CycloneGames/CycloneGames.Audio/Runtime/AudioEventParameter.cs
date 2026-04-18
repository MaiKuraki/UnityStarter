// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// An AudioParameter with a response curve and audio property to apply changes to
    /// </summary>
    [System.Serializable]
    public class AudioEventParameter
    {
        /// <summary>
        /// The root parameter that the event is using
        /// </summary>
        public AudioParameter parameter = null;
        /// <summary>
        /// The curve to evaluate the parameter's value on
        /// </summary>
        public AnimationCurve responseCurve = new AnimationCurve();
        /// <summary>
        /// Which audio property the parameter affects
        /// </summary>
        public ParameterType paramType;

        public float Evaluate(float value)
        {
            return this.responseCurve.Evaluate(value);
        }

        /// <summary>
        /// Evaluate a custom value on the parameter's response curve
        /// </summary>
        /// <param name="newValue">The custom value to evaluate</param>
        /// <returns>The result of the newValue on the parameter's response curve</returns>
        public float ProcessParameter(float newValue)
        {
            return Evaluate(newValue);
        }
    }

    /// <summary>
    /// The audio properties that a parameter can affect
    /// </summary>
    public enum ParameterType
    {
        Volume,
        Pitch,
        SpatialBlend,
        PanStereo,
        ReverbZoneMix,
        DopplerLevel
    }
}
