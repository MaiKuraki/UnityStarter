using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a granted instance of a GameplayAbility on an AbilitySystemComponent.
    /// Holds runtime state such as level and active status.
    /// Released instances are invalidated and discarded.
    /// </summary>
    public class GameplayAbilitySpec : IGASLeasedObject
    {
        private GASRuntimeMemory memoryOwner;
        private bool leaseActive;
        private bool leaseEverAcquired;
        private ulong leaseGeneration;

        internal ulong LeaseGeneration => leaseGeneration;
        /// <summary>
        /// ASC-local handle assigned when the ability is granted.
        /// Stable across the spec's entire lifetime; reset when the runtime lease is released.
        /// This process-local handle is never a stable wire grant identity.
        /// </summary>
        public int Handle { get; private set; }

        /// <summary>
        /// The stateless definition of the ability (template).
        /// </summary>
        public GameplayAbility Ability { get; private set; }

        /// <summary>
        /// Convenience accessor for the ability's CDO.
        /// </summary>
        public GameplayAbility AbilityCDO => Ability;

        /// <summary>
        /// The live, stateful instance if instancing policy requires one.
        /// </summary>
        public GameplayAbility AbilityInstance { get; private set; }

        /// <summary>
        /// The current level of this granted ability.
        /// </summary>
        public int Level { get; internal set; }

        /// <summary>
        /// Flag indicating if this ability is currently executing.
        /// </summary>
        public bool IsActive { get; internal set; }
        internal bool IsLocallyExecuting { get; set; }
        /// <summary>Current replicated input hold state for this exact granted spec.</summary>
        public bool IsInputPressed { get; internal set; }
        internal bool ActivationCallInProgress { get; set; }
        internal bool EndCallInProgress { get; set; }

        /// <summary>
        /// Reference to the owning ASC.
        /// </summary>
        public AbilitySystemComponent Owner { get; private set; }

        /// <summary>
        /// The ActiveGameplayEffect that granted this ability (null if granted directly).
        /// Used for automatic cleanup when the granting effect is removed.
        /// </summary>
        public ActiveGameplayEffect GrantingEffect { get; internal set; }

        public GameplayAbilitySpec() { }

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
            if (leaseGeneration == ulong.MaxValue)
            {
                throw new System.InvalidOperationException(
                    "GameplayAbilitySpec lease generation is exhausted.");
            }

            leaseGeneration++;
        }

        void IGASLeasedObject.OnLeaseReleased()
        {
            Ability = null;
            AbilityInstance = null;
            Owner = null;
            GrantingEffect = null;
            Level = 0;
            IsActive = false;
            IsLocallyExecuting = false;
            IsInputPressed = false;
            ActivationCallInProgress = false;
            EndCallInProgress = false;
            Handle = 0;
        }

        internal void SetMemoryOwner(GASRuntimeMemory owner) => memoryOwner = owner;

        #region Factory

        public static GameplayAbilitySpec Create(GameplayAbility ability, AbilitySystemComponent owner, int level = 1, int replicatedHandle = 0)
        {
            if (ability == null) throw new System.ArgumentNullException(nameof(ability));
            if (owner == null) throw new System.ArgumentNullException(nameof(owner));
            if (!ability.IsConfigurationInitialized) throw new System.InvalidOperationException("Cannot create a spec for an uninitialized GameplayAbility definition.");
            if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.NonInstanced)
            {
                throw new System.InvalidOperationException("Unity Runtime GameplayAbilitySpec does not support NonInstanced abilities.");
            }
            if (level <= 0 || level > GASRuntimeDataContract.MaxGameplayLevel)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(level),
                    level,
                    $"Ability level must be between 1 and {GASRuntimeDataContract.MaxGameplayLevel}.");
            }
            if (replicatedHandle < 0) throw new System.ArgumentOutOfRangeException(nameof(replicatedHandle), replicatedHandle, "Replicated handles cannot be negative.");

            var spec = owner.RuntimeContext.Memory.AcquireAbilitySpec();
            try
            {
                spec.Ability = ability;
                spec.Level = level;
                spec.IsActive = false;
                spec.IsLocallyExecuting = false;
                spec.IsInputPressed = false;
                spec.AbilityInstance = null;
                spec.Owner = owner;
                spec.Handle = replicatedHandle > 0
                    ? replicatedHandle
                    : owner.AllocateAbilitySpecHandle();
                owner.ObserveAbilitySpecHandle(spec.Handle);
                return spec;
            }
            catch
            {
                spec.ReleaseRuntimeLease();
                throw;
            }
        }

        internal void AssignHandleFromContainer(int replicatedHandle)
        {
            if (replicatedHandle <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(replicatedHandle), replicatedHandle, "Ability spec handles must be positive.");
            }

            Handle = replicatedHandle;
        }

        #endregion

        /// <summary>
        /// Gets the primary object to execute logic on.
        /// </summary>
        public GameplayAbility GetPrimaryInstance() => AbilityInstance ?? AbilityCDO;

        /// <summary>
        /// Creates a stateful instance if required by instancing policy.
        /// </summary>
        internal void CreateInstance()
        {
            if (Ability.InstancingPolicy != EGameplayAbilityInstancingPolicy.NonInstanced && AbilityInstance == null)
            {
                GameplayAbility instance = Owner.RuntimeContext.Memory.AcquireAbility(Ability);
                AbilityInstance = instance;
                try
                {
                    instance.OnGiveAbility(new GameplayAbilityActorInfo(Owner.OwnerActor, Owner.AvatarActor), this);
                }
                catch
                {
                    AbilityInstance = null;
                    Owner.RuntimeContext.Memory.ReleaseAbility(instance);
                    throw;
                }
            }
        }

        /// <summary>
        /// Clears the stateful instance.
        /// </summary>
        internal void ClearInstance()
        {
            if (AbilityInstance != null)
            {
                if (IsLocallyExecuting)
                {
                    AbilityInstance.CancelAbility();
                }

                if (Ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerExecution)
                {
                    Owner.RuntimeContext.Memory.ReleaseAbility(AbilityInstance);
                }
                AbilityInstance = null;
            }
        }

        /// <summary>
        /// Called when the ability is being removed from the ASC.
        /// </summary>
        internal void OnRemoveSpec()
        {
            var abilityToRemove = GetPrimaryInstance();
            System.Exception failure = null;
            try
            {
                abilityToRemove?.OnRemoveAbility();
            }
            catch (System.Exception exception)
            {
                failure = exception;
            }

            try
            {
                // Extension callbacks are not trusted to call base. The non-virtual end path guarantees
                // tasks, subscriptions, prediction state, and ASC activation bookkeeping are released.
                if (IsLocallyExecuting)
                {
                    abilityToRemove?.EndAbility();
                }
                else
                {
                    IsActive = false;
                    abilityToRemove?.CleanupTasksForSpecRemoval();
                }
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
            finally
            {
                if (AbilityInstance != null && Ability.InstancingPolicy != EGameplayAbilityInstancingPolicy.NonInstanced)
                {
                    var instanceToReturn = AbilityInstance;
                    AbilityInstance = null;
                    Owner.RuntimeContext.Memory.ReleaseAbility(instanceToReturn);
                }
            }

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        internal void ReleaseRuntimeLease()
        {
            memoryOwner?.ReleaseAbilitySpec(this);
        }
    }
}
