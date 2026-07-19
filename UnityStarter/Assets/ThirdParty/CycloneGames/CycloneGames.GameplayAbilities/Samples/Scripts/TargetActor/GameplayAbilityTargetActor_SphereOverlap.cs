using System;
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
        private readonly Collider[] overlapBuffer;
        private readonly List<GameObject> foundTargets;
        private readonly HashSet<int> foundTargetIds;

        public GameplayAbilityTargetActor_SphereOverlap(
            LayerMask layerMask,
            TargetingQuery query,
            float radius,
            int maxResults = 64)
            : base(layerMask, query)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Targeting radius must be finite and positive.");
            }

            if (maxResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "Target result capacity must be positive.");
            }

            this.radius = radius;
            overlapBuffer = new Collider[maxResults];
            foundTargets = new List<GameObject>(maxResults);
            foundTargetIds = new HashSet<int>(maxResults);
        }

        protected override void PerformTrace()
        {
            foundTargets.Clear();
            foundTargetIds.Clear();
            int hitCount = Physics.OverlapSphereNonAlloc(
                CasterGameObject.transform.position,
                radius,
                overlapBuffer,
                TraceLayerMask);

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = overlapBuffer[i];
                overlapBuffer[i] = null;
                // Use the centralized IsValidTarget method from the base class for all filtering.
                if (col != null &&
                    IsValidTarget(col.gameObject) &&
                    foundTargetIds.Add(col.gameObject.GetInstanceID()))
                {
                    foundTargets.Add(col.gameObject);
                }
            }

            if (foundTargets.Count > 0)
            {
                var multiTargetData = OwningAbility.AbilitySystemComponent.RentTargetData<GameplayAbilityTargetData_MultiTarget>();
                multiTargetData.Init(foundTargets);
                BroadcastReady(multiTargetData);
            }
            else
            {
                BroadcastCancelled();
            }
        }

        public override void Destroy()
        {
            foundTargets.Clear();
            foundTargetIds.Clear();
            Array.Clear(overlapBuffer, 0, overlapBuffer.Length);
            base.Destroy();
        }
    }
}
