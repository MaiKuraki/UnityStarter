using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    /// <summary>
    /// Built-in interactable that disappears once activated. Playback of a pickup effect is left to
    /// the product: override <see cref="OnPickedUp"/> and play whatever the project uses (object
    /// pool, VFX Graph, ECS system). The interaction module does not own presentation concerns.
    /// </summary>
    public class PickableItem : Interactable
    {
        [Header("Pickable Settings")]
        [SerializeField] private bool destroyOnPickup = true;

        protected override async UniTask OnDoInteractAsync(CancellationToken ct)
        {
            await base.OnDoInteractAsync(ct);

            OnPickedUp();

            if (destroyOnPickup)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }

        /// <summary>Runs once when the item is picked up, before it is destroyed.</summary>
        protected virtual void OnPickedUp() { }
    }
}
