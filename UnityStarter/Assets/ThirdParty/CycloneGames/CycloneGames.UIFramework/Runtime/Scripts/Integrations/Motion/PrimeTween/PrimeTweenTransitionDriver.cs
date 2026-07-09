#if CYCLONEGAMES_HAS_PRIMETWEEN
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using PrimeTween;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Extensible PrimeTween-based transition driver.
    ///
    /// INHERITANCE:
    /// External projects can inherit and override animation methods:
    ///
    /// public class MyDriver : PrimeTweenTransitionDriver
    /// {
    ///     protected override Tween CreateFadeTween(...) { ... custom logic ... }
    ///     protected override Tween CreateScaleTween(...) { ... custom logic ... }
    /// }
    /// </summary>
    public class PrimeTweenTransitionDriver : IUIWindowTransitionDriver
    {
        protected readonly TransitionConfigBase OpenConfig;
        protected readonly TransitionConfigBase CloseConfig;
        protected readonly Ease EaseIn;
        protected readonly Ease EaseOut;

        public PrimeTweenTransitionDriver(
            TransitionConfigBase config,
            Ease easeIn = Ease.OutQuad,
            Ease easeOut = Ease.InQuad)
            : this(config, config, easeIn, easeOut)
        {
        }

        public PrimeTweenTransitionDriver(
            TransitionConfigBase openConfig,
            TransitionConfigBase closeConfig,
            Ease easeIn = Ease.OutQuad,
            Ease easeOut = Ease.InQuad)
        {
            OpenConfig = openConfig ?? FadeConfig.Default;
            CloseConfig = closeConfig ?? FadeConfig.Default;
            EaseIn = easeIn;
            EaseOut = easeOut;
        }

        public async UniTask PlayOpenAsync(UIWindow window, CancellationToken ct)
        {
            if (window == null || ct.IsCancellationRequested) return;

            var context = CreateContext(window);
            KillExistingTweens(context);
            SetupInitialState(context, OpenConfig, true);
            if (!context.GameObject.activeSelf) context.GameObject.SetActive(true);

            await PlayOpenCoreAsync(context, OpenConfig, ct);

            if (!ct.IsCancellationRequested)
            {
                FinalizeOpenState(context);
            }
        }

        public async UniTask PlayCloseAsync(UIWindow window, CancellationToken ct)
        {
            if (window == null || ct.IsCancellationRequested) return;

            var context = CreateContext(window);
            KillExistingTweens(context);
            context.CanvasGroup.interactable = false;
            context.CanvasGroup.blocksRaycasts = false;

            await PlayCloseCoreAsync(context, CloseConfig, ct);

            context.CanvasGroup.alpha = 0f;
        }

        protected virtual async UniTask PlayOpenCoreAsync(TransitionContext ctx, TransitionConfigBase config, CancellationToken ct)
        {
            var tween = CreateAnimationTween(ctx, config, true, EaseIn);
            await AwaitTweenAsync(tween, ct);
        }

        protected virtual async UniTask PlayCloseCoreAsync(TransitionContext ctx, TransitionConfigBase config, CancellationToken ct)
        {
            var tween = CreateAnimationTween(ctx, config, false, EaseOut);
            await AwaitTweenAsync(tween, ct);
        }

        protected virtual Tween CreateAnimationTween(TransitionContext ctx, TransitionConfigBase config, bool isOpen, Ease ease)
        {
            switch (config)
            {
                case FadeConfig fade:
                    return CreateFadeTween(ctx, fade.Duration, isOpen, ease);
                case ScaleConfig scale:
                    return CreateScaleTween(ctx, scale, isOpen, ease);
                case SlideConfig slide:
                    if (ctx.RectTransform != null)
                        return CreateSlideTween(ctx, slide, isOpen, ease);
                    return default;
                case CompositeConfig composite:
                    return CreateCompositeTween(ctx, composite, isOpen, ease);
                default:
                    return CreateFadeTween(ctx, config.Duration, isOpen, ease);
            }
        }

        protected virtual Tween CreateFadeTween(TransitionContext ctx, float duration, bool isOpen, Ease ease)
        {
            float to = isOpen ? 1f : 0f;
            return Tween.Alpha(ctx.CanvasGroup, to, duration, ease, useUnscaledTime: true);
        }

        protected virtual Tween CreateScaleTween(TransitionContext ctx, ScaleConfig config, bool isOpen, Ease ease)
        {
            Vector3 to = isOpen ? ctx.OriginalScale : ctx.OriginalScale * config.ScaleFrom;
            return Tween.Scale(ctx.Transform, to, config.Duration, ease, useUnscaledTime: true);
        }

        protected virtual Tween CreateSlideTween(TransitionContext ctx, SlideConfig config, bool isOpen, Ease ease)
        {
            Vector2 to = isOpen ? ctx.OriginalPosition : GetSlideOffset(ctx.RectTransform, config);
            return Tween.UIAnchoredPosition(ctx.RectTransform, to, config.Duration, ease, useUnscaledTime: true);
        }

        /// <summary>
        /// Creates a composite animation. In PrimeTween, Sequence and Tween are separate types
        /// with no implicit conversion, so we fire all sub-tweens simultaneously (they animate
        /// different properties — alpha, scale, position — so there is no conflict) and return
        /// a dummy delay tween that matches the composite duration for await purposes.
        /// </summary>
        protected virtual Tween CreateCompositeTween(TransitionContext ctx, CompositeConfig config, bool isOpen, Ease ease)
        {
            if (config.UseFade)
                CreateFadeTween(ctx, config.Duration, isOpen, ease);
            if (config.Scale != null)
                CreateScaleTween(ctx, config.Scale, isOpen, ease);
            if (config.Slide != null && ctx.RectTransform != null)
                CreateSlideTween(ctx, config.Slide, isOpen, ease);

            return Tween.Delay(config.Duration, useUnscaledTime: true);
        }

        private static async UniTask AwaitTweenAsync(Tween tween, CancellationToken ct)
        {
            if (!tween.isAlive) return;

            var tcs = new UniTaskCompletionSource();
            // Capture tween reference in a one-element array to avoid struct-copy staleness
            // across the async boundary — the tween struct may have been replaced by .OnComplete().
            var tweenHolder = new Tween[1];
            tweenHolder[0] = tween.OnComplete(tcs, static state => state.TrySetResult());

            try
            {
                await tcs.Task.AttachExternalCancellation(ct);
            }
            catch (System.OperationCanceledException)
            {
                if (tweenHolder[0].isAlive)
                    tweenHolder[0].Stop();
                throw;
            }
        }

        protected virtual void KillExistingTweens(TransitionContext ctx)
        {
            Tween.StopAll(onTarget: ctx.CanvasGroup);
            Tween.StopAll(onTarget: ctx.Transform);
            if (ctx.RectTransform != null)
                Tween.StopAll(onTarget: ctx.RectTransform);
        }

        protected virtual void SetupInitialState(TransitionContext ctx, TransitionConfigBase config, bool isOpen)
        {
            ctx.CanvasGroup.interactable = false;
            ctx.CanvasGroup.blocksRaycasts = false;

            switch (config)
            {
                case FadeConfig:
                    if (isOpen) ctx.CanvasGroup.alpha = 0f;
                    break;
                case ScaleConfig scale:
                    if (isOpen) ctx.Transform.localScale = ctx.OriginalScale * scale.ScaleFrom;
                    break;
                case SlideConfig slide:
                    if (isOpen && ctx.RectTransform != null)
                        ctx.RectTransform.anchoredPosition = GetSlideOffset(ctx.RectTransform, slide);
                    break;
                case CompositeConfig composite:
                    if (composite.UseFade && isOpen) ctx.CanvasGroup.alpha = 0f;
                    if (composite.Scale != null && isOpen) ctx.Transform.localScale = ctx.OriginalScale * composite.Scale.ScaleFrom;
                    if (composite.Slide != null && isOpen && ctx.RectTransform != null)
                        ctx.RectTransform.anchoredPosition = GetSlideOffset(ctx.RectTransform, composite.Slide);
                    break;
            }
        }

        protected virtual void FinalizeOpenState(TransitionContext ctx)
        {
            ctx.CanvasGroup.alpha = 1f;
            ctx.CanvasGroup.interactable = true;
            ctx.CanvasGroup.blocksRaycasts = true;
            ctx.Transform.localScale = ctx.OriginalScale;
            if (ctx.RectTransform != null)
                ctx.RectTransform.anchoredPosition = ctx.OriginalPosition;
        }

        protected Vector2 GetSlideOffset(RectTransform rt, SlideConfig config)
        {
            var rect = rt.rect;
            return config.Direction switch
            {
                SlideDirection.Left => rt.anchoredPosition + new Vector2(-rect.width * config.Offset, 0),
                SlideDirection.Right => rt.anchoredPosition + new Vector2(rect.width * config.Offset, 0),
                SlideDirection.Top => rt.anchoredPosition + new Vector2(0, rect.height * config.Offset),
                SlideDirection.Bottom => rt.anchoredPosition + new Vector2(0, -rect.height * config.Offset),
                _ => rt.anchoredPosition
            };
        }

        protected TransitionContext CreateContext(UIWindow window)
        {
            var go = window.gameObject;
            var rt = window.transform as RectTransform;
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();

            return new TransitionContext
            {
                GameObject = go,
                Transform = go.transform,
                RectTransform = rt,
                CanvasGroup = group,
                OriginalPosition = rt != null ? rt.anchoredPosition : Vector2.zero,
                OriginalScale = go.transform.localScale
            };
        }

        protected struct TransitionContext
        {
            public GameObject GameObject;
            public Transform Transform;
            public RectTransform RectTransform;
            public CanvasGroup CanvasGroup;
            public Vector2 OriginalPosition;
            public Vector3 OriginalScale;
        }
    }
}
#endif
