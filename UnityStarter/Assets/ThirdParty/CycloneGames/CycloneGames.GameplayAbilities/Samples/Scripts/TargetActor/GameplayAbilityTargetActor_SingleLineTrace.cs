using System;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// A simple implementation of ITargetActor that performs a single line trace (raycast)
    /// from the caster's forward direction to find a target.
    /// </summary>
    public class GameplayAbilityTargetActor_SingleLineTrace : ITargetActor
    {
        public event Action<TargetData> OnTargetDataReady;
        public event Action OnCanceled;

        private GameplayAbility owningAbility;
        private Action<TargetData> onTargetDataReadyCallback;
        private Action onCancelledCallback;

        private float traceRange = 20f; // Max range for the trace

        public void Configure(GameplayAbility ability, Action<TargetData> onTargetDataReady, Action onCancelled)
        {
            this.owningAbility = ability;
            this.onTargetDataReadyCallback = onTargetDataReady;
            this.onCancelledCallback = onCancelled;
        }

        public void StartTargeting()
        {
            // For an instant trace, we can perform it immediately.
            PerformTrace();
        }

        private void PerformTrace()
        {
            if (owningAbility?.ActorInfo.AvatarActor is GameObject caster)
            {
                // Perform a raycast from the caster's position, forward.
                if (Physics.Raycast(caster.transform.position, caster.transform.forward, out RaycastHit hit, traceRange))
                {
                    // If we hit something, package it as TargetData.
                    var targetData = GameplayAbilityTargetData_SingleTargetHit.Get();
                    targetData.Init(hit);
                    onTargetDataReadyCallback?.Invoke(targetData);
                    return;
                }
            }

            // If trace fails or caster is invalid, we consider it a "cancel" or "no valid data".
            onCancelledCallback?.Invoke();
        }

        public void ConfirmTargeting()
        {
            // Not needed for an instant trace actor like this one.
        }

        public void CancelTargeting()
        {
            onCancelledCallback?.Invoke();
        }

        public void Destroy()
        {
            // This class is not a MonoBehaviour, so it doesn't need to be destroyed in the traditional sense.
            // We just clear delegates to prevent memory leaks.
            owningAbility = null;
            onTargetDataReadyCallback = null;
            onCancelledCallback = null;
            OnTargetDataReady = null;
            OnCanceled = null;
        }
    }
}