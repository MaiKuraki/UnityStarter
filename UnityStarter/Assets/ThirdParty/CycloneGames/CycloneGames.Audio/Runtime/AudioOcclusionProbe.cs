// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Samples how much of the emitter-to-listener path is blocked by geometry.
    /// </summary>
    /// <remarks>
    /// A single centre ray yields a binary result, which makes an emitter pop between muffled and
    /// clear when it crosses a thin edge. Sampling several parallel rays returns a fraction instead,
    /// so partial cover (pillars, door frames, half walls) attenuates proportionally. Offsets are
    /// perpendicular to the path and horizontal, matching how level geometry usually occludes.
    /// </remarks>
    internal static class AudioOcclusionProbe
    {
        internal const int MaximumRayCount = 5;
        private const float RayOffsetMeters = 0.35f;

        /// <summary>
        /// Returns the blocked fraction in [0, 1]. A ray count of one produces the legacy binary result.
        /// </summary>
        internal static float Sample(Vector3 emitterPosition, Vector3 listenerPosition, LayerMask occlusionLayers, int rayCount)
        {
            int count = Mathf.Clamp(rayCount, 1, MaximumRayCount);
            if (count == 1)
            {
                return Physics.Linecast(emitterPosition, listenerPosition, occlusionLayers) ? 1f : 0f;
            }

            Vector3 axis = listenerPosition - emitterPosition;
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.0001f)
            {
                return Physics.Linecast(emitterPosition, listenerPosition, occlusionLayers) ? 1f : 0f;
            }

            Vector3 offset = Vector3.Cross(Vector3.up, axis.normalized) * RayOffsetMeters;
            int blocked = 0;

            // Centre ray first so the dominant path always contributes.
            if (Physics.Linecast(emitterPosition, listenerPosition, occlusionLayers)) blocked++;

            for (int i = 1; i < count; i++)
            {
                int side = (i + 1) >> 1;
                float sign = (i & 1) == 1 ? 1f : -1f;
                Vector3 lateral = offset * (side * sign);

                if (Physics.Linecast(emitterPosition + lateral, listenerPosition + lateral, occlusionLayers))
                {
                    blocked++;
                }
            }

            return blocked / (float)count;
        }
    }
}
