using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    [CreateAssetMenu(menuName = "CycloneGames/RPG Foundation/Interaction/System Config")]
    public sealed class InteractionSystemConfig : ScriptableObject
    {
        [Header("Spatial Grid")]
        [SerializeField] private float cellSize = 10f;
        [SerializeField] private bool is2DMode;

        [Header("Detection Defaults")]
        [SerializeField] private int maxInteractables = 64;
        [SerializeField] private float positionUpdateThreshold = 1f;

        [Header("LOD Defaults")]
        [SerializeField] private float nearDistance = 5f;
        [SerializeField] private float farDistance = 15f;
        [SerializeField] private float disableDistance = 50f;

        [SerializeField] private float nearIntervalMs = 33f;
        [SerializeField] private float farIntervalMs = 150f;
        [SerializeField] private float veryFarIntervalMs = 300f;
        [SerializeField] private float sleepIntervalMs = 500f;
        [SerializeField] private float sleepEnterMs = 1000f;

        [Header("Performance")]
        [SerializeField] private int maxLosChecksPerFrame = 32;
        [SerializeField] private bool useLosSpatialCache = true;

        public float CellSize => cellSize;
        public bool Is2DMode => is2DMode;
        public int MaxInteractables => maxInteractables;
        public float PositionUpdateThreshold => positionUpdateThreshold;
        public float NearDistance => nearDistance;
        public float FarDistance => farDistance;
        public float DisableDistance => disableDistance;
        public float NearIntervalMs => nearIntervalMs;
        public float FarIntervalMs => farIntervalMs;
        public float VeryFarIntervalMs => veryFarIntervalMs;
        public float SleepIntervalMs => sleepIntervalMs;
        public float SleepEnterMs => sleepEnterMs;
        public int MaxLosChecksPerFrame => maxLosChecksPerFrame;
        public bool UseLosSpatialCache => useLosSpatialCache;

        private static InteractionSystemConfig s_default;

        public static InteractionSystemConfig Default
        {
            get => s_default;
            set => s_default = value;
        }
    }
}
