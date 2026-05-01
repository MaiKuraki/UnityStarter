using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Manager component that drives the perception system update loop.
    /// Supports both immediate and deferred job completion modes.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("CycloneGames/AI/Perception Manager")]
    public class PerceptionManagerComponent : MonoBehaviour
    {
        private static PerceptionManagerComponent _instance;
        private static bool _isQuitting;
        
        [Header("Performance")]
        [Tooltip("When enabled, Jobs are batched and completed in LateUpdate for better performance with many sensors.")]
        [SerializeField] private bool _useDeferredJobCompletion = false;
        
        [Header("LOD (Level of Detail)")]
        [Tooltip("Reference transform for distance-based LOD. Typically the main camera or player. Null disables LOD.")]
        [SerializeField] private Transform _lodReference;
        [SerializeField] private SensorLODLevel[] _lodLevels = SensorLODLevel.DefaultLevels;
        
        public static PerceptionManagerComponent Instance
        {
            get
            {
                if (_isQuitting) return null;
                
                if (_instance == null)
                {
                    var go = new GameObject("[PerceptionManager]");
                    _instance = go.AddComponent<PerceptionManagerComponent>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        public static bool HasInstance => _instance != null;
        
        /// <summary>
        /// When true, jobs are batched and completed in LateUpdate for better performance.
        /// When false (default), jobs complete immediately for simpler debugging.
        /// </summary>
        public bool UseDeferredJobCompletion
        {
            get => _useDeferredJobCompletion;
            set
            {
                _useDeferredJobCompletion = value;
                if (SensorManager.HasInstance)
                {
                    SensorManager.Instance.UseDeferredJobCompletion = value;
                }
            }
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            
            // Sync settings to SensorManager
            if (SensorManager.HasInstance)
            {
                SensorManager.Instance.UseDeferredJobCompletion = _useDeferredJobCompletion;
                SensorManager.Instance.ConfigureLOD(_lodReference, _lodLevels);
            }
        }
        
        private void Update()
        {
            var manager = SensorManager.Instance;
            if (manager == null) return;
            
            // Sync settings
            manager.UseDeferredJobCompletion = _useDeferredJobCompletion;
            manager.ConfigureLOD(_lodReference, _lodLevels);
            
            // Update sensors (schedules jobs)
            manager.Update(Time.deltaTime);
        }
        
        private void LateUpdate()
        {
            var manager = SensorManager.Instance;
            if (manager == null) return;
            
            // In deferred mode, complete batched jobs and process results
            if (_useDeferredJobCompletion)
            {
                manager.LateUpdate();
            }
        }
        
        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
            {
                SensorManager.Instance?.Dispose();
                PerceptibleRegistry.Instance?.Dispose();
                _instance = null;
            }
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_lodReference == null || _lodLevels == null || _lodLevels.Length == 0) return;
            
            var pos = _lodReference.position;
            
            for (int i = 0; i < _lodLevels.Length; i++)
            {
                float dist = _lodLevels[i].Distance;
                float alpha = 0.25f - i * 0.06f;
                if (alpha < 0.05f) alpha = 0.05f;
                
                UnityEditor.Handles.color = new Color(0.3f, 0.8f, 0.4f, alpha);
                UnityEditor.Handles.DrawWireDisc(pos, Vector3.up, dist);
                
                // Draw small markers at cardinal directions
                float markerAlpha = 0.5f - i * 0.12f;
                if (markerAlpha < 0.1f) markerAlpha = 0.1f;
                UnityEditor.Handles.color = new Color(0.4f, 0.9f, 0.5f, markerAlpha);
                
                var labelPos = pos + Vector3.forward * dist;
                UnityEditor.Handles.Label(labelPos, $"×{_lodLevels[i].FrequencyMultiplier:F2}");
            }
        }
#endif
    }
}
