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
        
        private Camera m_CachedCamera;

        // Camera.main is a tagged-object search, so the reference is cached and only re-acquired when it
        // dies (scene change, camera destroyed). The acquisition lives in a property getter rather than
        // Update, which keeps the per-frame method free of the call entirely.
        private Camera TargetCamera
        {
            get
            {
                if (m_CachedCamera == null)
                    m_CachedCamera = Camera.main;

                return m_CachedCamera;
            }
        }

        void Update()
        {
            if (!isTargeting) return;

            Camera targetingCamera = TargetCamera;
            if (targetingCamera == null)
                return;

            // Trace from camera to mouse position
            Ray ray = targetingCamera.ScreenPointToRay(Input.mousePosition);
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

            var targetData = owningAbility.AbilitySystemComponent.RentTargetData<GameplayAbilityTargetData_SingleTargetHit>();
            targetData.Init(lastValidHit);
            Action<TargetData> callback = onTargetDataReadyCallback;
            if (callback == null)
            {
                targetData.Release();
                throw new InvalidOperationException("Ground targeting completed without a TargetData receiver.");
            }

            callback(targetData);
        }

        public void CancelTargeting()
        {
            if (!isTargeting) return;
            isTargeting = false;
            onCancelledCallback?.Invoke();
        }

        public void Destroy()
        {
            onTargetDataReadyCallback = null;
            onCancelledCallback = null;
            owningAbility = null;
            // This is now a MonoBehaviour, so we destroy the GameObject.
            Destroy(this.gameObject);
        }
    }
}
