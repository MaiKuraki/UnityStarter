using UnityEngine;
using CycloneGames.Logger;
using UnityEngine.Rendering;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Utility.Runtime
{
    /*
        IMPORTANT USAGE NOTE:
 
        This script by default only works in Edit Mode. For runtime functionality:
 
        Option 1 - Manual control:
        Call TryUpdateSize() manually when the screen/canvas changes 
 
        Option 2 - Automatic runtime updates:
        Remove the #if UNITY_EDITOR preprocessor directive for the Update() method 
 
        Note: Automatic updates will have a small performance impact as it checks 
        for screen size changes every frame.
    */

    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class CustomAspectRatioFitter : MonoBehaviour
    {
        public enum EFitMode { None, FitInTarget, EnvelopeTarget }

        private const string DEBUG_FLAG = "[AspectRatioFitter]";
        private const float MIN_ASPECT_RATIO = 0.001f;
        private readonly Vector2 _sizeTolerance = new Vector2(0.01f, 0.01f);

        [SerializeField][Min(MIN_ASPECT_RATIO)] private float _targetAspectRatio = 16f / 9f;
        [SerializeField] private EFitMode _fitMode = EFitMode.FitInTarget;
        [Tooltip("If empty, uses parent RectTransform")]
        [SerializeField] private RectTransform _fitTarget; // Optional target, defaults to parent 

        // Cache system 
        private RectTransform _rt;
        private EFitMode _lastFitMode;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Vector2 _lastAppliedSize;

        // Safety flags 
        private bool _isInValidation;
        private bool _isDelayedUpdatePending;

        #region Unity Lifecycle 
        private void Reset() => GetRequiredComponents();

        private void OnEnable()
        {
            GetRequiredComponents();
            ResetCache();
            SetDirty();
            TryUpdateSize();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR 
            if (!Application.isPlaying)
                EditorApplication.delayCall -= DelayedUpdate;
#endif 
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR 
            if (!Application.isPlaying)
                EditorApplication.delayCall -= DelayedUpdate;
#endif 
        }

        private void Update()
        {
#if UNITY_EDITOR 
            if (!Application.isPlaying)
            {
                TryUpdateSize();
            }
#endif 
        }

        private void OnRectTransformDimensionsChange() => SetDirty();

        private void OnValidate()
        {
            if (_isInValidation) return;

            _isInValidation = true;
            SetDirty();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!_isDelayedUpdatePending)
                {
                    _isDelayedUpdatePending = true;
                    EditorApplication.delayCall += DelayedUpdate;
                }
                return;
            }
#endif 
            StartCoroutine(DelayedUpdateCoroutine());
        }
        #endregion

        #region Core Functionality 
        [ContextMenu("Update Size")]
        public void SetDirty()
        {
            _lastScreenWidth = -1; // Force update 
            _lastScreenHeight = -1;
            _lastFitMode = EFitMode.None;
        }

        public void TryUpdateSize()
        {
            if (!CanUpdate()) return;

            bool needsUpdate = CheckForChanges();
            if (!needsUpdate && !SizeDifferenceExceedsTolerance()) return;

            UpdateCache();
            ApplyNewSize(CalculateTargetSize());
        }

        private Vector2 CalculateTargetSize()
        {
            RectTransform target = GetEffectiveFitTarget();
            if (target == null || target.rect.size.magnitude < 0.001f)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} No valid target found");
                return _rt.sizeDelta;
            }

            Vector2 targetSize = target.rect.size;
            float targetRatio = targetSize.x / targetSize.y;

            switch (_fitMode)
            {
                case EFitMode.FitInTarget:
                    return targetRatio >= _targetAspectRatio
                        ? new Vector2(targetSize.y * _targetAspectRatio, targetSize.y)
                        : new Vector2(targetSize.x, targetSize.x / _targetAspectRatio);

                case EFitMode.EnvelopeTarget:
                    return targetRatio <= _targetAspectRatio
                        ? new Vector2(targetSize.y * _targetAspectRatio, targetSize.y)
                        : new Vector2(targetSize.x, targetSize.x / _targetAspectRatio);

                default: return _rt.sizeDelta;
            }
        }

        private RectTransform GetEffectiveFitTarget()
        {
            if (_rt == null) return null;

            // If a target is explicitly set, use that 
            if (_fitTarget != null) return _fitTarget;

            // Otherwise use parent if available 
            if (_rt.parent != null)
            {
                RectTransform parentRT = _rt.parent.GetComponent<RectTransform>();
                if (parentRT != null) return parentRT;
            }

            return null;
        }
        #endregion

        #region Helper Methods 
        private void GetRequiredComponents()
        {
            if (_rt == null) _rt = GetComponent<RectTransform>();
        }

        private void ResetCache()
        {
            _lastFitMode = _fitMode;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _lastAppliedSize = _rt.sizeDelta;
        }

        private bool CanUpdate()
        {
            if (_isInValidation) return false;
            if (_rt == null || !isActiveAndEnabled) return false;
            return true;
        }

        private bool CheckForChanges()
        {
            return Screen.width != _lastScreenWidth ||
                   Screen.height != _lastScreenHeight ||
                   _fitMode != _lastFitMode;
        }

        private bool SizeDifferenceExceedsTolerance()
        {
            return Mathf.Abs(_rt.sizeDelta.x - _lastAppliedSize.x) > _sizeTolerance.x ||
                   Mathf.Abs(_rt.sizeDelta.y - _lastAppliedSize.y) > _sizeTolerance.y;
        }

        private void UpdateCache()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _lastFitMode = _fitMode;
        }

        private void ApplyNewSize(Vector2 newSize)
        {
            if (Vector2.Distance(newSize, _lastAppliedSize) < 0.01f) return;

            _rt.sizeDelta = newSize;
            _lastAppliedSize = newSize;
        }
        #endregion

        #region Delayed Update Handlers 
#if UNITY_EDITOR
        private void DelayedUpdate()
        {
            _isDelayedUpdatePending = false;
            _isInValidation = false;
            EditorApplication.delayCall -= DelayedUpdate;
            TryUpdateSize();
        }
#endif

        private System.Collections.IEnumerator DelayedUpdateCoroutine()
        {
            yield return null;
            _isInValidation = false;
            TryUpdateSize();
        }
        #endregion 
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(CustomAspectRatioFitter.EFitMode))]
    public class EFitModeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var options = new[] { "FitInTarget", "EnvelopeTarget" };
            var values = new[] { 1, 2 }; // Skip None (0)

            int current = property.intValue;
            int selected = current > 0 && current <= values.Length ? current - 1 : 0;

            EditorGUI.BeginChangeCheck();
            selected = EditorGUI.Popup(position, label.text, selected, options);
            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = values[selected];
            }

            EditorGUI.EndProperty();
        }
    }
#endif 
}