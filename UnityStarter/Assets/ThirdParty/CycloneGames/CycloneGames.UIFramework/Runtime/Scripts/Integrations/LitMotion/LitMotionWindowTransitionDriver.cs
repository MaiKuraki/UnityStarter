#if LITMOTION_PRESENT
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using LitMotion;

namespace CycloneGames.UIFramework
{
    /// <summary>
    /// LitMotion-based transition driver. Requires com.annulusgames.lit-motion.
    /// </summary>
    public sealed class LitMotionWindowTransitionDriver : IUIWindowTransitionDriver
    {
        private readonly float _duration;
        private readonly Ease _easeIn;
        private readonly Ease _easeOut;

        public LitMotionWindowTransitionDriver(float duration = 0.2f, Ease easeIn = Ease.OutQuad, Ease easeOut = Ease.InQuad)
        {
            _duration = duration;
            _easeIn = easeIn;
            _easeOut = easeOut;
        }

        public async UniTask PlayOpenAsync(UIWindow window, CancellationToken ct)
        {
            if (window == null) return;
            var go = window.gameObject;
            var group = GetOrAddCanvasGroup(go);
            group.alpha = 0f;
            if (!go.activeSelf) go.SetActive(true);

            var motion = LMotion.Create(0f, 1f, _duration)
                .WithEase(_easeIn)
                .Bind(value => group.alpha = value);

            await motion.ToUniTask(cancellationToken: ct);
        }

        public async UniTask PlayCloseAsync(UIWindow window, CancellationToken ct)
        {
            if (window == null) return;
            var go = window.gameObject;
            var group = GetOrAddCanvasGroup(go);

            var motion = LMotion.Create(group.alpha, 0f, _duration)
                .WithEase(_easeOut)
                .Bind(value => group.alpha = value);

            await motion.ToUniTask(cancellationToken: ct);
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }
    }
}
#endif