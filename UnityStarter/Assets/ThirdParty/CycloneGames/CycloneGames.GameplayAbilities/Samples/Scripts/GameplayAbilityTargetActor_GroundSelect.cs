using System;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// A MonoBehaviour that implements ITargetActor. It continuously traces from the mouse cursor
    /// to the ground, displaying a visual indicator. It waits for a 'Confirm' signal to send back the TargetData.
    /// </summary>
    public class GameplayAbilityTargetActor_GroundSelect : MonoBehaviour, ITargetActor
    {
        public event Action<TargetData> OnTargetDataReady;
        public event Action OnCanceled;

        [Tooltip("The visual indicator for the selection area.")]
        public GameObject SelectionIndicator;
        [Tooltip("The layer mask representing the ground.")]
        public LayerMask GroundLayerMask;

        private GameplayAbility owningAbility;
        private Action<TargetData> onTargetDataReadyCallback;
        private Action onCancelledCallback;
        private RaycastHit lastValidHit;
        private bool isTargeting = false;

        public void Configure(GameplayAbility ability, Action<TargetData> onTargetDataReady, Action onCancelled)
        {
            this.owningAbility = ability;
            this.onTargetDataReadyCallback = onTargetDataReady;
            this.onCancelledCallback = onCancelled;
        }

        public void StartTargeting()
        {
            isTargeting = true;
            if (SelectionIndicator)
            {
                SelectionIndicator.SetActive(true);
            }
        }
        
        void Update()
        {
            if (!isTargeting) return;

            // Trace from camera to mouse position
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, GroundLayerMask))
            {
                lastValidHit = hit;
                if (SelectionIndicator)
                {
                    SelectionIndicator.transform.position = hit.point;
                }
            }
        }

        public void ConfirmTargeting()
        {
            if (!isTargeting) return;
            isTargeting = false;

            var targetData = GameplayAbilityTargetData_SingleTargetHit.Get();
            targetData.Init(lastValidHit);
            onTargetDataReadyCallback?.Invoke(targetData);
        }

        public void CancelTargeting()
        {
            if (!isTargeting) return;
            isTargeting = false;
            onCancelledCallback?.Invoke();
        }

        public void Destroy()
        {
            // This is now a MonoBehaviour, so we destroy the GameObject.
            Destroy(this.gameObject);
        }
    }
}