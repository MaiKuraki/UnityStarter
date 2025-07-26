using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Performs an instant sphere overlap check, using the configurable TargetingQuery for filtering.
    /// </summary>
    public class GameplayAbilityTargetActor_SphereOverlap : GameplayAbilityTargetActor_TraceBase
    {
        private readonly float radius;

        public GameplayAbilityTargetActor_SphereOverlap(LayerMask layerMask, TargetingQuery query, float radius)
            : base(layerMask, query)
        {
            this.radius = radius;
        }

        protected override void PerformTrace()
        {
            var hitColliders = Physics.OverlapSphere(CasterGameObject.transform.position, radius, TraceLayerMask);
            var foundTargets = new List<GameObject>();

            foreach (var col in hitColliders)
            {
                // Use the centralized IsValidTarget method from the base class for all filtering.
                if (IsValidTarget(col.gameObject))
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