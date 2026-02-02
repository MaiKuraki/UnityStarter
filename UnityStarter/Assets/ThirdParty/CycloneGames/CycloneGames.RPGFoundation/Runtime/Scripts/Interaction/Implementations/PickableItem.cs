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

        // Note: autoInteract is configured in the base class Interactable
        // Set autoInteract = true in Inspector for auto-pickup behavior

        protected override UniTask OnDoInteractAsync(CancellationToken ct)
        {
            // Call base to trigger onInteract event (for sound, etc.)
            base.OnDoInteractAsync(ct);

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

            return UniTask.CompletedTask;
        }

        protected virtual void OnPickedUp() { }
    }
}