using System;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public abstract class GameplayAbilityTargetActor_TraceBase : ITargetActor
    {
        protected LayerMask TraceLayerMask;
        protected TargetingQuery Query;
        protected GameObject CasterGameObject;
        public event Action<TargetData> OnTargetDataReady;
        public event Action OnCanceled;

        public GameplayAbilityTargetActor_TraceBase(LayerMask layerMask, TargetingQuery query)
        {
            this.TraceLayerMask = layerMask;
            this.Query = query;
        }

        public virtual void Configure(GameplayAbility ability, Action<TargetData> onTargetDataReady, Action onCancelled)
        {
            this.OnTargetDataReady = onTargetDataReady;
            this.OnCanceled = onCancelled;

            if (ability.ActorInfo.AvatarActor is GameObject casterGO)
            {
                this.CasterGameObject = casterGO;
            }
        }

        public virtual void StartTargeting()
        {
            if (CasterGameObject == null)
            {
                BroadcastCancelled();
                return;
            }

            PerformTrace();
        }

        // Subclasses must implement this to perform their specific trace logic (e.g., OverlapSphere, Raycast).
        protected abstract void PerformTrace();

        /// <summary>
        /// A robust, centralized method to check if a potential target is valid based on the query settings.
        /// </summary>
        protected virtual bool IsValidTarget(GameObject targetObject)
        {
            if (targetObject == null) return false;

            // Ignore the caster if specified.
            if (Query.IgnoreCaster && targetObject == CasterGameObject)
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
        public virtual void ConfirmTargeting() { }
        public virtual void CancelTargeting()
        {
            BroadcastCancelled();
        }
        public virtual void Destroy()
        {
            OnTargetDataReady = null;
            OnCanceled = null;
            CasterGameObject = null;
        }

        /// <summary>
        /// A protected method for subclasses to safely invoke the OnTargetDataReady event.
        /// This is the correct way to trigger an event from a derived class.
        /// </summary>
        protected void BroadcastReady(TargetData data)
        {
            OnTargetDataReady?.Invoke(data);
        }

        /// <summary>
        /// A protected method for subclasses to safely invoke the OnCanceled event.
        /// </summary>
        protected void BroadcastCancelled()
        {
            OnCanceled?.Invoke();
        }
    }
}