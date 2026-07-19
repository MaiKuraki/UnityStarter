using System;

using UnityEngine;
using UnityEngine.Serialization;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Drives a <see cref="RectTransform"/> so that it remains inside a bounded screen safe area.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AdaptiveSafeAreaFitter : MonoBehaviour
    {
        [FormerlySerializedAs("extendIntoBottomSafeArea")]
        [SerializeField]
        [Tooltip("Allows the fitted rectangle to extend behind the bottom system gesture area.")]
        private bool _extendIntoBottomSafeArea = true;

        [FormerlySerializedAs("enforceVerticalSymmetry")]
        [SerializeField]
        [Tooltip("After optional bottom extension, makes the bottom inset at least as large as the top inset. This can add bottom inset back to balance a top cutout.")]
        private bool _enforceVerticalSymmetry = true;

        [FormerlySerializedAs("enforceHorizontalSymmetry")]
        [SerializeField]
        [Tooltip("Uses the larger of the left and right insets on both sides.")]
        private bool _enforceHorizontalSymmetry = true;

        [FormerlySerializedAs("manualTopPadding")]
        [SerializeField, Min(0f)]
        private float _manualTopPadding;

        [FormerlySerializedAs("manualBottomPadding")]
        [SerializeField, Min(0f)]
        private float _manualBottomPadding;

        [FormerlySerializedAs("manualLeftPadding")]
        [SerializeField, Min(0f)]
        private float _manualLeftPadding;

        [FormerlySerializedAs("manualRightPadding")]
        [SerializeField, Min(0f)]
        private float _manualRightPadding;

        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private ScreenOrientation _lastOrientation;
        private DrivenRectTransformTracker _tracker;
        private Vector2 _lastAppliedAnchorMin;
        private Vector2 _lastAppliedAnchorMax;
        private bool _refreshRequested = true;
        private bool _isApplying;

        public bool ExtendIntoBottomSafeArea
        {
            get => _extendIntoBottomSafeArea;
            set => SetAndRefresh(ref _extendIntoBottomSafeArea, value);
        }

        public bool EnforceVerticalSymmetry
        {
            get => _enforceVerticalSymmetry;
            set => SetAndRefresh(ref _enforceVerticalSymmetry, value);
        }

        public bool EnforceHorizontalSymmetry
        {
            get => _enforceHorizontalSymmetry;
            set => SetAndRefresh(ref _enforceHorizontalSymmetry, value);
        }

        public Vector4 PaddingPixels
        {
            get => new Vector4(
                _manualLeftPadding,
                _manualBottomPadding,
                _manualRightPadding,
                _manualTopPadding);
            set
            {
                _manualLeftPadding = SanitizePadding(value.x);
                _manualBottomPadding = SanitizePadding(value.y);
                _manualRightPadding = SanitizePadding(value.z);
                _manualTopPadding = SanitizePadding(value.w);
                RequestRefresh();
            }
        }

        [Obsolete("Use ExtendIntoBottomSafeArea.")]
        public bool extendIntoBottomSafeArea
        {
            get => ExtendIntoBottomSafeArea;
            set => ExtendIntoBottomSafeArea = value;
        }

        [Obsolete("Use EnforceVerticalSymmetry.")]
        public bool enforceVerticalSymmetry
        {
            get => EnforceVerticalSymmetry;
            set => EnforceVerticalSymmetry = value;
        }

        [Obsolete("Use EnforceHorizontalSymmetry.")]
        public bool enforceHorizontalSymmetry
        {
            get => EnforceHorizontalSymmetry;
            set => EnforceHorizontalSymmetry = value;
        }

        [Obsolete("Use PaddingPixels.w.")]
        public float manualTopPadding
        {
            get => _manualTopPadding;
            set => SetPaddingAndRefresh(ref _manualTopPadding, value);
        }

        [Obsolete("Use PaddingPixels.y.")]
        public float manualBottomPadding
        {
            get => _manualBottomPadding;
            set => SetPaddingAndRefresh(ref _manualBottomPadding, value);
        }

        [Obsolete("Use PaddingPixels.x.")]
        public float manualLeftPadding
        {
            get => _manualLeftPadding;
            set => SetPaddingAndRefresh(ref _manualLeftPadding, value);
        }

        [Obsolete("Use PaddingPixels.z.")]
        public float manualRightPadding
        {
            get => _manualRightPadding;
            set => SetPaddingAndRefresh(ref _manualRightPadding, value);
        }

        private void OnEnable()
        {
            if (!TryGetComponent(out _rectTransform))
            {
                enabled = false;
                return;
            }

            _tracker.Clear();
            _tracker.Add(
                this,
                _rectTransform,
                DrivenTransformProperties.Anchors |
                DrivenTransformProperties.SizeDelta |
                DrivenTransformProperties.AnchoredPosition);

            _refreshRequested = true;
            Refresh();
        }

        private void OnDisable()
        {
            _tracker.Clear();
        }

        private void Update()
        {
            if (_rectTransform == null)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            ScreenOrientation orientation = Screen.orientation;
            bool screenChanged =
                safeArea != _lastSafeArea ||
                screenWidth != _lastScreenWidth ||
                screenHeight != _lastScreenHeight ||
                orientation != _lastOrientation;
            bool drivenValuesChanged =
                _rectTransform.anchorMin != _lastAppliedAnchorMin ||
                _rectTransform.anchorMax != _lastAppliedAnchorMax ||
                _rectTransform.offsetMin != Vector2.zero ||
                _rectTransform.offsetMax != Vector2.zero;

            if (_refreshRequested || screenChanged || drivenValuesChanged)
            {
                ApplySafeArea(safeArea, screenWidth, screenHeight, orientation);
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_isApplying)
            {
                RequestRefresh();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _manualTopPadding = SanitizePadding(_manualTopPadding);
            _manualBottomPadding = SanitizePadding(_manualBottomPadding);
            _manualLeftPadding = SanitizePadding(_manualLeftPadding);
            _manualRightPadding = SanitizePadding(_manualRightPadding);
            RequestRefresh();
        }
#endif

        /// <summary>
        /// Recalculates and applies the current screen safe area immediately.
        /// </summary>
        public void Refresh()
        {
            if (_rectTransform == null && !TryGetComponent(out _rectTransform))
            {
                return;
            }

            ApplySafeArea(Screen.safeArea, Screen.width, Screen.height, Screen.orientation);
        }

        private void ApplySafeArea(
            Rect safeArea,
            int screenWidth,
            int screenHeight,
            ScreenOrientation orientation)
        {
            _lastSafeArea = safeArea;
            _lastScreenWidth = screenWidth;
            _lastScreenHeight = screenHeight;
            _lastOrientation = orientation;
            if (_rectTransform == null)
            {
                _refreshRequested = false;
                return;
            }

            SafeAreaPolicy policy = CreatePolicy();
            if (!SafeAreaUtility.TryCalculateAnchors(
                    safeArea,
                    screenWidth,
                    screenHeight,
                    in policy,
                    out Vector2 anchorMin,
                    out Vector2 anchorMax))
            {
                _refreshRequested = false;
                return;
            }

            _isApplying = true;
            try
            {
                if (_rectTransform.anchorMin != anchorMin)
                {
                    _rectTransform.anchorMin = anchorMin;
                }

                if (_rectTransform.anchorMax != anchorMax)
                {
                    _rectTransform.anchorMax = anchorMax;
                }

                if (_rectTransform.offsetMin != Vector2.zero)
                {
                    _rectTransform.offsetMin = Vector2.zero;
                }

                if (_rectTransform.offsetMax != Vector2.zero)
                {
                    _rectTransform.offsetMax = Vector2.zero;
                }

                _lastAppliedAnchorMin = anchorMin;
                _lastAppliedAnchorMax = anchorMax;
            }
            finally
            {
                _isApplying = false;
                _refreshRequested = false;
            }
        }

        private SafeAreaPolicy CreatePolicy()
        {
            return new SafeAreaPolicy(
                _extendIntoBottomSafeArea,
                _enforceVerticalSymmetry,
                _enforceHorizontalSymmetry,
                PaddingPixels);
        }

        private void RequestRefresh()
        {
            _refreshRequested = true;
        }

        private void SetAndRefresh(ref bool field, bool value)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            RequestRefresh();
        }

        private void SetPaddingAndRefresh(ref float field, float value)
        {
            value = SanitizePadding(value);
            if (Mathf.Approximately(field, value))
            {
                return;
            }

            field = value;
            RequestRefresh();
        }

        private static float SanitizePadding(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }
    }
}
