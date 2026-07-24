using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Unity lifecycle and composition bridge for the process-local perception world.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [AddComponentMenu("CycloneGames/AI/Perception Manager")]
    public sealed class PerceptionManagerComponent : MonoBehaviour
    {
        private static PerceptionManagerComponent _instance;
        private static bool _isQuitting;

        [Header("Scheduling")]
        [SerializeField] private bool _useDeferredJobCompletion = true;

        [Header("World Capacity")]
        [SerializeField, Min(0)] private int _maximumPerceptibles = 16384;
        [SerializeField, Min(0.001f)] private float _spatialCellSize = 20f;

        [Header("LOD")]
        [SerializeField] private Transform _lodReference;
        [SerializeField] private SensorLODLevel[] _lodLevels = SensorLODLevel.DefaultLevels;

        private bool _settingsDirty = true;

        public static PerceptionManagerComponent Instance
        {
            get
            {
                if (_isQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {
                    var owner = new GameObject("[PerceptionManager]");
                    _instance = owner.AddComponent<PerceptionManagerComponent>();
                    DontDestroyOnLoad(owner);
                }

                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        public bool UseDeferredJobCompletion
        {
            get => _useDeferredJobCompletion;
            set
            {
                if (_useDeferredJobCompletion == value)
                {
                    return;
                }

                _useDeferredJobCompletion = value;
                _settingsDirty = true;
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
            _settingsDirty = true;
            ApplySettings();
        }

        private void OnEnable()
        {
            _settingsDirty = true;
        }

        private void Update()
        {
            ApplySettings();
            SensorManager.ExistingInstance?.Update(Time.deltaTime);
        }

        private void LateUpdate()
        {
            SensorManager.ExistingInstance?.LateUpdate();
        }

        private void OnDisable()
        {
            SensorManager.ExistingInstance?.LateUpdate();
        }

        private void OnValidate()
        {
            if (_maximumPerceptibles < 0)
            {
                _maximumPerceptibles = 0;
            }

            if (!float.IsFinite(_spatialCellSize) || _spatialCellSize < 0.001f)
            {
                _spatialCellSize = 0.001f;
            }

            _settingsDirty = true;
        }

        private void ApplySettings()
        {
            if (!_settingsDirty || _isQuitting)
            {
                return;
            }

            SensorManager manager = SensorManager.Instance;
            PerceptibleRegistry registry = PerceptibleRegistry.Instance;
            manager.UseDeferredJobCompletion = _useDeferredJobCompletion;
            manager.ConfigureLOD(_lodReference, _lodLevels);
            if (!registry.TrySetMaxCapacity(_maximumPerceptibles))
            {
                Debug.LogError(
                    $"[AIPerception] Maximum perceptibles ({_maximumPerceptibles}) cannot be lower than the active count ({registry.Count}). The previous limit remains active.",
                    this);
            }
            registry.SetSpatialCellSize(_spatialCellSize);
            _settingsDirty = false;
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
            SensorManager.OnApplicationQuit();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            SensorManager.ResetInstance();
            PerceptibleRegistry.ResetInstance();
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SensorManager.ResetInstance();
            PerceptibleRegistry.ResetInstance();
            _instance = null;
            _isQuitting = false;
        }
    }
}
