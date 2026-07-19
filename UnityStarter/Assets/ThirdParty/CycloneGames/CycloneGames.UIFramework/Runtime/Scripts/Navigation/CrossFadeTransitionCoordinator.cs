using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Cross-fades two windows using their CanvasGroup components and unscaled time.
    /// </summary>
    public sealed class CrossFadeTransitionCoordinator : IUITransitionCoordinator
    {
        private readonly float _duration;
        private readonly AnimationCurve _curve;

        public CrossFadeTransitionCoordinator(float duration = 0.25f, AnimationCurve curve = null)
        {
            _duration = Mathf.Max(0f, duration);
            _curve = curve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        public async UniTask TransitionAsync(
            UIWindow leaving,
            UIWindow entering,
            NavigationDirection direction,
            CancellationToken cancellationToken = default)
        {
            if (object.ReferenceEquals(leaving, entering))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            CanvasGroup leavingGroup = ResolveCanvasGroup(leaving);
            CanvasGroup enteringGroup = ResolveCanvasGroup(entering);
            var leavingState = new CanvasGroupState(leavingGroup);
            var enteringState = new CanvasGroupState(enteringGroup);
            bool completed = false;

            try
            {
                SetInputEnabled(leavingGroup, false);
                SetInputEnabled(enteringGroup, false);

                if (enteringGroup != null)
                {
                    enteringGroup.alpha = 0f;
                }

                if (_duration > 0f)
                {
                    float elapsed = 0f;
                    while (elapsed < _duration)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        elapsed += Time.unscaledDeltaTime;
                        float progress = _curve.Evaluate(Mathf.Clamp01(elapsed / _duration));

                        if (leavingGroup != null)
                        {
                            leavingGroup.alpha = Mathf.LerpUnclamped(leavingState.Alpha, 0f, progress);
                        }

                        if (enteringGroup != null)
                        {
                            enteringGroup.alpha = progress;
                        }

                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    }
                }

                if (leavingGroup != null)
                {
                    leavingGroup.alpha = 0f;
                }

                if (enteringGroup != null)
                {
                    enteringGroup.alpha = 1f;
                    enteringGroup.interactable = enteringState.Interactable;
                    enteringGroup.blocksRaycasts = enteringState.BlocksRaycasts;
                }

                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    leavingState.Restore();
                    enteringState.Restore();
                }
            }
        }

        private static CanvasGroup ResolveCanvasGroup(UIWindow window)
        {
            if (window == null)
            {
                return null;
            }

            CanvasGroup group = window.GetComponent<CanvasGroup>();

            return group != null ? group : window.gameObject.AddComponent<CanvasGroup>();
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

        private readonly struct CanvasGroupState
        {
            private readonly CanvasGroup _group;

            public readonly float Alpha;
            public readonly bool Interactable;
            public readonly bool BlocksRaycasts;

            public CanvasGroupState(CanvasGroup group)
            {
                _group = group;
                Alpha = group != null ? group.alpha : 1f;
                Interactable = group != null && group.interactable;
                BlocksRaycasts = group != null && group.blocksRaycasts;
            }

            public void Restore()
            {
                if (_group == null)
                {
                    return;
                }

                _group.alpha = Alpha;
                _group.interactable = Interactable;
                _group.blocksRaycasts = BlocksRaycasts;
            }
        }
    }
}
