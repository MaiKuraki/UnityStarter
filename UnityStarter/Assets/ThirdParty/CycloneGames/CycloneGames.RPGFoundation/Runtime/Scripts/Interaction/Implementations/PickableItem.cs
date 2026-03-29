using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public class PickableItem : Interactable
    {
        [Header("Pickable Settings")]
        [SerializeField] private bool destroyOnPickup = true;
        [SerializeField] private GameObject pickupEffectPrefab;

        protected override void Awake()
        {
            base.Awake();
            // Subclass or Inspector can assign a project-specific channel.
            // No default override — the framework does not assume game semantics.
        }

        protected override async UniTask OnDoInteractAsync(CancellationToken ct)
        {
            await base.OnDoInteractAsync(ct);

            OnPickedUp();

            if (pickupEffectPrefab != null)
            {
                EffectPoolSystem.Spawn(pickupEffectPrefab, transform.position, Quaternion.identity);
            }

            if (destroyOnPickup)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }

        protected virtual void OnPickedUp() { }
    }
}