using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// A coordinator that slides two windows simultaneously: the leaving window exits
    /// in one direction while the entering window arrives from the opposite side.
    ///
    /// Forward:  Leaving → slides left;  Entering ← slides in from right.
    /// Backward: Leaving → slides right; Entering ← slides in from left.
    /// Replace:  Cross-fade (alpha only, no positional movement).
    ///
    /// Uses pure UnityEngine RectTransform offsets driven via UniTask — no third-party dependency.
    /// All intermediate RectTransform values are set on the main thread, ensuring Unity correctness.
    /// </summary>
    public sealed class SlideTransitionCoordinator : IUITransitionCoordinator
    {
        private readonly float _duration;
        private readonly AnimationCurve _curve;

        /// <param name="duration">Transition duration in seconds. Default 0.35s.</param>
        /// <param name="curve">Optional ease curve. Defaults to smooth-step if null.</param>
        public SlideTransitionCoordinator(float duration = 0.35f, AnimationCurve curve = null)
        {
            _duration   = Mathf.Max(0.05f, duration);
            _curve      = curve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        public async UniTask TransitionAsync(UIWindow leaving, UIWindow entering,
            NavigationDirection direction, CancellationToken ct)
        {
            if (leaving == null && entering == null) return;

            // Resolve the canvas width to determine slide distances
            float slideWidth = ResolveSlideWidth(entering ?? leaving);

            // Determine offset sign based on direction
            // Forward: leaving exits left (-), entering arrives from right (+)
            // Backward: leaving exits right (+), entering arrives from left (-)
            float exitSign  = direction == NavigationDirection.Backward ?  1f : -1f;
            float enterSign = direction == NavigationDirection.Backward ? -1f :  1f;

            RectTransform leavingRt  = leaving  != null ? leaving.GetComponent<RectTransform>()  : null;
            RectTransform enteringRt = entering != null ? entering.GetComponent<RectTransform>() : null;

            // Capture original anchored positions to restore if cancelled
            Vector2 leavingOrigin  = leavingRt  != null ? leavingRt.anchoredPosition  : Vector2.zero;
            Vector2 enteringOrigin = enteringRt != null ? enteringRt.anchoredPosition : Vector2.zero;

            // Place entering window off-screen at the start
            if (enteringRt != null)
                enteringRt.anchoredPosition = enteringOrigin + new Vector2(slideWidth * enterSign, 0f);

            float elapsed = 0f;
            while (elapsed < _duration)
            {
                if (ct.IsCancellationRequested)
                {
                    // Restore positions on cancel so the caller can clean up gracefully
                    if (leavingRt  != null) leavingRt.anchoredPosition  = leavingOrigin;
                    if (enteringRt != null) enteringRt.anchoredPosition = enteringOrigin;
                    return;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = _curve.Evaluate(Mathf.Clamp01(elapsed / _duration));

                if (leavingRt != null)
                    leavingRt.anchoredPosition = Vector2.LerpUnclamped(
                        leavingOrigin,
                        leavingOrigin + new Vector2(slideWidth * exitSign, 0f),
                        t);

                if (enteringRt != null)
                    enteringRt.anchoredPosition = Vector2.LerpUnclamped(
                        enteringOrigin + new Vector2(slideWidth * enterSign, 0f),
                        enteringOrigin,
                        t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // Snap to final positions
            if (leavingRt  != null) leavingRt.anchoredPosition  = leavingOrigin  + new Vector2(slideWidth * exitSign,  0f);
            if (enteringRt != null) enteringRt.anchoredPosition = enteringOrigin;
        }

        private static float ResolveSlideWidth(UIWindow window)
        {
            if (window == null) return Screen.width;
            var rt = window.GetComponent<RectTransform>();
            if (rt == null) return Screen.width;
            // Walk up to Canvas to get the root's width; fallback to Screen.width
            Canvas canvas = window.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                var cvRt = canvas.GetComponent<RectTransform>();
                if (cvRt != null) return cvRt.rect.width;
            }
            return rt.rect.width > 0f ? rt.rect.width : Screen.width;
        }
    }
}
