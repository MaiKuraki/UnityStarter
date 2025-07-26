using System;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// A simple implementation of ITargetActor that performs a single line trace (raycast)
    /// from the caster's forward direction to find a target.
    /// </summary>
    public class GameplayAbilityTargetActor_SingleLineTrace : GameplayAbilityTargetActor_TraceBase
    {
        private GameplayAbility owningAbility;

        private float traceRange = 20f; // Max range for the trace

        public GameplayAbilityTargetActor_SingleLineTrace(LayerMask layerMask, TargetingQuery query)
            : base(layerMask, query)
        {
            
        }

        public override void Configure(GameplayAbility ability, Action<TargetData> onTargetDataReady, Action onCancelled)
        {
            base.Configure(ability, onTargetDataReady, onCancelled);

            this.owningAbility = ability;
        }

        public override void StartTargeting()
        {
            PerformTrace();
        }

        protected override void PerformTrace()
        {
            if (owningAbility?.ActorInfo.AvatarActor is GameObject caster)
            {
                // Perform a raycast from the caster's position, forward.
                if (Physics.Raycast(caster.transform.position, caster.transform.forward, out RaycastHit hit, traceRange))
                {
                    var targetData = GameplayAbilityTargetData_SingleTargetHit.Get();
                    targetData.Init(hit);
                    BroadcastReady(targetData);
                    return;
                }
            }

            // If trace fails or caster is invalid, we consider it a "cancel" or "no valid data".
            BroadcastCancelled();
        }

        public override void Destroy()
        {
            base.Destroy();
            
            owningAbility = null;
        }
    }
}