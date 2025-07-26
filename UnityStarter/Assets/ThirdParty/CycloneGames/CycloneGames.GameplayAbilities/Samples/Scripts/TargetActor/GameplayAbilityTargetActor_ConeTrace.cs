using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Performs an instant cone-shaped check in front of the caster to find multiple targets.
    /// This is simulated using an overlap sphere followed by an angle check.
    /// </summary>
    public class GameplayAbilityTargetActor_ConeTrace : ITargetActor
    {
        public event System.Action<TargetData> OnTargetDataReady;
        public event System.Action OnCanceled;
        
        private GameplayAbility owningAbility;
        private System.Action<TargetData> onTargetDataReadyCallback;
        private System.Action onCancelledCallback;

        private float range;
        private float coneAngle;
        private LayerMask layerMask;

        public GameplayAbilityTargetActor_ConeTrace(float range, float coneAngle, LayerMask layerMask)
        {
            this.range = range;
            this.coneAngle = coneAngle;
            this.layerMask = layerMask;
        }

        public void Configure(GameplayAbility ability, System.Action<TargetData> onTargetDataReady, System.Action onCancelled)
        {
            this.owningAbility = ability;
            this.onTargetDataReadyCallback = onTargetDataReady;
            this.onCancelledCallback = onCancelled;
        }

        public void StartTargeting()
        {
            PerformConeCheck();
        }

        private void PerformConeCheck()
        {
            var caster = owningAbility?.ActorInfo.AvatarActor as GameObject;
            if (caster == null)
            {
                onCancelledCallback?.Invoke();
                return;
            }

            var hitColliders = Physics.OverlapSphere(caster.transform.position, range, layerMask);
            var foundTargets = new List<GameObject>();

            foreach (var col in hitColliders)
            {
                if (col.gameObject == caster) continue;

                Vector3 directionToTarget = (col.transform.position - caster.transform.position).normalized;
                
                // Check if the target is within the forward-facing cone angle.
                if (Vector3.Angle(caster.transform.forward, directionToTarget) < coneAngle / 2)
                {
                    foundTargets.Add(col.gameObject);
                }
            }
            
            if (foundTargets.Count > 0)
            {
                var multiTargetData = GameplayAbilityTargetData_MultiTarget.Get();
                multiTargetData.Init(foundTargets);
                onTargetDataReadyCallback?.Invoke(multiTargetData);
            }
            else
            {
                onCancelledCallback?.Invoke();
            }
        }

        public void ConfirmTargeting() { }
        public void CancelTargeting() => onCancelledCallback?.Invoke();
        public void Destroy()
        {
            // Clear delegates
            owningAbility = null;
            onTargetDataReadyCallback = null;
            onCancelledCallback = null;
            OnTargetDataReady = null;
            OnCanceled = null;
        }
    }
}