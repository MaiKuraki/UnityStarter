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
            ct.ThrowIfCancellationRequested();
            if (window == null) return;

            var context = CreateContext(window);
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
            ct.ThrowIfCancellationRequested();
            if (window == null) return;

            var context = CreateContext(window);
            context.CanvasGroup.interactable = false;
            context.CanvasGroup.blocksRaycasts = false;

            await PlayCloseCoreAsync(context, CloseConfig, ct);

            context.CanvasGroup.alpha = 0f;
        }

        protected virtual async UniTask PlayOpenCoreAsync(TransitionContext ctx, TransitionConfigBase config, CancellationToken ct)
        {
            if (config is CompositeConfig composite)
            {
                await AnimateCompositeAsync(ctx, composite, true, EaseIn, ct);
                return;
            }

            var tween = CreateAnimationTween(ctx, config, true, EaseIn);
            await AwaitTweenAsync(tween, ct);
        }

        protected virtual async UniTask PlayCloseCoreAsync(TransitionContext ctx, TransitionConfigBase config, CancellationToken ct)
        {
            if (config is CompositeConfig composite)
            {
                await AnimateCompositeAsync(ctx, composite, false, EaseOut, ct);
                return;
            }

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
                case CompositeConfig:
                    return default;
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
            Vector2 to = isOpen
                ? ctx.OriginalPosition
                : GetSlideOffset(ctx.RectTransform, config, ctx.OriginalPosition);
            return Tween.UIAnchoredPosition(ctx.RectTransform, to, config.Duration, ease, useUnscaledTime: true);
        }

        /// <summary>
        /// Starts every configured property tween and awaits the owned tweens as one operation.
        /// </summary>
        protected virtual async UniTask AnimateCompositeAsync(
            TransitionContext ctx,
            CompositeConfig config,
            bool isOpen,
            Ease ease,
            CancellationToken cancellationToken)
        {
            int taskCount = (config.UseFade ? 1 : 0) +
                            (config.Scale != null ? 1 : 0) +
                            (config.Slide != null && ctx.RectTransform != null ? 1 : 0);
            if (taskCount == 0)
            {
                return;
            }

            var tasks = new UniTask[taskCount];
            int index = 0;
            if (config.UseFade)
            {
                tasks[index++] = AwaitTweenAsync(
                    CreateFadeTween(ctx, config.Duration, isOpen, ease),
                    cancellationToken);
            }

            if (config.Scale != null)
            {
                tasks[index++] = AwaitTweenAsync(
                    CreateScaleTween(ctx, config.Scale, isOpen, ease),
                    cancellationToken);
            }

            if (config.Slide != null && ctx.RectTransform != null)
            {
                tasks[index] = AwaitTweenAsync(
                    CreateSlideTween(ctx, config.Slide, isOpen, ease),
                    cancellationToken);
            }

            await UniTask.WhenAll(tasks);
        }

        /// <summary>
        /// Awaits a tween to completion, cancellation, or external kill. PrimeTween's isAlive
        /// reads the shared tween registry, so polling a struct copy stays valid and an
        /// externally killed tween completes the await instead of hanging forever.
        /// PrimeTween has no SetLink equivalent (DOTween KillOnDestroy); tweens bound to a
        /// destroyed target stop on their next tick, and callers drive cancellation through
        /// the window lifetime token.
        /// </summary>
        private static async UniTask AwaitTweenAsync(Tween tween, CancellationToken ct)
        {
            try
            {
                while (tween.isAlive)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (System.OperationCanceledException)
            {
                if (tween.isAlive)
                    tween.Stop();
                throw;
            }
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

        protected Vector2 GetSlideOffset(
            RectTransform rt,
            SlideConfig config,
            Vector2? origin = null)
        {
            var rect = rt.rect;
            Vector2 start = origin ?? rt.anchoredPosition;
            return config.Direction switch
            {
                SlideDirection.Left => start + new Vector2(-rect.width * config.Offset, 0),
                SlideDirection.Right => start + new Vector2(rect.width * config.Offset, 0),
                SlideDirection.Top => start + new Vector2(0, rect.height * config.Offset),
                SlideDirection.Bottom => start + new Vector2(0, -rect.height * config.Offset),
                _ => start
            };
        }

        protected TransitionContext CreateContext(UIWindow window)
        {
            var go = window.gameObject;
            var rt = window.transform as RectTransform;
            var group = UIWindowTransitionUtility.GetOrAddCanvasGroup(window);

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
