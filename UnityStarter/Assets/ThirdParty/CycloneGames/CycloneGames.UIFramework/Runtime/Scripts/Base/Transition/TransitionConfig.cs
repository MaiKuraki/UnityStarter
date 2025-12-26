using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Direction for slide animations.
    /// </summary>
    public enum SlideDirection
    {
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// Base class for transition configurations.
    /// Inherit to create custom transition configs for external extensions.
    /// 
    /// EXTENSIBILITY:
    /// External projects can create their own config classes:
    /// public class MyCustomConfig : TransitionConfigBase { ... }
    /// 
    /// Then implement a custom driver that handles them:
    /// public class MyCustomDriver : IUIWindowTransitionDriver { ... }
    /// </summary>
    public abstract class TransitionConfigBase
    {
        /// <summary>Animation duration in seconds.</summary>
        public float Duration { get; }

        protected TransitionConfigBase(float duration = 0.25f)
        {
            Duration = duration;
        }
    }

    /// <summary>
    /// Fade transition configuration.
    /// </summary>
    public sealed class FadeConfig : TransitionConfigBase
    {
        public static readonly FadeConfig Default = new FadeConfig();

        public FadeConfig(float duration = 0.25f) : base(duration) { }
    }

    /// <summary>
    /// Scale transition configuration.
    /// </summary>
    public sealed class ScaleConfig : TransitionConfigBase
    {
        public float ScaleFrom { get; }
        
        public static readonly ScaleConfig Default = new ScaleConfig();

        public ScaleConfig(float scaleFrom = 0.8f, float duration = 0.25f) : base(duration)
        {
            ScaleFrom = scaleFrom;
        }
    }

    /// <summary>
    /// Slide transition configuration.
    /// </summary>
    public sealed class SlideConfig : TransitionConfigBase
    {
        public SlideDirection Direction { get; }
        public float Offset { get; }
        
        public static readonly SlideConfig Left = new SlideConfig(SlideDirection.Left);
        public static readonly SlideConfig Right = new SlideConfig(SlideDirection.Right);
        public static readonly SlideConfig Top = new SlideConfig(SlideDirection.Top);
        public static readonly SlideConfig Bottom = new SlideConfig(SlideDirection.Bottom);

        public SlideConfig(
            SlideDirection direction = SlideDirection.Bottom, 
            float offset = 1f, 
            float duration = 0.3f) : base(duration)
        {
            Direction = direction;
            Offset = offset;
        }
    }

    /// <summary>
    /// Composite transition that combines multiple effects.
    /// Supports any combination of fade, scale, and slide.
    /// 
    /// USAGE:
    /// var config = new CompositeConfig(
    ///     fade: true,
    ///     scale: new ScaleConfig(0.9f),
    ///     slide: SlideConfig.Bottom
    /// );
    /// </summary>
    public sealed class CompositeConfig : TransitionConfigBase
    {
        public bool UseFade { get; }
        public ScaleConfig Scale { get; }
        public SlideConfig Slide { get; }
        
        /// <summary>Fade + Scale preset.</summary>
        public static readonly CompositeConfig FadeScale = new CompositeConfig(
            fade: true, 
            scale: new ScaleConfig(0.9f)
        );
        
        /// <summary>Fade + Slide from bottom preset.</summary>
        public static readonly CompositeConfig FadeSlideBottom = new CompositeConfig(
            fade: true, 
            slide: new SlideConfig(SlideDirection.Bottom, 0.3f)
        );

        public CompositeConfig(
            bool fade = false,
            ScaleConfig scale = null,
            SlideConfig slide = null,
            float duration = 0.25f) : base(duration)
        {
            UseFade = fade;
            Scale = scale;
            Slide = slide;
        }
    }
}
