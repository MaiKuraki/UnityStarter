/*
 * A standalone, intelligent safe area handler that respects system-defined insets
 * while providing optional symmetry and home indicator exclusion.
 */

using UnityEngine;
namespace CycloneGames.Utility.Runtime
{
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public class AdaptiveSafeAreaFitter : MonoBehaviour
    {
        [Header("Behavior Settings")]

        [Tooltip("If true, the UI will extend into the bottom safe area. On iOS, this means drawing behind the Home Indicator. This is useful for creating a more immersive, full-screen background, but interactive elements in this zone may be hard to use due to system gestures.")]
        public bool extendIntoBottomSafeArea = true;

        [Tooltip("If true, the bottom inset is increased to match the top inset if the top is larger. Balances a top notch in portrait mode.")]
        public bool enforceVerticalSymmetry = true;

        [Tooltip("If true, left/right insets are matched to the larger of the two. Balances a notch/indicator in landscape mode.")]
        public bool enforceHorizontalSymmetry = true;


        [Header("Manual Padding (in pixels)")]

        [Tooltip("Additional padding applied to the top inset.")]
        public float manualTopPadding = 0f;

        [Tooltip("Additional padding applied to the bottom inset.")]
        public float manualBottomPadding = 0f;

        [Tooltip("Additional padding applied to the left inset.")]
        public float manualLeftPadding = 0f;

        [Tooltip("Additional padding applied to the right inset.")]
        public float manualRightPadding = 0f;


        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private ScreenOrientation _lastOrientation;
        private DrivenRectTransformTracker _tracker;
        private Vector2 _lastAppliedAnchorMin;
        private Vector2 _lastAppliedAnchorMax;

        void OnEnable()
        {
            _rectTransform = GetComponent<RectTransform>();

            // Set up tracker once — locks anchors and offsets in Inspector to prevent manual edits
            _tracker.Clear();
            _tracker.Add(this, _rectTransform,
                DrivenTransformProperties.Anchors |
                DrivenTransformProperties.SizeDelta |
                DrivenTransformProperties.AnchoredPosition);

            // Force initial apply by invalidating cached state
            _lastSafeArea = new Rect(0, 0, 0, 0);
            _lastScreenWidth = 0;
            _lastScreenHeight = 0;
            _lastOrientation = Screen.orientation;
            Refresh();
        }

        void OnDisable()
        {
            _tracker.Clear();
        }

        void Update()
        {
            int sw = Screen.width;
            int sh = Screen.height;
            Rect sa = Screen.safeArea;
            ScreenOrientation orient = Screen.orientation;

            bool screenChanged = sa != _lastSafeArea || sw != _lastScreenWidth || sh != _lastScreenHeight || orient != _lastOrientation;

            // Detect anchor tampering (e.g. user changed anchor preset via Inspector popup)
            bool anchorTampered = _rectTransform != null &&
                (_rectTransform.anchorMin != _lastAppliedAnchorMin ||
                 _rectTransform.anchorMax != _lastAppliedAnchorMax ||
                 _rectTransform.offsetMin != Vector2.zero ||
                 _rectTransform.offsetMax != Vector2.zero);

            if (screenChanged || anchorTampered)
            {
                _lastSafeArea = sa;
                _lastScreenWidth = sw;
                _lastScreenHeight = sh;
                _lastOrientation = orient;
                ApplySafeArea(sa, sw, sh);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Re-applies safe area when settings are changed in the Inspector.
        /// </summary>
        private void OnValidate()
        {
            // OnValidate can fire before OnEnable; guard against null
            if (_rectTransform == null) return;
            // Delay to avoid modifying transform during validation
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && _rectTransform != null)
                {
                    Refresh();
                }
            };
        }
#endif

        /// <summary>
        /// Forces an immediate safe area recalculation. Call this after programmatic
        /// resolution changes or when re-enabling UI panels at runtime.
        /// </summary>
        public void Refresh()
        {
            int sw = Screen.width;
            int sh = Screen.height;
            _lastSafeArea = Screen.safeArea;
            _lastScreenWidth = sw;
            _lastScreenHeight = sh;
            _lastOrientation = Screen.orientation;
            ApplySafeArea(_lastSafeArea, sw, sh);
        }

        /// <summary>
        /// Calculates and applies the safe area insets to the RectTransform's anchors.
        /// </summary>
        private void ApplySafeArea(Rect safeArea, int screenWidth, int screenHeight)
        {
            if (_rectTransform == null) return;
            if (screenWidth <= 0 || screenHeight <= 0) return;

            // Calculate initial pixel insets from screen edges.
            float topInset = screenHeight - safeArea.yMax;
            float bottomInset = safeArea.yMin;
            float leftInset = safeArea.xMin;
            float rightInset = screenWidth - safeArea.xMax;

            #region Do not Change this pipe
            // On modern iPhones, the bottom safe area is reserved for the Home Indicator.
            // Swiping up from this area returns to the home screen. While it's generally
            // best to avoid placing interactive UI here, extending non-interactive elements
            // (like backgrounds) into this space can create a more seamless look.
            // Large, easily tappable buttons may also be acceptable, as users are less
            // likely to accidentally trigger the system gesture.
            if (extendIntoBottomSafeArea)
            {
                bottomInset = 0;
            }

            // The symmetry logic is then applied to the *result* of the previous step.
            // This ensures that even if the bottom inset was cleared, it can be restored
            // to match the top inset (e.g., for a notch). This sequence guarantees that
            // the aesthetic need for symmetry correctly overrides the functional choice
            // to extend into the bottom area when a top notch is present.
            if (enforceVerticalSymmetry)
            {
                bottomInset = Mathf.Max(bottomInset, topInset);
            }
            #endregion

            if (enforceHorizontalSymmetry)
            {
                float maxHorizontal = leftInset > rightInset ? leftInset : rightInset;
                leftInset = maxHorizontal;
                rightInset = maxHorizontal;
            }

            // Apply final manual padding for fine-tuning.
            topInset += manualTopPadding;
            bottomInset += manualBottomPadding;
            leftInset += manualLeftPadding;
            rightInset += manualRightPadding;

            // Convert final pixel insets to normalized anchor positions, clamped to [0,1].
            float invW = 1f / screenWidth;
            float invH = 1f / screenHeight;

            Vector2 anchorMin = new Vector2(
                Mathf.Clamp01(leftInset * invW),
                Mathf.Clamp01(bottomInset * invH));
            Vector2 anchorMax = new Vector2(
                Mathf.Clamp01(1f - rightInset * invW),
                Mathf.Clamp01(1f - topInset * invH));

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
            // Zero out offsets so the RectTransform exactly matches the anchor-defined area.
            // Without this, pre-existing offset values would shift the element away from
            // the computed safe area, which is the most common source of incorrect fitting.
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;

            // Cache applied values for tampering detection
            _lastAppliedAnchorMin = anchorMin;
            _lastAppliedAnchorMax = anchorMax;
        }
    }
}
