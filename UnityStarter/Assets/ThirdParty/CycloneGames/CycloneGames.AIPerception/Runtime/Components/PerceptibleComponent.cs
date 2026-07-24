using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Unity authoring adapter that publishes one continuously sampled perceptible to the world
    /// registry. The registry captures dynamic values once at the start of each perception frame.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    [AddComponentMenu("CycloneGames/AI/Perceptible")]
    public sealed class PerceptibleComponent : MonoBehaviour, IPerceptible
    {
        [Header("Type")]
        [SerializeField] private int _typeId = PerceptibleTypes.Default;
        [SerializeField] private string _tag = string.Empty;

        [Header("Detection")]
        [SerializeField, Min(0f)] private float _detectionRadius = 1f;
        [SerializeField] private bool _isDetectable = true;
        [SerializeField] private Transform _losPoint;

        [Header("Continuous Hearing Emission")]
        [SerializeField, Min(0f)] private float _loudness = 1f;
        [SerializeField] private bool _isSoundSource;

        // Retained for serialized compatibility. Diagnostics are provided by the custom Inspector.
        [SerializeField, HideInInspector] private bool _showDebugOverlay;

        private PerceptibleHandle _handle = PerceptibleHandle.Invalid;
        private int _perceptibleId;

        public int PerceptibleId => _perceptibleId;
        public int PerceptibleTypeId => _typeId;
        public float DetectionRadius => math.max(0f, _detectionRadius);
        public float Loudness => math.max(0f, _loudness);
        public bool IsSoundSource => _isSoundSource;
        public string Tag => _tag;
        public float3 Position => transform.position;
        public PerceptibleHandle Handle => _handle;

        public bool IsDetectable
        {
            get => _isDetectable && isActiveAndEnabled;
            set
            {
                if (_isDetectable == value)
                {
                    return;
                }

                _isDetectable = value;
                NotifyChanged();
            }
        }

        public bool ShowDebugOverlay
        {
            get => _showDebugOverlay;
            set => _showDebugOverlay = value;
        }

        public float3 GetLOSPoint()
        {
            return _losPoint != null ? (float3)_losPoint.position : (float3)transform.position;
        }

        private void OnEnable()
        {
            _perceptibleId = GetInstanceID();
            TryRegister();
        }

        private void OnDisable()
        {
            if (PerceptibleRegistry.HasInstance)
            {
                PerceptibleRegistry.Instance.Unregister(_handle);
            }

            _handle = PerceptibleHandle.Invalid;
        }

        private void OnValidate()
        {
            if (!float.IsFinite(_detectionRadius) || _detectionRadius < 0f)
            {
                _detectionRadius = 0f;
            }

            if (!float.IsFinite(_loudness) || _loudness < 0f)
            {
                _loudness = 0f;
            }

            if (Application.isPlaying)
            {
                NotifyChanged();
            }
        }

        public void SetTypeId(int typeId)
        {
            if (_typeId == typeId)
            {
                return;
            }

            _typeId = typeId;
            NotifyChanged();
        }

        public void SetDetectionRadius(float radius)
        {
            float value = float.IsFinite(radius) ? math.max(0f, radius) : 0f;
            if (_detectionRadius.Equals(value))
            {
                return;
            }

            _detectionRadius = value;
            NotifyChanged();
        }

        public void SetLoudness(float loudness)
        {
            float value = float.IsFinite(loudness) ? math.max(0f, loudness) : 0f;
            if (_loudness.Equals(value))
            {
                return;
            }

            _loudness = value;
            NotifyChanged();
        }

        public void SetSoundSource(bool isSoundSource)
        {
            if (_isSoundSource == isSoundSource)
            {
                return;
            }

            _isSoundSource = isSoundSource;
            NotifyChanged();
        }

        /// <summary>
        /// Attempts registration after an earlier world-capacity failure. This is a cold-path
        /// recovery operation; callers should invoke it after capacity is released, not per frame.
        /// </summary>
        public bool TryRegister()
        {
            PerceptibleRegistry registry = PerceptibleRegistry.Instance;
            if (registry.IsValid(_handle))
            {
                return true;
            }

            _handle = registry.Register(this);
            return registry.IsValid(_handle);
        }

        private static void NotifyChanged()
        {
            if (PerceptibleRegistry.HasInstance)
            {
                PerceptibleRegistry.Instance.MarkDirty();
            }
        }
    }
}
