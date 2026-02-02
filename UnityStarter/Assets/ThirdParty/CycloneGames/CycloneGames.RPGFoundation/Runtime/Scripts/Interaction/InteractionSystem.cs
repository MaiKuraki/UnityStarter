using System;
using VitalRouter;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    [Routes]
    public partial class InteractionSystem : MonoBehaviour, IInteractionSystem
    {
        private IDisposable _subscription;
        private bool _initialized;

        private void Awake() => Initialize();

        private void Start()
        {
            _subscription = this.MapTo(Router.Default);
        }

        private void OnDestroy() => Dispose();

        public void Initialize()
        {
            if (_initialized) return;
            EffectPoolSystem.Initialize();
            InteractionDetector.ClearComponentCache();
            _initialized = true;
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _subscription?.Dispose();
            _subscription = null;
            EffectPoolSystem.Dispose();
            InteractionDetector.ClearComponentCache();
            _initialized = false;
        }

        public async UniTask On(InteractionCommand command)
        {
            await ProcessInteractionAsync(command.Target);
        }

        public async UniTask ProcessInteractionAsync(IInteractable target)
        {
            if (target == null || !target.IsInteractable) return;
            await target.TryInteractAsync(destroyCancellationToken);
        }
    }
}