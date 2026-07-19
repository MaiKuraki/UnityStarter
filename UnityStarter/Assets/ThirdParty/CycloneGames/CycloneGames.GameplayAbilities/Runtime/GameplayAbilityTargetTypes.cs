using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum TargetDataValidationResult
    {
        Invalid = 0,
        Valid,
        MissingData,
        PredictionKeyMismatch,
        AbilitySpecMismatch,
        SourceMismatch,
        TooOld,
        FutureFrame,
        InvalidTarget,
        TargetOutOfRange
    }

    /// <summary>
    /// Abstract base class for all targeting data structures passed from TargetActors to GameplayAbilities.
    /// </summary>
    public abstract class TargetData
    {
        private GASRuntimeMemory memoryOwner;
        private bool leaseActive = true;
        private int maxTargets = GASRuntimeLimits.Default.MaxTargetsPerTargetData;

        private GASPredictionKey predictionKey;
        private int abilitySpecHandle;
        private AbilitySystemComponent source;
        private long createdFrame;

        public GASPredictionKey PredictionKey { get { EnsureLeaseIsActive(); return predictionKey; } }
        public int AbilitySpecHandle { get { EnsureLeaseIsActive(); return abilitySpecHandle; } }
        public AbilitySystemComponent Source { get { EnsureLeaseIsActive(); return source; } }
        public long CreatedFrame { get { EnsureLeaseIsActive(); return createdFrame; } }
        protected int MaxTargets => maxTargets;

        protected void EnsureLeaseIsActive()
        {
            if (!leaseActive)
            {
                throw new System.ObjectDisposedException(GetType().FullName, "TargetData has already been released.");
            }
        }

        internal void MarkLeaseAcquired(GASRuntimeMemory owner, int targetLimit)
        {
            if (!leaseActive || memoryOwner != null)
            {
                throw new System.InvalidOperationException($"TargetData '{GetType().FullName}' cannot receive another runtime lease.");
            }

            if (targetLimit <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(targetLimit), targetLimit, "Target limit must be greater than zero.");
            }

            memoryOwner = owner ?? throw new System.ArgumentNullException(nameof(owner));
            maxTargets = targetLimit;
            leaseActive = true;
        }

        internal bool TryReleaseLease()
        {
            if (!leaseActive)
            {
                return false;
            }

            leaseActive = false;
            return true;
        }

        public void StampPrediction(GameplayAbility ability, GASPredictionKey predictionKey)
        {
            EnsureLeaseIsActive();
            this.predictionKey = predictionKey;
            abilitySpecHandle = ability?.Spec?.Handle ?? 0;
            source = ability?.AbilitySystemComponent;
            createdFrame = source?.SimulationFrame ?? 0L;
        }

        protected void ClearPredictionStamp()
        {
            predictionKey = default;
            abilitySpecHandle = 0;
            source = null;
            createdFrame = 0;
        }

        public void Release()
        {
            if (memoryOwner != null)
            {
                memoryOwner.ReleaseTargetData(this);
            }
            else if (TryReleaseLease())
            {
                ResetRuntimeState();
            }
        }

        internal virtual void ResetRuntimeState()
        {
            ClearPredictionStamp();
            maxTargets = GASRuntimeLimits.Default.MaxTargetsPerTargetData;
        }
    }

    /// <summary>
    /// A common TargetData base class that provides a list of actor targets.
    /// This is the core class for any targeting that results in one or more GameObjects.
    /// Abilities can safely cast to this type to get a list of actors without needing to know
    /// the specific targeting method used (e.g., raycast, sphere overlap).
    /// </summary>
    public class GameplayAbilityTargetData_ActorArray : TargetData
    {
        private List<GameObject> actors;

        public int ActorCount
        {
            get
            {
                EnsureLeaseIsActive();
                return actors?.Count ?? 0;
            }
        }

        public GameObject GetActor(int index)
        {
            EnsureLeaseIsActive();
            List<GameObject> currentActors = actors;
            if (currentActors == null || (uint)index >= (uint)currentActors.Count)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }

            return currentActors[index];
        }

        /// <summary>
        /// A convenience property to get the first actor, or null if the list is empty.
        /// Useful for single-target scenarios.
        /// </summary>
        public GameObject FirstActor
        {
            get
            {
                EnsureLeaseIsActive();
                List<GameObject> currentActors = actors;
                return currentActors != null && currentActors.Count > 0 ? currentActors[0] : null;
            }
        }

        public virtual void AddTarget(GameObject target)
        {
            EnsureLeaseIsActive();
            if (target != null)
            {
                List<GameObject> currentActors = actors;
                if ((currentActors?.Count ?? 0) >= MaxTargets)
                {
                    throw new System.InvalidOperationException($"TargetData exceeded its target limit of {MaxTargets}.");
                }

                if (currentActors == null)
                {
                    currentActors = new List<GameObject>();
                    actors = currentActors;
                }
                currentActors.Add(target);
            }
        }

        public virtual void AddTargets(IReadOnlyList<GameObject> targets)
        {
            EnsureLeaseIsActive();
            if (targets != null)
            {
                List<GameObject> currentActors = actors;
                if (targets.Count > MaxTargets - (currentActors?.Count ?? 0))
                {
                    throw new System.InvalidOperationException($"TargetData exceeded its target limit of {MaxTargets}.");
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    GameObject target = targets[i];
                    if (target != null)
                    {
                        if (currentActors == null)
                        {
                            currentActors = new List<GameObject>();
                            actors = currentActors;
                        }
                        currentActors.Add(target);
                    }
                }
            }
        }

        public virtual void Clear()
        {
            EnsureLeaseIsActive();
            actors?.Clear();
        }

        internal override void ResetRuntimeState()
        {
            actors?.Clear();
            base.ResetRuntimeState();
        }
    }

    /// <summary>
    /// This class remains a concrete implementation for a single physics-based hit result,
    /// but now also conforms to the generic actor provider pattern.
    /// </summary>
    public class GameplayAbilityTargetData_SingleTargetHit : GameplayAbilityTargetData_ActorArray
    {
        /// <summary>
        /// The specific, engine-dependent physics hit result.
        /// An ability can still access this if it needs detailed collision info (e.g., impact normal for ricochets).
        /// </summary>
        private RaycastHit hitResult;
        public RaycastHit HitResult { get { EnsureLeaseIsActive(); return hitResult; } }

        public void Init(RaycastHit hit)
        {
            EnsureLeaseIsActive();
            hitResult = hit;

            Clear();
            if (hit.collider != null)
            {
                AddTarget(hit.collider.gameObject);
            }
        }

        public Vector3 HitPoint { get { EnsureLeaseIsActive(); return hitResult.point; } }
        public Vector3 HitNormal { get { EnsureLeaseIsActive(); return hitResult.normal; } }
        public float HitDistance { get { EnsureLeaseIsActive(); return hitResult.distance; } }

        internal override void ResetRuntimeState()
        {
            hitResult = default;
            base.ResetRuntimeState();
        }
    }

    /// <summary>
    /// This class defined as a data container for multiple actors
    /// found via non-physics or bulk-physics checks (like sphere overlap).
    /// </summary>
    public class GameplayAbilityTargetData_MultiTarget : GameplayAbilityTargetData_ActorArray
    {
        public void Init(List<GameObject> targets)
        {
            Clear();
            AddTargets(targets);
        }

        internal override void ResetRuntimeState()
        {
            base.ResetRuntimeState();
        }
    }
}
