using System;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Context object carrying metadata about an effect's application.
    /// Released contexts are invalidated and discarded.
    /// </summary>
    public class GameplayEffectContext : IDisposable, IGASLeasedObject
    {
        private enum ContextOwnership : byte
        {
            None,
            Caller,
            Spec
        }

        private GASRuntimeMemory memoryOwner;
        private bool leaseActive;
        private bool leaseEverAcquired;
        private ContextOwnership ownership = ContextOwnership.Caller;
        private GameplayEffectSpec owningSpec;
        private GASPredictionKey predictionKey;

        public AbilitySystemComponent Instigator { get; private set; }
        public GameplayAbility AbilityInstance { get; private set; }
        public GASPredictionKey PredictionKey
        {
            get => predictionKey;
            set
            {
                AssertMemoryOwnerThread();
                EnsureCallerOwned();
                predictionKey = value;
            }
        }

        public GameplayEffectContext() { }

        bool IGASLeasedObject.TryAcquireLease()
        {
            if (leaseActive || leaseEverAcquired) return false;
            leaseEverAcquired = true;
            leaseActive = true;
            return true;
        }

        bool IGASLeasedObject.TryReleaseLease()
        {
            if (!leaseActive) return false;
            leaseActive = false;
            return true;
        }

        void IGASLeasedObject.OnLeaseAcquired()
        {
            ResetState();
            owningSpec = null;
            ownership = ContextOwnership.Caller;
        }

        void IGASLeasedObject.OnLeaseReleased()
        {
            ResetState();
            owningSpec = null;
            ownership = ContextOwnership.None;
        }

        internal void SetMemoryOwner(GASRuntimeMemory owner) => memoryOwner = owner;
        internal GASRuntimeMemory MemoryOwner => memoryOwner;

        public void AddInstigator(AbilitySystemComponent instigator, GameplayAbility abilityInstance)
        {
            AssertMemoryOwnerThread();
            EnsureCallerOwned();
            Instigator = instigator;
            AbilityInstance = abilityInstance;
        }

        public void Reset()
        {
            AssertMemoryOwnerThread();
            EnsureCallerOwned();
            ResetState();
        }

        private void ResetState()
        {
            Instigator = null;
            AbilityInstance = null;
            predictionKey = default;
            ResetCustomState();
        }

        /// <summary>
        /// Resets data introduced by a derived context when the lease is acquired or released.
        /// </summary>
        protected virtual void ResetCustomState() { }

        internal void AttachToSpec(GameplayEffectSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            AssertMemoryOwnerThread();
            EnsureCallerOwned();
            owningSpec = spec;
            ownership = ContextOwnership.Spec;
        }

        internal void SetPredictionKeyFromSpec(GameplayEffectSpec spec, GASPredictionKey value)
        {
            AssertMemoryOwnerThread();
            EnsureOwnedBy(spec);

            predictionKey = value;
        }

        internal GameplayAbility ConsumeAbilityInstance(GameplayEffectSpec spec)
        {
            AssertMemoryOwnerThread();
            EnsureOwnedBy(spec);
            GameplayAbility ability = AbilityInstance;
            AbilityInstance = null;
            return ability;
        }

        internal void ReturnFromSpec(GameplayEffectSpec spec)
        {
            AssertMemoryOwnerThread();
            EnsureOwnedBy(spec);

            if (memoryOwner != null)
            {
                memoryOwner.ReleaseEffectContext(this);
            }
            else
            {
                owningSpec = null;
                ownership = ContextOwnership.None;
                ResetState();
            }
        }

        /// <summary>
        /// Releases this context lease.
        /// </summary>
        public void Dispose()
        {
            AssertMemoryOwnerThread();
            if (ownership == ContextOwnership.Spec)
            {
                throw new InvalidOperationException("GameplayEffectContext ownership has transferred to a GameplayEffectSpec and cannot be disposed by the caller.");
            }

            if (ownership == ContextOwnership.None)
            {
                return;
            }

            if (memoryOwner != null)
            {
                memoryOwner.ReleaseEffectContext(this);
            }
            else
            {
                owningSpec = null;
                ownership = ContextOwnership.None;
                ResetState();
            }
        }

        internal void ReleaseRuntimeLease() => Dispose();

        private void EnsureCallerOwned()
        {
            if (ownership != ContextOwnership.Caller)
            {
                throw new InvalidOperationException("GameplayEffectContext mutation is only valid before it is attached to a GameplayEffectSpec.");
            }
        }

        private void EnsureOwnedBy(GameplayEffectSpec spec)
        {
            if (ownership != ContextOwnership.Spec || !ReferenceEquals(owningSpec, spec))
            {
                throw new InvalidOperationException("GameplayEffectContext can only be changed or released by its owning GameplayEffectSpec.");
            }
        }

        private void AssertMemoryOwnerThread()
        {
            memoryOwner?.AssertOwnerThread();
        }
    }
}
