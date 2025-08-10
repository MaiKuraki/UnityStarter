/*
 * A standalone, intelligent safe area handler that respects system-defined insets
 * while providing optional symmetry and home indicator exclusion.
 */

using UnityEngine;
namespace CycloneGames.Utility.Runtime
{
    [RequireComponent(typeof(RectTransform))]
    [ExecuteInEditMode]
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


        private RectTransform rectTransform;
        private Rect lastSafeArea;
        private ScreenOrientation lastOrientation;

        void OnEnable()
        {
            rectTransform = GetComponent<RectTransform>();
            lastSafeArea = new Rect(0, 0, 0, 0);
            lastOrientation = Screen.orientation;
            ApplySafeArea();
        }

        void Update()
        {
            Rect currentSafeArea = Screen.safeArea;

            if (currentSafeArea != lastSafeArea || Screen.orientation != lastOrientation)
            {
                lastSafeArea = currentSafeArea;
                lastOrientation = Screen.orientation;
                ApplySafeArea();
            }
        }

        /// <summary>
        /// Calculates and applies the safe area insets to the RectTransform's anchors.
        /// </summary>
        private void ApplySafeArea()
        {
            if (rectTransform == null) return;

            Rect safeArea = Screen.safeArea;

            // Calculate initial pixel insets from screen edges.
            float topInset = Screen.height - safeArea.yMax;
            float bottomInset = safeArea.yMin;
            float leftInset = safeArea.xMin;
            float rightInset = Screen.width - safeArea.xMax;

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
                float maxHorizontal = Mathf.Max(leftInset, rightInset);
                leftInset = maxHorizontal;
                rightInset = maxHorizontal;
            }

            // Apply final manual padding for fine-tuning.
            topInset += manualTopPadding;
            bottomInset += manualBottomPadding;
            leftInset += manualLeftPadding;
            rightInset += manualRightPadding;

            // Convert final pixel insets back to normalized anchor positions.
            Vector2 anchorMin = new Vector2(leftInset / Screen.width, bottomInset / Screen.height);
            Vector2 anchorMax = new Vector2(1f - (rightInset / Screen.width), 1f - (topInset / Screen.height));

            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
        }
    }
}
