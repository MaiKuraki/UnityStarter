using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Slides windows horizontally for forward and backward navigation.
    /// Replace navigation uses a cross-fade with the same duration and curve.
    /// </summary>
    public sealed class SlideTransitionCoordinator : IUITransitionCoordinator
    {
        private readonly float _duration;
        private readonly AnimationCurve _curve;
        private readonly CrossFadeTransitionCoordinator _replaceTransition;

        public SlideTransitionCoordinator(float duration = 0.35f, AnimationCurve curve = null)
        {
            _duration = Mathf.Max(0f, duration);
            _curve = curve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            _replaceTransition = new CrossFadeTransitionCoordinator(_duration, _curve);
        }

        public UniTask TransitionAsync(
            UIWindow leaving,
            UIWindow entering,
            NavigationDirection direction,
            CancellationToken cancellationToken = default)
        {
            if (direction == NavigationDirection.Replace)
            {
                return _replaceTransition.TransitionAsync(
                    leaving,
                    entering,
                    direction,
                    cancellationToken);
            }

            return SlideAsync(leaving, entering, direction, cancellationToken);
        }

        private async UniTask SlideAsync(
            UIWindow leaving,
            UIWindow entering,
            NavigationDirection direction,
            CancellationToken cancellationToken)
        {
            if (object.ReferenceEquals(leaving, entering))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            RectTransform leavingTransform = ResolveRectTransform(leaving);
            RectTransform enteringTransform = ResolveRectTransform(entering);
            CanvasGroup leavingGroup = ResolveCanvasGroup(leaving);
            CanvasGroup enteringGroup = ResolveCanvasGroup(entering);

            Vector2 leavingOrigin = leavingTransform != null
                ? leavingTransform.anchoredPosition
                : Vector2.zero;
            Vector2 enteringOrigin = enteringTransform != null
                ? enteringTransform.anchoredPosition
                : Vector2.zero;
            bool leavingInteractable = leavingGroup != null && leavingGroup.interactable;
            bool leavingBlocksRaycasts = leavingGroup != null && leavingGroup.blocksRaycasts;
            bool enteringInteractable = enteringGroup != null && enteringGroup.interactable;
            bool enteringBlocksRaycasts = enteringGroup != null && enteringGroup.blocksRaycasts;

            float slideWidth = ResolveSlideWidth(entering ?? leaving);
            float exitSign = direction == NavigationDirection.Backward ? 1f : -1f;
            float enterSign = -exitSign;
            Vector2 leavingTarget = leavingOrigin + Vector2.right * (slideWidth * exitSign);
            Vector2 enteringStart = enteringOrigin + Vector2.right * (slideWidth * enterSign);
            bool completed = false;

            try
            {
                SetInputEnabled(leavingGroup, false);
                SetInputEnabled(enteringGroup, false);

                if (enteringTransform != null)
                {
                    enteringTransform.anchoredPosition = enteringStart;
                }

                if (_duration > 0f)
                {
                    float elapsed = 0f;
                    while (elapsed < _duration)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        elapsed += Time.unscaledDeltaTime;
                        float progress = _curve.Evaluate(Mathf.Clamp01(elapsed / _duration));

                        if (leavingTransform != null)
                        {
                            leavingTransform.anchoredPosition = Vector2.LerpUnclamped(
                                leavingOrigin,
                                leavingTarget,
                                progress);
                        }

                        if (enteringTransform != null)
                        {
                            enteringTransform.anchoredPosition = Vector2.LerpUnclamped(
                                enteringStart,
                                enteringOrigin,
                                progress);
                        }

                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    }
                }

                if (leavingTransform != null)
                {
                    leavingTransform.anchoredPosition = leavingTarget;
                }

                if (enteringTransform != null)
                {
                    enteringTransform.anchoredPosition = enteringOrigin;
                }

                if (enteringGroup != null)
                {
                    enteringGroup.interactable = enteringInteractable;
                    enteringGroup.blocksRaycasts = enteringBlocksRaycasts;
                }

                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    if (leavingTransform != null)
                    {
                        leavingTransform.anchoredPosition = leavingOrigin;
                    }

                    if (enteringTransform != null)
                    {
                        enteringTransform.anchoredPosition = enteringOrigin;
                    }

                    if (leavingGroup != null)
                    {
                        leavingGroup.interactable = leavingInteractable;
                        leavingGroup.blocksRaycasts = leavingBlocksRaycasts;
                    }

                    if (enteringGroup != null)
                    {
                        enteringGroup.interactable = enteringInteractable;
                        enteringGroup.blocksRaycasts = enteringBlocksRaycasts;
                    }
                }
            }
        }

        private static RectTransform ResolveRectTransform(UIWindow window)
        {
            return window != null ? window.GetComponent<RectTransform>() : null;
        }

        private static CanvasGroup ResolveCanvasGroup(UIWindow window)
        {
            if (window == null)
            {
                return null;
            }

            return window.GetComponent<CanvasGroup>();
        }

        private static void SetInputEnabled(CanvasGroup group, bool enabled)
        {
            if (group == null)
            {
                return;
            }

            group.interactable = enabled;
            group.blocksRaycasts = enabled;
        }

        private static float ResolveSlideWidth(UIWindow window)
        {
            if (window == null)
            {
                return Mathf.Max(0f, Screen.width);
            }

            Canvas canvas = window.GetComponentInParent<Canvas>(true);
            if (canvas != null && canvas.transform is RectTransform canvasTransform)
            {
                float canvasWidth = Mathf.Abs(canvasTransform.rect.width);
                if (canvasWidth > 0f)
                {
                    return canvasWidth;
                }
            }

            RectTransform windowTransform = ResolveRectTransform(window);
            if (windowTransform != null)
            {
                float windowWidth = Mathf.Abs(windowTransform.rect.width);
                if (windowWidth > 0f)
                {
                    return windowWidth;
                }
            }

            return Mathf.Max(0f, Screen.width);
        }
    }
}
