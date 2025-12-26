#if DO_TWEEN_PRESENT
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DG.Tweening;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Extensible DOTween-based transition driver.
    /// 
    /// INHERITANCE:
    /// External projects can inherit and override animation methods:
    /// 
    /// public class MyDriver : DOTweenTransitionDriver
    /// {
    ///     protected override Tween CreateFadeTween(...) { ... custom logic ... }
    ///     protected override Tween CreateScaleTween(...) { ... custom logic ... }
    /// }
    /// 
    /// Or implement completely custom transitions:
    /// 
    /// public class MyDriver : DOTweenTransitionDriver
    /// {
    ///     public MyDriver(MyCustomConfig config) : base(config) { }
    ///     
    ///     protected override UniTask PlayOpenCoreAsync(...)
    ///     {
    ///         // Handle MyCustomConfig here
    ///     }
    /// }
    /// </summary>
    public class DOTweenTransitionDriver : IUIWindowTransitionDriver
    {
        protected readonly TransitionConfigBase OpenConfig;
        protected readonly TransitionConfigBase CloseConfig;
        protected readonly Ease EaseIn;
        protected readonly Ease EaseOut;

        /// <summary>
        /// Creates a driver with the same config for open and close.
        /// </summary>
        public DOTweenTransitionDriver(
            TransitionConfigBase config,
            Ease easeIn = Ease.OutQuad,
            Ease easeOut = Ease.InQuad)
            : this(config, config, easeIn, easeOut)
        {
        }

        /// <summary>
        /// Creates a driver with separate configs for open and close.
        /// </summary>
        public DOTweenTransitionDriver(
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

        /// <summary>
        /// Override to customize open animation logic.
        /// </summary>
        protected virtual async UniTask PlayOpenCoreAsync(TransitionContext ctx, TransitionConfigBase config, CancellationToken ct)
        {
            var sequence = CreateAnimationSequence(ctx, config, true, EaseIn);
            await ExecuteSequenceAsync(sequence, ct);
        }

        /// <summary>
        /// Override to customize close animation logic.
        /// </summary>
        protected virtual async UniTask PlayCloseCoreAsync(TransitionContext ctx, TransitionConfigBase config, CancellationToken ct)
        {
            var sequence = CreateAnimationSequence(ctx, config, false, EaseOut);
            await ExecuteSequenceAsync(sequence, ct);
        }

        /// <summary>
        /// Creates animation sequence based on config type. Override to support custom config types.
        /// </summary>
        protected virtual Sequence CreateAnimationSequence(TransitionContext ctx, TransitionConfigBase config, bool isOpen, Ease ease)
        {
            var sequence = DOTween.Sequence().SetUpdate(true);

            switch (config)
            {
                case FadeConfig fade:
                    sequence.Join(CreateFadeTween(ctx, fade.Duration, isOpen, ease));
                    break;
                case ScaleConfig scale:
                    sequence.Join(CreateScaleTween(ctx, scale, isOpen, ease));
                    break;
                case SlideConfig slide:
                    if (ctx.RectTransform != null)
                        sequence.Join(CreateSlideTween(ctx, slide, isOpen, ease));
                    break;
                case CompositeConfig composite:
                    AddCompositeTweens(sequence, ctx, composite, isOpen, ease);
                    break;
                default:
                    // Unknown config type - do fade as fallback
                    sequence.Join(CreateFadeTween(ctx, config.Duration, isOpen, ease));
                    break;
            }

            return sequence;
        }

        /// <summary>Override to customize fade tween creation.</summary>
        protected virtual Tween CreateFadeTween(TransitionContext ctx, float duration, bool isOpen, Ease ease)
        {
            float to = isOpen ? 1f : 0f;
            return ctx.CanvasGroup.DOFade(to, duration).SetEase(ease);
        }

        /// <summary>Override to customize scale tween creation.</summary>
        protected virtual Tween CreateScaleTween(TransitionContext ctx, ScaleConfig config, bool isOpen, Ease ease)
        {
            Vector3 to = isOpen ? ctx.OriginalScale : ctx.OriginalScale * config.ScaleFrom;
            return ctx.Transform.DOScale(to, config.Duration).SetEase(ease);
        }

        /// <summary>Override to customize slide tween creation.</summary>
        protected virtual Tween CreateSlideTween(TransitionContext ctx, SlideConfig config, bool isOpen, Ease ease)
        {
            Vector2 to = isOpen ? ctx.OriginalPosition : GetSlideOffset(ctx.RectTransform, config);
            return ctx.RectTransform.DOAnchorPos(to, config.Duration).SetEase(ease);
        }

        /// <summary>Override to customize composite animation.</summary>
        protected virtual void AddCompositeTweens(Sequence sequence, TransitionContext ctx, CompositeConfig config, bool isOpen, Ease ease)
        {
            if (config.UseFade)
                sequence.Join(CreateFadeTween(ctx, config.Duration, isOpen, ease));
            if (config.Scale != null)
                sequence.Join(CreateScaleTween(ctx, config.Scale, isOpen, ease));
            if (config.Slide != null && ctx.RectTransform != null)
                sequence.Join(CreateSlideTween(ctx, config.Slide, isOpen, ease));
        }

        protected virtual async UniTask ExecuteSequenceAsync(Sequence sequence, CancellationToken ct)
        {
            try
            {
                await sequence.ToUniTask(cancellationToken: ct);
            }
            catch (System.OperationCanceledException)
            {
                sequence.Kill();
            }
        }

        protected virtual void KillExistingTweens(TransitionContext ctx)
        {
            ctx.CanvasGroup.DOKill();
            ctx.Transform.DOKill();
            if (ctx.RectTransform != null) ctx.RectTransform.DOKill();
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

        /// <summary>
        /// Context object containing all necessary references for animation.
        /// Passed to all animation methods for extensibility.
        /// </summary>
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
