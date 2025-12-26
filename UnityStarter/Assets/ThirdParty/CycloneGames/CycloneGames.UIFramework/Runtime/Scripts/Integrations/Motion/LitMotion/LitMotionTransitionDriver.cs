#if LIT_MOTION_PRESENT
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using LitMotion;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Extensible LitMotion-based transition driver.
    /// 
    /// INHERITANCE:
    /// External projects can inherit and override animation methods:
    /// 
    /// public class MyDriver : LitMotionTransitionDriver
    /// {
    ///     protected override UniTask AnimateFade(...) { ... custom logic ... }
    ///     protected override UniTask AnimateScale(...) { ... custom logic ... }
    /// }
    /// 
    /// Or implement completely custom transitions:
    /// 
    /// public class MyDriver : LitMotionTransitionDriver
    /// {
    ///     public MyDriver(MyCustomConfig config) : base(config) { }
    ///     
    ///     protected override UniTask PlayOpenCoreAsync(...)
    ///     {
    ///         // Handle MyCustomConfig here
    ///     }
    /// }
    /// </summary>
    public class LitMotionTransitionDriver : IUIWindowTransitionDriver
    {
        protected readonly TransitionConfigBase OpenConfig;
        protected readonly TransitionConfigBase CloseConfig;
        protected readonly Ease EaseIn;
        protected readonly Ease EaseOut;

        /// <summary>
        /// Creates a driver with the same config for open and close.
        /// </summary>
        public LitMotionTransitionDriver(
            TransitionConfigBase config,
            Ease easeIn = Ease.OutQuad,
            Ease easeOut = Ease.InQuad)
            : this(config, config, easeIn, easeOut)
        {
        }

        /// <summary>
        /// Creates a driver with separate configs for open and close.
        /// </summary>
        public LitMotionTransitionDriver(
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
            await AnimateConfigAsync(ctx, config, true, EaseIn, ct);
        }

        /// <summary>
        /// Override to customize close animation logic.
        /// </summary>
        protected virtual async UniTask PlayCloseCoreAsync(TransitionContext ctx, TransitionConfigBase config, CancellationToken ct)
        {
            await AnimateConfigAsync(ctx, config, false, EaseOut, ct);
        }

        /// <summary>
        /// Handles animation based on config type. Override to support custom config types.
        /// </summary>
        protected virtual async UniTask AnimateConfigAsync(TransitionContext ctx, TransitionConfigBase config, bool isOpen, Ease ease, CancellationToken ct)
        {
            switch (config)
            {
                case FadeConfig fade:
                    await AnimateFade(ctx, fade.Duration, isOpen, ease, ct);
                    break;
                case ScaleConfig scale:
                    await AnimateScale(ctx, scale, isOpen, ease, ct);
                    break;
                case SlideConfig slide:
                    await AnimateSlide(ctx, slide, isOpen, ease, ct);
                    break;
                case CompositeConfig composite:
                    await AnimateComposite(ctx, composite, isOpen, ease, ct);
                    break;
                default:
                    // Unknown config type - do fade as fallback
                    await AnimateFade(ctx, config.Duration, isOpen, ease, ct);
                    break;
            }
        }

        /// <summary>Override to customize fade animation.</summary>
        protected virtual async UniTask AnimateFade(TransitionContext ctx, float duration, bool isOpen, Ease ease, CancellationToken ct)
        {
            float from = isOpen ? 0f : ctx.CanvasGroup.alpha;
            float to = isOpen ? 1f : 0f;
            var handle = LMotion.Create(from, to, duration)
                .WithEase(ease)
                .Bind(v => ctx.CanvasGroup.alpha = v);
            try { await handle.ToUniTask(cancellationToken: ct); }
            catch (System.OperationCanceledException) { handle.Cancel(); }
        }

        /// <summary>Override to customize scale animation.</summary>
        protected virtual async UniTask AnimateScale(TransitionContext ctx, ScaleConfig config, bool isOpen, Ease ease, CancellationToken ct)
        {
            float from = isOpen ? config.ScaleFrom : 1f;
            float to = isOpen ? 1f : config.ScaleFrom;
            var handle = LMotion.Create(from, to, config.Duration)
                .WithEase(ease)
                .Bind(v => ctx.Transform.localScale = ctx.OriginalScale * v);
            try { await handle.ToUniTask(cancellationToken: ct); }
            catch (System.OperationCanceledException) { handle.Cancel(); }
        }

        /// <summary>Override to customize slide animation.</summary>
        protected virtual async UniTask AnimateSlide(TransitionContext ctx, SlideConfig config, bool isOpen, Ease ease, CancellationToken ct)
        {
            if (ctx.RectTransform == null) return;
            Vector2 offset = GetSlideOffset(ctx.RectTransform, config);
            Vector2 from = isOpen ? offset : ctx.OriginalPosition;
            Vector2 to = isOpen ? ctx.OriginalPosition : offset;
            var handle = LMotion.Create(from, to, config.Duration)
                .WithEase(ease)
                .Bind(v => ctx.RectTransform.anchoredPosition = v);
            try { await handle.ToUniTask(cancellationToken: ct); }
            catch (System.OperationCanceledException) { handle.Cancel(); }
        }

        /// <summary>Override to customize composite animation.</summary>
        protected virtual async UniTask AnimateComposite(TransitionContext ctx, CompositeConfig config, bool isOpen, Ease ease, CancellationToken ct)
        {
            var tasks = new System.Collections.Generic.List<UniTask>(3);
            
            if (config.UseFade)
                tasks.Add(AnimateFade(ctx, config.Duration, isOpen, ease, ct));
            if (config.Scale != null)
                tasks.Add(AnimateScale(ctx, config.Scale, isOpen, ease, ct));
            if (config.Slide != null)
                tasks.Add(AnimateSlide(ctx, config.Slide, isOpen, ease, ct));
            
            if (tasks.Count > 0)
                await UniTask.WhenAll(tasks);
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
