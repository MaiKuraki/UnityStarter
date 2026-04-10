using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Lightweight Perlin-noise camera shake processor.
    ///
    /// This works in the same conceptual layer as Cinemachine Perlin noise, but stays inside
    /// GameplayFramework's post-processor pipeline so it remains deterministic with your custom
    /// CameraMode stack and collision processing.
    /// </summary>
    public sealed class PerlinNoiseShakePostProcessor : ICameraPostProcessor
    {
        private readonly float seedX;
        private readonly float seedY;
        private readonly float seedZ;

        private float trauma;
        private float time;

        /// <summary>Translation amplitude in world units at full trauma (1.0).</summary>
        public float PositionAmplitude { get; set; } = 0.18f;

        /// <summary>Rotation amplitude in degrees at full trauma (1.0).</summary>
        public float RotationAmplitude { get; set; } = 2.2f;

        /// <summary>Noise sample frequency in Hz.</summary>
        public float Frequency { get; set; } = 22f;

        /// <summary>Trauma decay per second.</summary>
        public float TraumaDecay { get; set; } = 1.5f;

        /// <summary>Exponent applied to trauma before output. 2~3 gives punchier falloff.</summary>
        public float TraumaExponent { get; set; } = 2f;

        public bool Enabled { get; set; } = true;

        public PerlinNoiseShakePostProcessor(int seed = 1337)
        {
            var rand = new System.Random(seed);
            seedX = (float)rand.NextDouble() * 1000f + 37.1f;
            seedY = (float)rand.NextDouble() * 1000f + 73.3f;
            seedZ = (float)rand.NextDouble() * 1000f + 11.7f;
        }

        /// <summary>Add trauma in [0,1]. Multiple calls accumulate and clamp to 1.</summary>
        public void AddTrauma(float amount)
        {
            trauma = Mathf.Clamp01(trauma + Mathf.Max(0f, amount));
        }

        public void ClearTrauma()
        {
            trauma = 0f;
        }

        public CameraPose Process(CameraPose desiredPose, CameraContext context, float deltaTime)
        {
            if (!Enabled || trauma <= 0.0001f)
            {
                trauma = Mathf.Max(0f, trauma - TraumaDecay * Mathf.Max(0f, deltaTime));
                return desiredPose;
            }

            time += Mathf.Max(0f, deltaTime) * Mathf.Max(0.01f, Frequency);
            float shake = Mathf.Pow(trauma, Mathf.Max(0.01f, TraumaExponent));

            float nx = SampleSignedNoise(seedX, time);
            float ny = SampleSignedNoise(seedY, time + 13.37f);
            float nz = SampleSignedNoise(seedZ, time + 29.71f);

            Vector3 posOffset = new Vector3(nx, ny, nz) * PositionAmplitude * shake;
            Vector3 rotEuler = new Vector3(ny, nx, nz) * RotationAmplitude * shake;
            Quaternion rotOffset = Quaternion.Euler(rotEuler);

            trauma = Mathf.Max(0f, trauma - TraumaDecay * Mathf.Max(0f, deltaTime));
            return new CameraPose(desiredPose.Position + posOffset, desiredPose.Rotation * rotOffset, desiredPose.Fov);
        }

        private static float SampleSignedNoise(float seed, float t)
        {
            return Mathf.PerlinNoise(seed, t) * 2f - 1f;
        }
    }
}
