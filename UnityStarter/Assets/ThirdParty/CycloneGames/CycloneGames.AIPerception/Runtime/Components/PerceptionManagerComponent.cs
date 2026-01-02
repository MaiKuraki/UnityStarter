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
            }
        }
        
        private void Update()
        {
            var manager = SensorManager.Instance;
            if (manager == null) return;
            
            // Sync settings
            manager.UseDeferredJobCompletion = _useDeferredJobCompletion;
            
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
    }
}
