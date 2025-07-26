using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Performs an instant cone-shaped check in front of the caster to find multiple targets.
    /// This is simulated using an overlap sphere followed by an angle check.
    /// </summary>
    public class GameplayAbilityTargetActor_ConeTrace : GameplayAbilityTargetActor_TraceBase
    {
        private GameplayAbility owningAbility;

        private float range;
        private float coneAngle;

        public GameplayAbilityTargetActor_ConeTrace(LayerMask layerMask, TargetingQuery query, float range, float coneAngle)
            : base(layerMask, query)
        {
            this.range = range;
            this.coneAngle = coneAngle;
        }

        public override void Configure(GameplayAbility ability, System.Action<TargetData> onTargetDataReady, System.Action onCancelled)
        {
            base.Configure(ability, onTargetDataReady, onCancelled);

            this.owningAbility = ability;
        }

        public override void StartTargeting()
        {
            PerformTrace();
        }

        public override void Destroy()
        {
            base.Destroy();
            owningAbility = null;
        }

        protected override void PerformTrace()
        {
            var caster = owningAbility?.ActorInfo.AvatarActor as GameObject;
            if (caster == null)
            {
                BroadcastCancelled();
                return;
            }

            var hitColliders = Physics.OverlapSphere(caster.transform.position, range, TraceLayerMask);
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
                BroadcastReady(multiTargetData);
            }
            else
            {
                BroadcastCancelled();
            }
        }
    }
}