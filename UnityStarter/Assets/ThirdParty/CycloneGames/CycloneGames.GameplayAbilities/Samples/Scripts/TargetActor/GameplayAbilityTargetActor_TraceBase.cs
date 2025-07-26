using System;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public abstract class GameplayAbilityTargetActor_TraceBase : ITargetActor
    {
        protected TargetingQuery Query;
        protected Action<TargetData> onTargetDataReadyCallback;
        protected Action onCancelledCallback;
        protected Character CasterCharacter;

        public event Action<TargetData> OnTargetDataReady;
        public event Action OnCanceled;

        public virtual void Configure(GameplayAbility ability, Action<TargetData> onTargetDataReady, Action onCancelled)
        {
            this.onTargetDataReadyCallback = onTargetDataReady;
            this.onCancelledCallback = onCancelled;

            if (ability.ActorInfo.AvatarActor is GameObject casterGO)
            {
                this.CasterCharacter = casterGO.GetComponent<Character>();
            }
        }
        
        public void StartTargeting()
        {
            if (CasterCharacter == null)
            {
                onCancelledCallback?.Invoke();
                return;
            }
            PerformTrace();
        }

        // Subclasses must implement this to perform their specific trace logic (e.g., OverlapSphere, Raycast).
        protected abstract void PerformTrace();

        /// <summary>
        /// A robust, centralized method to check if a potential target is valid based on the query settings.
        /// </summary>
        protected bool IsValidTarget(GameObject targetObject)
        {
            if (targetObject == null) return false;
            
            // Ignore the caster if specified.
            if (Query.IgnoreCaster && targetObject == CasterCharacter.gameObject)
            {
                return false;
            }

            var targetCharacter = targetObject.GetComponent<Character>();
            if (targetCharacter == null) return false; // Must be a valid character

            // Faction Tag check.
            bool hasRequired = (Query.RequiredTags == null || Query.RequiredTags.IsEmpty || targetCharacter.FactionTags.HasAll(Query.RequiredTags));
            bool hasForbidden = (Query.ForbiddenTags != null && !Query.ForbiddenTags.IsEmpty && targetCharacter.FactionTags.HasAny(Query.ForbiddenTags));

            return hasRequired && !hasForbidden;
        }

        // Default implementations for non-instant targeting.
        public void ConfirmTargeting() { }
        public void CancelTargeting() => onCancelledCallback?.Invoke();

        public virtual void Destroy()
        {
            onTargetDataReadyCallback = null;
            onCancelledCallback = null;
            OnTargetDataReady = null;
            OnCanceled = null;
            CasterCharacter = null;
        }
    }
}