using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// A coordinator that cross-fades two windows simultaneously via CanvasGroup alpha.
    /// Suitable for pop-overs, scene-like transitions, or any case where no directional
    /// slide is appropriate. Works regardless of NavigationDirection.
    /// </summary>
    public sealed class CrossFadeTransitionCoordinator : IUITransitionCoordinator
    {
        private readonly float _duration;
        private readonly AnimationCurve _curve;

        public CrossFadeTransitionCoordinator(float duration = 0.25f, AnimationCurve curve = null)
        {
            _duration = Mathf.Max(0.05f, duration);
            _curve    = curve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        public async UniTask TransitionAsync(UIWindow leaving, UIWindow entering,
            NavigationDirection direction, CancellationToken ct)
        {
            CanvasGroup leavingCg  = leaving  != null ? EnsureCanvasGroup(leaving)  : null;
            CanvasGroup enteringCg = entering != null ? EnsureCanvasGroup(entering) : null;

            // Start entering at alpha 0
            if (enteringCg != null) enteringCg.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < _duration)
            {
                if (ct.IsCancellationRequested)
                {
                    if (leavingCg  != null) leavingCg.alpha  = 0f;
                    if (enteringCg != null) enteringCg.alpha = 1f;
                    return;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = _curve.Evaluate(Mathf.Clamp01(elapsed / _duration));

                if (leavingCg  != null) leavingCg.alpha  = 1f - t;
                if (enteringCg != null) enteringCg.alpha = t;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            if (leavingCg  != null) leavingCg.alpha  = 0f;
            if (enteringCg != null) enteringCg.alpha = 1f;
        }

        private static CanvasGroup EnsureCanvasGroup(UIWindow window)
        {
            var cg = window.GetComponent<CanvasGroup>();
            if (cg == null) cg = window.gameObject.AddComponent<CanvasGroup>();
            return cg;
        }
    }
}
