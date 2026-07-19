using System;
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
        private readonly float range;
        private readonly float minimumDot;
        private readonly Collider[] overlapBuffer;
        private readonly List<GameObject> foundTargets;
        private readonly HashSet<int> foundTargetIds;

        public GameplayAbilityTargetActor_ConeTrace(
            LayerMask layerMask,
            TargetingQuery query,
            float range,
            float coneAngle,
            int maxResults = 64)
            : base(layerMask, query)
        {
            if (float.IsNaN(range) || float.IsInfinity(range) || range <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "Cone range must be finite and positive.");
            }

            if (float.IsNaN(coneAngle) || float.IsInfinity(coneAngle) || coneAngle <= 0f || coneAngle > 360f)
            {
                throw new ArgumentOutOfRangeException(nameof(coneAngle), coneAngle, "Cone angle must be in (0, 360].");
            }

            if (maxResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "Target result capacity must be positive.");
            }

            this.range = range;
            minimumDot = Mathf.Cos(coneAngle * 0.5f * Mathf.Deg2Rad);
            overlapBuffer = new Collider[maxResults];
            foundTargets = new List<GameObject>(maxResults);
            foundTargetIds = new HashSet<int>(maxResults);
        }

        public override void StartTargeting()
        {
            PerformTrace();
        }

        protected override void PerformTrace()
        {
            var caster = OwningAbility?.ActorInfo.AvatarGameObject;
            if (caster == null)
            {
                BroadcastCancelled();
                return;
            }

            foundTargets.Clear();
            foundTargetIds.Clear();
            int hitCount = Physics.OverlapSphereNonAlloc(
                caster.transform.position,
                range,
                overlapBuffer,
                TraceLayerMask);

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = overlapBuffer[i];
                overlapBuffer[i] = null;
                if (col == null || !IsValidTarget(col.gameObject)) continue;

                Vector3 offset = col.transform.position - caster.transform.position;
                float sqrDistance = offset.sqrMagnitude;
                if (sqrDistance <= 0f) continue;

                float dot = Vector3.Dot(caster.transform.forward, offset / Mathf.Sqrt(sqrDistance));
                if (dot >= minimumDot && foundTargetIds.Add(col.gameObject.GetInstanceID()))
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
