using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayAbilities.Core;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Determines how an ability is instantiated when activated.
    /// </summary>
    public enum EGameplayAbilityInstancingPolicy
    {
        /// <summary>
        /// The ability runs on its Class Default Object (CDO). No new instance is created.
        /// Best for performance-critical, simple, stateless abilities (e.g., a basic jump).
        /// Cannot maintain state (member variables) across frames or activations. Cannot use latent AbilityTasks.
        /// </summary>
        NonInstanced,
        /// <summary>
        /// A single instance of the ability is created per actor (ASC) when the ability is granted.
        /// This instance is reused for every activation. Can maintain state across activations.
        /// This is the most common and versatile policy.
        /// </summary>
        InstancedPerActor,
        /// <summary>
        /// A new instance of the ability is created every time it is activated and destroyed when it ends.
        /// Guarantees a clean state on every execution but has higher performance overhead due to object creation/destruction.
        /// </summary>
        InstancedPerExecution
    }

    /// <summary>
    /// Defines whether an ability executes locally or only on the simulation authority.
    /// </summary>
    public enum EAbilityExecutionPolicy
    {
        /// <summary>
        /// Unconfigured definitions fail closed during initialization.
        /// </summary>
        Invalid = 0,
        /// <summary>
        /// The ability executes in the current runtime without requiring authority ownership.
        /// This is appropriate for offline gameplay and local presentation-only behavior.
        /// </summary>
        LocalOnly = 1,
        /// <summary>
        /// The ability executes only in a runtime context that owns simulation authority.
        /// A remote request must pass through a project-owned authenticated endpoint before invoking it.
        /// </summary>
        AuthorityOnly = 2,
        /// <summary>
        /// The owning client may execute an explicitly opened prediction window while simulation
        /// authority independently validates and executes the same command.
        /// </summary>
        LocalPredicted = 3
    }

    /// <summary>
    /// A container for references to the core actors involved in an ability's execution.
    /// </summary>
    public struct GameplayAbilityActorInfo
    {
        /// <summary>
        /// The logical owner of the AbilitySystemComponent, often a PlayerState or the character itself.
        /// This actor is responsible for the lifetime of the ASC.
        /// </summary>
        public readonly object OwnerActor;
        public readonly UnityEngine.Object OwnerUnityObject;
        /// <summary>
        /// The physical representation of the owner in the game world, typically a Character or Pawn.
        /// This is the actor that performs animations, has a transform, etc.
        /// </summary>
        public readonly object AvatarActor;
        public readonly GameObject AvatarGameObject;

        public GameplayAbilityActorInfo(object owner, object avatar)
        {
            OwnerActor = owner;
            OwnerUnityObject = owner as UnityEngine.Object;
            AvatarActor = avatar;
            AvatarGameObject = ResolveAvatarGameObject(avatar);
        }

        private static GameObject ResolveAvatarGameObject(object avatar)
        {
            if (avatar is GameObject gameObject)
            {
                return gameObject;
            }

            if (avatar is Component component)
            {
                return component.gameObject;
            }

            return null;
        }
    }

    /// <summary>
    /// Contains transient information specific to a single activation of an ability.
    /// </summary>
    public struct GameplayAbilityActivationInfo
    {
        /// <summary>
        /// ASC-scoped key that associates provisional mutations with one local commit or rollback boundary.
        /// </summary>
        public GASPredictionKey PredictionKey { get; set; }
    }

    /// <summary>
    /// The base class for all gameplay abilities. It defines the logic and properties of a single skill or action an actor can perform.
    /// This class is designed to be subclassed to implement specific abilities.
    /// </summary>
    public abstract class GameplayAbility : IGASAbilityDefinition
    {
        public const int MaxNameLength = 256;
        public const int MaxTriggerCount = 64;
        public const int MaxAggregateTagCount = 256;

        private GameplayAbility runtimeDefinition;
        private bool configurationInitialized;
        private bool runtimeLeaseActive;
        private bool runtimeLeaseEverAcquired;
        #region Configuration Properties

        /// <summary>
        /// The display name of the ability, primarily used for debugging and logging.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Defines how this ability is instantiated upon activation. See <see cref="EGameplayAbilityInstancingPolicy"/>.
        /// </summary>
        public EGameplayAbilityInstancingPolicy InstancingPolicy { get; private set; }

        /// <summary>
        /// Defines whether the ability runs locally or requires simulation authority.
        /// See <see cref="EAbilityExecutionPolicy"/>.
        /// </summary>
        public EAbilityExecutionPolicy ExecutionPolicy { get; private set; }

        /// <summary>
        /// The GameplayEffect that defines the resource cost (e.g., mana, stamina) required to activate this ability.
        /// This effect is checked before activation and applied upon committing the ability.
        /// </summary>
        public GameplayEffect CostEffectDefinition { get; private set; }

        /// <summary>
        /// The GameplayEffect that puts the ability on cooldown. This effect typically grants a specific cooldown tag to the owner.
        /// The presence of this tag is checked before activation.
        /// </summary>
        public GameplayEffect CooldownEffectDefinition { get; private set; }

        /// <summary>
        /// Tags that describe the ability itself (e.g., "Ability.Damage.Fire", "Ability.Movement").
        /// These are used for identification and can be queried by other systems.
        /// </summary>
        public GameplayDefinitionTagSet AbilityTags { get; private set; }

        /// <summary>
        /// This ability is blocked from activating if the owner has ANY of these tags.
        /// </summary>
        public GameplayDefinitionTagSet ActivationBlockedTags { get; private set; }

        /// <summary>
        /// The owner must have ALL of these tags for the ability to be activatable.
        /// </summary>
        public GameplayDefinitionTagSet ActivationRequiredTags { get; private set; }

        /// <summary>
        /// When this ability is activated, it will cancel any other active abilities that have ANY of these tags.
        /// </summary>
        public GameplayDefinitionTagSet CancelAbilitiesWithTag { get; private set; }

        /// <summary>
        /// While this ability is active, other abilities that have ANY of these tags are blocked from activating.
        /// </summary>
        public GameplayDefinitionTagSet BlockAbilitiesWithTag { get; private set; }

        /// <summary>
        /// Tags that are granted to the owner while this ability is active.
        /// These tags are added when the ability activates and removed when it ends.
        /// </summary>
        public GameplayDefinitionTagSet ActivationOwnedTags { get; private set; }

        /// <summary>
        /// If true, this ability is automatically activated when granted and deactivated when removed.
        /// UE5: bActivateAbilityOnGranted. Used for passive abilities (auras, buffs).
        /// </summary>
        public bool ActivateAbilityOnGranted { get; private set; }

        /// <summary>
        /// The source (caster) must have ALL of these tags for the ability to activate.
        /// UE5: SourceRequiredTags on FGameplayTagRequirements.
        /// </summary>
        public GameplayDefinitionTagSet SourceRequiredTags { get; private set; }

        /// <summary>
        /// The ability is blocked if the source (caster) has ANY of these tags.
        /// UE5: SourceBlockedTags on FGameplayTagRequirements.
        /// </summary>
        public GameplayDefinitionTagSet SourceBlockedTags { get; private set; }

        /// <summary>
        /// The target must have ALL of these tags for the ability to activate on that target.
        /// UE5: TargetRequiredTags on FGameplayTagRequirements.
        /// Checked in CanApplyToTarget().
        /// </summary>
        public GameplayDefinitionTagSet TargetRequiredTags { get; private set; }

        /// <summary>
        /// The ability is blocked if the target has ANY of these tags.
        /// UE5: TargetBlockedTags on FGameplayTagRequirements.
        /// Checked in CanApplyToTarget().
        /// </summary>
        public GameplayDefinitionTagSet TargetBlockedTags { get; private set; }

        /// <summary>
        /// Defines automatic triggers for this ability (event received, tag added/removed).
        /// UE5: TArray<FAbilityTriggerData> AbilityTriggers.
        /// </summary>
        public IReadOnlyList<AbilityTriggerData> AbilityTriggers { get; private set; }

        internal ReadOnlyGameplayTagContainer AbilityTagsSnapshot { get; private set; }
        internal ReadOnlyGameplayTagContainer ActivationBlockedTagsSnapshot { get; private set; }
        internal ReadOnlyGameplayTagContainer ActivationRequiredTagsSnapshot { get; private set; }
        internal ReadOnlyGameplayTagContainer SourceRequiredTagsSnapshot { get; private set; }
        internal ReadOnlyGameplayTagContainer SourceBlockedTagsSnapshot { get; private set; }
        internal ReadOnlyGameplayTagContainer TargetRequiredTagsSnapshot { get; private set; }
        internal ReadOnlyGameplayTagContainer TargetBlockedTagsSnapshot { get; private set; }
        internal ReadOnlyGameplayTagContainer CooldownGrantedTagsSnapshot { get; private set; }
        internal bool IsConfigurationInitialized => configurationInitialized;
        internal GameplayAbility StableDefinition => runtimeDefinition ?? this;

        #endregion

        #region Runtime Properties

        /// <summary>
        /// A direct reference to the owning AbilitySystemComponent.
        /// </summary>
        public AbilitySystemComponent AbilitySystemComponent { get; private set; }

        /// <summary>
        /// A reference to the GameplayAbilitySpec that represents this granted ability on the ASC.
        /// The Spec holds runtime state like level and active status.
        /// </summary>
        public GameplayAbilitySpec Spec { get; private set; }

        /// <summary>
        /// Cached actor information for this ability.
        /// </summary>
        public GameplayAbilityActorInfo ActorInfo { get; private set; }

        public GameplayAbilityActivationInfo CurrentActivationInfo { get; private set; }

        #endregion

        private List<AbilityTask> activeTasks;
        private Dictionary<AbilityTask, int> activeTaskIndexByTask;
        private List<IAbilityTaskTick> tickableTasks;
        private Dictionary<IAbilityTaskTick, int> tickableTaskIndexByTask;
        private int tickableTaskTombstoneCount;
        private bool isTickingTasks;
        private ulong activationGeneration;
        private bool isEnding = false;
        private const int MaxRetainedPredictionRollbackTaskScratchCapacity = 256;
        private List<AbilityTaskLeaseSnapshot> predictionRollbackTaskScratch;
        private bool predictionTaskRollbackInProgress;

        private readonly struct AbilityTaskLeaseSnapshot
        {
            public AbilityTaskLeaseSnapshot(AbilityTask task)
            {
                Task = task;
                LeaseGeneration = task.LeaseGeneration;
            }

            public AbilityTask Task { get; }
            public ulong LeaseGeneration { get; }
        }

        protected GameplayAbility() { }

        public void Initialize(string name, EGameplayAbilityInstancingPolicy instancingPolicy, EAbilityExecutionPolicy executionPolicy,
            GameplayEffect cost, GameplayEffect cooldown, IReadOnlyGameplayTagContainer abilityTags,
            IReadOnlyGameplayTagContainer activationBlockedTags, IReadOnlyGameplayTagContainer activationRequiredTags,
            IReadOnlyGameplayTagContainer cancelAbilitiesWithTag, IReadOnlyGameplayTagContainer blockAbilitiesWithTag,
            IReadOnlyGameplayTagContainer activationOwnedTags = null, bool activateAbilityOnGranted = false,
            IReadOnlyGameplayTagContainer sourceRequiredTags = null, IReadOnlyGameplayTagContainer sourceBlockedTags = null,
            IReadOnlyGameplayTagContainer targetRequiredTags = null, IReadOnlyGameplayTagContainer targetBlockedTags = null,
            List<AbilityTriggerData> abilityTriggers = null)
        {
            if (configurationInitialized)
            {
                throw new System.InvalidOperationException("GameplayAbility configuration can only be initialized once.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new System.ArgumentException("GameplayAbility name must be a non-empty string.", nameof(name));
            }

            if (name.Length > MaxNameLength)
            {
                throw new System.ArgumentOutOfRangeException(nameof(name), name.Length, $"GameplayAbility names cannot exceed {MaxNameLength} characters.");
            }

            if ((int)instancingPolicy < (int)EGameplayAbilityInstancingPolicy.NonInstanced ||
                (int)instancingPolicy > (int)EGameplayAbilityInstancingPolicy.InstancedPerExecution)
            {
                throw new System.ArgumentOutOfRangeException(nameof(instancingPolicy), instancingPolicy, "Unknown GameplayAbility instancing policy.");
            }

            if ((int)executionPolicy < (int)EAbilityExecutionPolicy.LocalOnly ||
                (int)executionPolicy > (int)EAbilityExecutionPolicy.LocalPredicted)
            {
                throw new System.ArgumentOutOfRangeException(nameof(executionPolicy), executionPolicy, "Unknown GameplayAbility execution policy.");
            }

            int aggregateTagCount = 0;
            ValidateDefinitionTags(abilityTags, nameof(abilityTags), ref aggregateTagCount);
            ValidateDefinitionTags(activationBlockedTags, nameof(activationBlockedTags), ref aggregateTagCount);
            ValidateDefinitionTags(activationRequiredTags, nameof(activationRequiredTags), ref aggregateTagCount);
            ValidateDefinitionTags(cancelAbilitiesWithTag, nameof(cancelAbilitiesWithTag), ref aggregateTagCount);
            ValidateDefinitionTags(blockAbilitiesWithTag, nameof(blockAbilitiesWithTag), ref aggregateTagCount);
            ValidateDefinitionTags(activationOwnedTags, nameof(activationOwnedTags), ref aggregateTagCount);
            ValidateDefinitionTags(sourceRequiredTags, nameof(sourceRequiredTags), ref aggregateTagCount);
            ValidateDefinitionTags(sourceBlockedTags, nameof(sourceBlockedTags), ref aggregateTagCount);
            ValidateDefinitionTags(targetRequiredTags, nameof(targetRequiredTags), ref aggregateTagCount);
            ValidateDefinitionTags(targetBlockedTags, nameof(targetBlockedTags), ref aggregateTagCount);
            ValidateAbilityTriggers(abilityTriggers);

            var sealedAbilityTags = new GameplayDefinitionTagSet(abilityTags);
            var sealedActivationBlockedTags = new GameplayDefinitionTagSet(activationBlockedTags);
            var sealedActivationRequiredTags = new GameplayDefinitionTagSet(activationRequiredTags);
            var sealedCancelAbilitiesWithTag = new GameplayDefinitionTagSet(cancelAbilitiesWithTag);
            var sealedBlockAbilitiesWithTag = new GameplayDefinitionTagSet(blockAbilitiesWithTag);
            var sealedActivationOwnedTags = new GameplayDefinitionTagSet(activationOwnedTags);
            var sealedSourceRequiredTags = new GameplayDefinitionTagSet(sourceRequiredTags);
            var sealedSourceBlockedTags = new GameplayDefinitionTagSet(sourceBlockedTags);
            var sealedTargetRequiredTags = new GameplayDefinitionTagSet(targetRequiredTags);
            var sealedTargetBlockedTags = new GameplayDefinitionTagSet(targetBlockedTags);
            IReadOnlyList<AbilityTriggerData> sealedAbilityTriggers = abilityTriggers != null
                ? System.Array.AsReadOnly(abilityTriggers.ToArray())
                : System.Array.Empty<AbilityTriggerData>();

            Name = name;
            InstancingPolicy = instancingPolicy;
            ExecutionPolicy = executionPolicy;
            CostEffectDefinition = cost;
            CooldownEffectDefinition = cooldown;
            AbilityTags = sealedAbilityTags;
            ActivationBlockedTags = sealedActivationBlockedTags;
            ActivationRequiredTags = sealedActivationRequiredTags;
            CancelAbilitiesWithTag = sealedCancelAbilitiesWithTag;
            BlockAbilitiesWithTag = sealedBlockAbilitiesWithTag;
            ActivationOwnedTags = sealedActivationOwnedTags;
            ActivateAbilityOnGranted = activateAbilityOnGranted;
            SourceRequiredTags = sealedSourceRequiredTags;
            SourceBlockedTags = sealedSourceBlockedTags;
            TargetRequiredTags = sealedTargetRequiredTags;
            TargetBlockedTags = sealedTargetBlockedTags;
            AbilityTriggers = sealedAbilityTriggers;

            AbilityTagsSnapshot = AbilityTags.Snapshot;
            ActivationBlockedTagsSnapshot = ActivationBlockedTags.Snapshot;
            ActivationRequiredTagsSnapshot = ActivationRequiredTags.Snapshot;
            SourceRequiredTagsSnapshot = SourceRequiredTags.Snapshot;
            SourceBlockedTagsSnapshot = SourceBlockedTags.Snapshot;
            TargetRequiredTagsSnapshot = TargetRequiredTags.Snapshot;
            TargetBlockedTagsSnapshot = TargetBlockedTags.Snapshot;
            CooldownGrantedTagsSnapshot = cooldown?.GrantedTags?.Snapshot;
            configurationInitialized = true;
        }

        private static void ValidateDefinitionTags(
            IReadOnlyGameplayTagContainer tags,
            string parameterName,
            ref int aggregateTagCount)
        {
            if (tags == null || tags.IsEmpty)
            {
                return;
            }

            if (tags.TagCount > MaxAggregateTagCount - aggregateTagCount)
            {
                throw new System.ArgumentException(
                    $"GameplayAbility tag data exceeds the aggregate limit of {MaxAggregateTagCount} tags.",
                    parameterName);
            }

            foreach (GameplayTag tag in tags.GetExplicitTags())
            {
                if (tag.IsNone || !tag.IsValid)
                {
                    throw new System.ArgumentException("GameplayAbility tag data contains an invalid tag.", parameterName);
                }
            }

            aggregateTagCount += tags.TagCount;
        }

        private static void ValidateAbilityTriggers(List<AbilityTriggerData> abilityTriggers)
        {
            if (abilityTriggers == null)
            {
                return;
            }

            if (abilityTriggers.Count > MaxTriggerCount)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(abilityTriggers),
                    abilityTriggers.Count,
                    $"GameplayAbility trigger count cannot exceed {MaxTriggerCount}.");
            }

            for (int i = 0; i < abilityTriggers.Count; i++)
            {
                AbilityTriggerData trigger = abilityTriggers[i];
                if (trigger.TriggerTag.IsNone || !trigger.TriggerTag.IsValid)
                {
                    throw new System.ArgumentException($"GameplayAbility trigger {i} has an invalid tag.", nameof(abilityTriggers));
                }

                if ((int)trigger.TriggerSource < (int)EAbilityTriggerSource.GameplayEvent ||
                    (int)trigger.TriggerSource > (int)EAbilityTriggerSource.OwnedTagRemoved)
                {
                    throw new System.ArgumentException($"GameplayAbility trigger {i} has an unknown source.", nameof(abilityTriggers));
                }

                for (int previousIndex = 0; previousIndex < i; previousIndex++)
                {
                    AbilityTriggerData previous = abilityTriggers[previousIndex];
                    if (previous.TriggerTag == trigger.TriggerTag && previous.TriggerSource == trigger.TriggerSource)
                    {
                        throw new System.ArgumentException($"GameplayAbility trigger {i} duplicates trigger {previousIndex}.", nameof(abilityTriggers));
                    }
                }
            }
        }

        /// <summary>
        /// Called when the ability is granted to an AbilitySystemComponent.
        /// Use this for initial setup that requires access to the owner.
        /// </summary>
        public virtual void OnGiveAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            this.ActorInfo = actorInfo;
            this.Spec = spec;
            this.AbilitySystemComponent = spec.Owner;
            this.isEnding = false;
            this.activeTasks?.Clear();
            this.activeTaskIndexByTask?.Clear();
            this.tickableTasks?.Clear();
            this.tickableTaskIndexByTask?.Clear();
            this.tickableTaskTombstoneCount = 0;
            this.predictionRollbackTaskScratch?.Clear();
            this.predictionTaskRollbackInProgress = false;
        }

        /// <summary>
        /// Called when the ability is removed from the AbilitySystemComponent.
        /// Ensures the ability is properly cleaned up if it was active.
        /// </summary>
        public virtual void OnRemoveAbility()
        {
            CancelAbility();
        }

        /// <summary>
        /// The main execution entry point for the ability's logic. This method is intended to be overridden by subclasses.
        /// </summary>
        public virtual void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            GASLog.Warning(sb => sb.Append("Base ActivateAbility called for '").Append(Name).Append("'. Did you forget to override it in your specific ability class?"));
            GameplayAbilityCommitResult commit = CommitAbility(actorInfo, spec);
            if (!commit.Succeeded)
            {
                EndAbility();
            }
        }

        internal void SetCurrentActivationInfo(GameplayAbilityActivationInfo activationInfo)
        {
            if (activationGeneration == ulong.MaxValue)
            {
                throw new System.InvalidOperationException(
                    $"GameplayAbility '{Name}' activation generation is exhausted. Replace the runtime ability instance.");
            }

            activationGeneration++;
            CurrentActivationInfo = activationInfo;
        }

        internal void CommitTasksForPredictionKey(GASPredictionKey predictionKey)
        {
            List<AbilityTask> tasks = activeTasks;
            if (!predictionKey.IsValid || tasks == null)
            {
                return;
            }

            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                var task = tasks[i];
                if (task != null && task.IsBoundToPredictionKey(predictionKey))
                {
                    task.CommitPrediction();
                }
            }
        }

        internal void RollbackTasksForPredictionKey(GASPredictionKey predictionKey)
        {
            List<AbilityTask> tasks = activeTasks;
            if (!predictionKey.IsValid || tasks == null || tasks.Count == 0)
            {
                return;
            }

            if (predictionTaskRollbackInProgress)
            {
                throw new System.InvalidOperationException(
                    $"GameplayAbility '{Name}' prediction-task rollback cannot be re-entered.");
            }

            predictionRollbackTaskScratch ??= new List<AbilityTaskLeaseSnapshot>(System.Math.Min(tasks.Count, 8));
            List<AbilityTaskLeaseSnapshot> tasksToCancel = predictionRollbackTaskScratch;
            tasksToCancel.Clear();
            predictionTaskRollbackInProgress = true;
            try
            {
                for (int i = 0; i < tasks.Count; i++)
                {
                    AbilityTask task = tasks[i];
                    if (task != null && task.IsBoundToPredictionKey(predictionKey))
                    {
                        tasksToCancel.Add(new AbilityTaskLeaseSnapshot(task));
                    }
                }

                System.Exception cleanupFailure = null;
                for (int i = tasksToCancel.Count - 1; i >= 0; i--)
                {
                    AbilityTaskLeaseSnapshot snapshot = tasksToCancel[i];
                    AbilityTask task = snapshot.Task;
                    if (!task.IsCurrentLease(snapshot.LeaseGeneration) ||
                        !task.IsBoundToPredictionKey(predictionKey))
                    {
                        continue;
                    }

                    try
                    {
                        task.CancelTask();
                    }
                    catch (System.Exception exception)
                    {
                        cleanupFailure ??= exception;
                    }
                }

                if (cleanupFailure != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                }
            }
            finally
            {
                tasksToCancel.Clear();
                if (tasksToCancel.Capacity > MaxRetainedPredictionRollbackTaskScratchCapacity)
                {
                    predictionRollbackTaskScratch = null;
                }
                predictionTaskRollbackInProgress = false;
            }
        }

        /// <summary>
        /// Triggers the end of the ability's execution. This cleans up all active tasks and notifies the ASC.
        /// </summary>
        public void EndAbility()
        {
            if (isEnding)
            {
                return;
            }

            using (AbilitySystemComponent?.BeginAbilityEndMutationScope(this) ?? default)
            {
                isEnding = true;

                System.Exception cleanupFailure = CancelAllTasksAndResetActivationState();

                try
                {
                    AbilitySystemComponent?.OnAbilityEnded(this);
                }
                catch (System.Exception exception)
                {
                    cleanupFailure ??= exception;
                }
                if (cleanupFailure != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                }
            }
        }

        internal void CleanupTasksForSpecRemoval()
        {
            isEnding = true;
            System.Exception cleanupFailure = CancelAllTasksAndResetActivationState();
            isEnding = false;
            if (cleanupFailure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }

        private System.Exception CancelAllTasksAndResetActivationState()
        {
            System.Exception cleanupFailure = null;
            if (activeTasks != null)
            {
                for (int i = activeTasks.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        activeTasks[i].CancelTask();
                    }
                    catch (System.Exception exception)
                    {
                        cleanupFailure ??= exception;
                    }
                }
            }

            activeTasks?.Clear();
            activeTaskIndexByTask?.Clear();
            tickableTasks?.Clear();
            tickableTaskIndexByTask?.Clear();
            tickableTaskTombstoneCount = 0;
            CurrentActivationInfo = default;
            return cleanupFailure;
        }

        internal void InternalOnEndAbility()
        {
            isEnding = false;
        }

        /// <summary>
        /// A specific way to end the ability that implies it was interrupted rather than completed naturally.
        /// </summary>
        public virtual void CancelAbility()
        {
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.AbilityCancelled, AbilitySystemComponent, this, decision: GASTraceDecision.Success, abilitySpecHandle: Spec?.Handle ?? 0, predictionKey: CurrentActivationInfo.PredictionKey, level: Spec?.Level ?? 0);
            }
            EndAbility();
        }

        /// <summary>
        /// Called when the ability's bound input action is pressed while the ability is active.
        /// Override this to implement channeling or hold-type ability behavior.
        /// UE5: UGameplayAbility::InputPressed.
        /// </summary>
        public virtual void InputPressed(GameplayAbilitySpec spec)
        {
        }

        /// <summary>
        /// Called when the ability's bound input action is released while the ability is active.
        /// Override this to implement release-to-fire or charged ability behavior.
        /// UE5: UGameplayAbility::InputReleased.
        /// </summary>
        public virtual void InputReleased(GameplayAbilitySpec spec)
        {
        }

        /// <summary>
        /// Creates a new AbilityTask runtime instance, initializes it, and adds it to the active tasks list.
        /// </summary>
        public T NewAbilityTask<T>() where T : AbilityTask, new()
        {
            if (isEnding)
            {
                throw new System.InvalidOperationException(
                    $"Ability '{Name}' cannot create AbilityTasks while it is ending.");
            }

            AbilitySystemComponent owner = AbilitySystemComponent;
            if (owner == null || owner.IsDisposed || Spec == null || !ReferenceEquals(Spec.Owner, owner))
            {
                throw new System.InvalidOperationException("Ability tasks require an active owning AbilitySystemComponent.");
            }

            var memory = owner.RuntimeContext.Memory;
            var task = memory.AcquireTask<T>();
            ulong taskLeaseGeneration = task.LeaseGeneration;
            try
            {
                task.InitTask(this);
                if (!task.IsCurrentLease(taskLeaseGeneration))
                {
                    throw new System.InvalidOperationException(
                        $"AbilityTask '{typeof(T).FullName}' changed its lease during initialization.");
                }

                activeTasks ??= new List<AbilityTask>();
                activeTaskIndexByTask ??= new Dictionary<AbilityTask, int>();
                activeTaskIndexByTask[task] = activeTasks.Count;
                activeTasks.Add(task);
                if (task.PredictionKey.IsValid)
                {
                    AbilitySystemComponent.NotifyPredictedAbilityTaskCreated(task.PredictionKey);
                }

                if (task is IAbilityTaskTick tickable)
                {
                    tickableTasks ??= new List<IAbilityTaskTick>();
                    tickableTaskIndexByTask ??= new Dictionary<IAbilityTaskTick, int>();
                    tickableTaskIndexByTask[tickable] = tickableTasks.Count;
                    tickableTasks.Add(tickable);
                }
                return task;
            }
            catch
            {
                if (task.IsCurrentLease(taskLeaseGeneration))
                {
                    OnTaskEnded(task);
                    activeTaskIndexByTask?.Remove(task);
                    activeTasks?.Remove(task);
                    if (task is IAbilityTaskTick tickable)
                    {
                        tickableTaskIndexByTask?.Remove(tickable);
                        tickableTasks?.Remove(tickable);
                    }
                    memory.ReleaseTask(task, releaseSucceeded: false);
                }
                throw;
            }
        }

        internal void OnTaskEnded(AbilityTask task)
        {
            if (isEnding)
            {
                return;
            }

            List<AbilityTask> tasks = activeTasks;
            Dictionary<AbilityTask, int> taskIndexes = activeTaskIndexByTask;
            if (tasks != null &&
                taskIndexes != null &&
                taskIndexes.TryGetValue(task, out int index) &&
                index >= 0 &&
                index < tasks.Count &&
                ReferenceEquals(tasks[index], task))
            {
                int lastIndex = tasks.Count - 1;
                if (index != lastIndex)
                {
                    var movedTask = tasks[lastIndex];
                    tasks[index] = movedTask;
                    taskIndexes[movedTask] = index;
                }
                tasks.RemoveAt(lastIndex);
                taskIndexes.Remove(task);
            }

            if (task is IAbilityTaskTick tickable)
            {
                List<IAbilityTaskTick> tickingTasks = tickableTasks;
                Dictionary<IAbilityTaskTick, int> tickingTaskIndexes = tickableTaskIndexByTask;
                if (tickingTasks != null &&
                    tickingTaskIndexes != null &&
                    tickingTaskIndexes.TryGetValue(tickable, out int tickIndex) &&
                    tickIndex >= 0 &&
                    tickIndex < tickingTasks.Count &&
                    ReferenceEquals(tickingTasks[tickIndex], tickable))
                {
                    if (isTickingTasks)
                    {
                        tickingTasks[tickIndex] = null;
                        tickingTaskIndexes.Remove(tickable);
                        tickableTaskTombstoneCount++;
                        return;
                    }

                    int lastTickIndex = tickingTasks.Count - 1;
                    if (tickIndex != lastTickIndex)
                    {
                        var movedTickable = tickingTasks[lastTickIndex];
                        tickingTasks[tickIndex] = movedTickable;
                        tickingTaskIndexes[movedTickable] = tickIndex;
                    }
                    tickingTasks.RemoveAt(lastTickIndex);
                    tickingTaskIndexes.Remove(tickable);
                }
            }
        }

        public void TickTasks(float deltaTime)
        {
            if (isTickingTasks)
            {
                throw new System.InvalidOperationException(
                    $"AbilityTask ticking cannot re-enter ability '{Name}'. Defer the nested tick until the next simulation frame.");
            }

            List<IAbilityTaskTick> tasks = tickableTasks;
            Dictionary<IAbilityTaskTick, int> taskIndexes = tickableTaskIndexByTask;
            if (tasks == null || taskIndexes == null || tasks.Count == 0)
            {
                return;
            }

            AbilitySystemComponent tickOwner = AbilitySystemComponent;
            GameplayAbilitySpec tickSpec = Spec;
            ulong tickActivationGeneration = activationGeneration;

            isTickingTasks = true;
            int tickCount = tasks.Count;
            try
            {
                for (int i = tickCount - 1; i >= 0; i--)
                {
                    if (activationGeneration != tickActivationGeneration ||
                        !ReferenceEquals(AbilitySystemComponent, tickOwner) ||
                        !ReferenceEquals(Spec, tickSpec))
                    {
                        break;
                    }

                    if (i >= tasks.Count)
                    {
                        continue;
                    }

                    IAbilityTaskTick tickable = tasks[i];
                    if (tickable == null ||
                        !taskIndexes.TryGetValue(tickable, out int currentIndex) ||
                        currentIndex != i)
                    {
                        continue;
                    }

                    AbilityTask tickTask = tickable as AbilityTask;
                    ulong tickTaskLeaseGeneration = tickTask?.LeaseGeneration ?? 0UL;
                    try
                    {
                        tickable.Tick(deltaTime);
                    }
                    catch (System.Exception exception)
                    {
                        GASLog.Error($"AbilityTask tick failed for ability '{Name}': {exception.Message}");
                        if (tickTask != null)
                        {
                            try { tickTask.EndTaskIfCurrentLease(tickTaskLeaseGeneration); }
                            catch (System.Exception cleanupException)
                            {
                                GASLog.Error($"AbilityTask cleanup failed after a tick exception: {cleanupException.Message}");
                            }
                        }
                    }
                }
            }
            finally
            {
                isTickingTasks = false;
                CompactTickableTaskTombstones();
            }
        }

        private void CompactTickableTaskTombstones()
        {
            if (tickableTaskTombstoneCount <= 0)
            {
                return;
            }

            List<IAbilityTaskTick> tasks = tickableTasks;
            Dictionary<IAbilityTaskTick, int> taskIndexes = tickableTaskIndexByTask;
            if (tasks == null || taskIndexes == null)
            {
                tickableTaskTombstoneCount = 0;
                return;
            }

            int writeIndex = 0;
            for (int readIndex = 0; readIndex < tasks.Count; readIndex++)
            {
                IAbilityTaskTick tickable = tasks[readIndex];
                if (tickable == null)
                {
                    continue;
                }

                if (writeIndex != readIndex)
                {
                    tasks[writeIndex] = tickable;
                }
                taskIndexes[tickable] = writeIndex;
                writeIndex++;
            }

            if (writeIndex < tasks.Count)
            {
                tasks.RemoveRange(writeIndex, tasks.Count - writeIndex);
            }
            tickableTaskTombstoneCount = 0;
        }

        /// <summary>
        /// Checks all conditions (tags, cost, cooldown) to determine if the ability can be activated.
        /// </summary>
        public virtual bool CanActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            if (isEnding)
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.IsEnding, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            if (spec.Owner.HasAnyMatchingGameplayTags(ActivationBlockedTagsSnapshot))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.ActivationBlockedTags, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            if (!spec.Owner.HasAllMatchingGameplayTags(ActivationRequiredTagsSnapshot))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.ActivationRequiredTags, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            // UE5: Source tag requirements --check tags on the source (owner)
            if (!spec.Owner.HasAllMatchingGameplayTags(SourceRequiredTagsSnapshot))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.SourceRequiredTags, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            if (spec.Owner.HasAnyMatchingGameplayTags(SourceBlockedTagsSnapshot))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.SourceBlockedTags, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            // UE5: Check if any active ability is blocking us via BlockAbilitiesWithTag
            if (AbilityTags != null && !AbilityTags.IsEmpty && spec.Owner.AreAbilitiesBlockedByTag(AbilityTags))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.BlockedByActiveAbility, abilitySpecHandle: spec.Handle);
                }
                GASLog.Debug(sb => sb.Append("Ability '").Append(Name).Append("' blocked by another active ability's BlockAbilitiesWithTag."));
                return false;
            }

            if (!CheckCooldown(spec.Owner))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.Cooldown, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            if (!CheckCost(spec.Owner))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, spec.Owner, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.Cost, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks whether this ability can be applied to the given target based on TargetRequiredTags and TargetBlockedTags.
        /// UE5: DoesAbilitySatisfyTagRequirements for target checks.
        /// </summary>
        /// <param name="target">The target ASC to check against.</param>
        /// <returns>True if the target meets the tag requirements.</returns>
        public virtual bool CanApplyToTarget(AbilitySystemComponent target)
        {
            if (target == null) return false;
            if (!target.HasAllMatchingGameplayTags(TargetRequiredTagsSnapshot)) return false;
            if (target.HasAnyMatchingGameplayTags(TargetBlockedTagsSnapshot)) return false;
            return true;
        }

        /// <summary>
        /// Checks if the owner has the cooldown tag associated with this ability.
        /// </summary>
        protected bool CheckCooldown(AbilitySystemComponent asc)
        {
            if (CooldownGrantedTagsSnapshot != null)
            {
                if (asc.HasAnyMatchingGameplayTagsExact(CooldownGrantedTagsSnapshot))
                {
                    GASLog.Debug(sb => sb.Append("Ability '").Append(Name).Append("' failed: on cooldown."));
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks if the owner has sufficient resources to pay the ability's cost.
        /// </summary>
        protected bool CheckCost(AbilitySystemComponent asc)
        {
            if (CostEffectDefinition == null)
            {
                return true;
            }

            GameplayEffectSpec costSpec = CreateCostEffectSpec(asc, Spec);
            if (costSpec == null)
            {
                return false;
            }

            try
            {
                costSpec.SetTarget(asc);
                return CanAffordCostSpec(asc, costSpec);
            }
            finally
            {
                costSpec.TryDiscardCallerOwned();
            }
        }

        /// <summary>
        /// Applies the cost and cooldown effects. This should be called once the ability's outcome is certain.
        /// </summary>
        public GameplayAbilityCommitResult CommitAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            AbilitySystemComponent asc = spec?.Owner ?? AbilitySystemComponent;
            if (asc == null)
            {
                return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.MissingOwner);
            }

            if (CostEffectDefinition != null && CostEffectDefinition.DurationPolicy != EDurationPolicy.Instant)
            {
                return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.InvalidCostDefinition);
            }

            if (CooldownEffectDefinition != null && CooldownEffectDefinition.DurationPolicy == EDurationPolicy.Instant)
            {
                return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.InvalidCooldownDefinition);
            }

            if (!CheckCooldown(asc))
            {
                return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.CooldownActive);
            }

            GameplayEffectSpec costSpec = null;
            GameplayEffectSpec cooldownSpec = null;
            try
            {
                costSpec = CostEffectDefinition != null ? CreateCostEffectSpec(asc, spec) : null;
                cooldownSpec = CooldownEffectDefinition != null ? CreateCooldownEffectSpec(asc, spec) : null;

                if (CostEffectDefinition != null && costSpec == null)
                {
                    return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.InvalidCostDefinition);
                }

                if (CooldownEffectDefinition != null && cooldownSpec == null)
                {
                    return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.InvalidCooldownDefinition);
                }

                if (costSpec != null)
                {
                    costSpec.SetTarget(asc);
                    if (!CanAffordCostSpec(asc, costSpec))
                    {
                        return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.CostUnavailable);
                    }

                    GameplayEffectApplicationResultCode validation = asc.CanApplyGameplayEffectSpec(costSpec);
                    if (validation != GameplayEffectApplicationResultCode.Applied)
                    {
                        return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.CostEffectRejected, validation);
                    }
                }

                if (cooldownSpec != null)
                {
                    GameplayEffectApplicationResultCode validation = asc.CanApplyGameplayEffectSpec(cooldownSpec);
                    if (validation != GameplayEffectApplicationResultCode.Applied)
                    {
                        return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.CooldownEffectRejected, validation);
                    }
                }

                ReplicationStateBuilder.MutationScope commitScope = costSpec != null || cooldownSpec != null
                    ? asc.BeginReplicationMutationScope()
                    : default;
                using (commitScope)
                {
                    ActiveGameplayEffect appliedCooldown = null;
                    if (cooldownSpec != null)
                    {
                        GameplayEffectApplicationResult cooldownResult = asc.ApplyGameplayEffectSpecToSelf(cooldownSpec);
                        cooldownSpec = null;
                        if (!cooldownResult.Succeeded)
                        {
                            return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.CooldownEffectRejected, cooldownResult.Code);
                        }

                        appliedCooldown = cooldownResult.ActiveEffect;
                    }

                    if (costSpec != null)
                    {
                        GameplayEffectApplicationResult costResult = asc.ApplyGameplayEffectSpecToSelf(costSpec);
                        costSpec = null;
                        if (!costResult.Succeeded)
                        {
                            if (appliedCooldown != null)
                            {
                                asc.TryRemoveActiveEffect(appliedCooldown);
                            }

                            return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.CostEffectRejected, costResult.Code);
                        }
                    }

                    asc.NotifyAbilityCommitted(this);
                    return new GameplayAbilityCommitResult(GameplayAbilityCommitResultCode.Committed);
                }
            }
            finally
            {
                costSpec?.TryDiscardCallerOwned();
                cooldownSpec?.TryDiscardCallerOwned();
            }
        }

        protected virtual GameplayEffectSpec CreateCostEffectSpec(AbilitySystemComponent asc, GameplayAbilitySpec spec)
        {
            return CostEffectDefinition != null ? GameplayEffectSpec.Create(CostEffectDefinition, asc, spec?.Level ?? 1) : null;
        }

        protected virtual GameplayEffectSpec CreateCooldownEffectSpec(AbilitySystemComponent asc, GameplayAbilitySpec spec)
        {
            return CooldownEffectDefinition != null ? GameplayEffectSpec.Create(CooldownEffectDefinition, asc, spec?.Level ?? 1) : null;
        }

        private static bool CanAffordCostSpec(AbilitySystemComponent asc, GameplayEffectSpec costSpec)
        {
            var modifiers = costSpec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                ModifierInfo modifier = modifiers[i];
                GameplayAttribute attribute = asc.GetAttribute(modifier.AttributeName);
                if (attribute == null)
                {
                    return false;
                }

                GASFixedValue current = attribute.CurrentFixedValue;
                GASFixedValue magnitude = GASFixedValue.FromRaw(costSpec.GetCalculatedMagnitudeRaw(i));
                GASFixedValue candidate;
                switch (modifier.Operation)
                {
                    case EAttributeModifierOperation.Add:
                        candidate = current + magnitude;
                        break;
                    case EAttributeModifierOperation.Multiply:
                        candidate = current * magnitude;
                        break;
                    case EAttributeModifierOperation.Division:
                        candidate = magnitude.RawValue != 0 ? current / magnitude : current;
                        break;
                    case EAttributeModifierOperation.Override:
                        candidate = magnitude;
                        break;
                    default:
                        candidate = current;
                        break;
                }

                if (candidate.RawValue < 0L)
                {
                    return false;
                }
            }

            return true;
        }

        #region Convenience API (UE5 parity)

        /// <summary>
        /// Gets the current level of this ability from its spec.
        /// UE5: GetAbilityLevel().
        /// </summary>
        public int GetAbilityLevel() => Spec?.Level ?? 1;

        /// <summary>
        /// Creates a GameplayEffectSpec from a GameplayEffect definition, automatically populating
        /// the context with this ability as the source. This is the primary way to create effect specs from abilities.
        /// UE5: MakeOutgoingGameplayEffectSpec.
        /// </summary>
        /// <param name="effectDef">The GameplayEffect definition to create a spec from.</param>
        /// <param name="level">Override level. If -1, uses the ability's current level.</param>
        /// <returns>A fully initialized GameplayEffectSpec with proper context.</returns>
        public GameplayEffectSpec MakeOutgoingGameplayEffectSpec(GameplayEffect effectDef, int level = -1)
        {
            if (effectDef == null || AbilitySystemComponent == null) return null;
            if (level == 0 || level < -1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(level), level, "Effect level must be -1 or greater than zero.");
            }

            int effectLevel = level >= 0 ? level : GetAbilityLevel();
            GameplayEffectContext context = AbilitySystemComponent.MakeEffectContext();
            try
            {
                context.AddInstigator(AbilitySystemComponent, this);
                return GameplayEffectSpec.Create(effectDef, AbilitySystemComponent, context, effectLevel);
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates and applies a GameplayEffect to the specified target.
        /// UE5: ApplyGameplayEffectToTarget.
        /// </summary>
        /// <param name="effectDef">The GameplayEffect definition.</param>
        /// <param name="target">The target AbilitySystemComponent.</param>
        /// <param name="level">Override level. If -1, uses the ability's current level.</param>
        /// <returns>The created ActiveGameplayEffect if successfully applied, or null.</returns>
        public GameplayEffectApplicationResult ApplyGameplayEffectToTarget(GameplayEffect effectDef, AbilitySystemComponent target, int level = -1)
        {
            if (target == null)
            {
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.InvalidSpec);
            }

            var spec = MakeOutgoingGameplayEffectSpec(effectDef, level);
            if (spec != null)
            {
                try
                {
                    return target.ApplyGameplayEffectSpecToSelf(spec);
                }
                catch
                {
                    spec.TryDiscardCallerOwned();
                    throw;
                }
            }

            return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.InvalidDefinition);
        }

        /// <summary>
        /// Creates and applies a GameplayEffect to the owning ASC.
        /// UE5: ApplyGameplayEffectToOwner / K2_ApplyGameplayEffectToOwner.
        /// </summary>
        /// <param name="effectDef">The GameplayEffect definition.</param>
        /// <param name="level">Override level. If -1, uses the ability's current level.</param>
        public GameplayEffectApplicationResult ApplyGameplayEffectToOwner(GameplayEffect effectDef, int level = -1)
        {
            if (AbilitySystemComponent != null)
            {
                return ApplyGameplayEffectToTarget(effectDef, AbilitySystemComponent, level);
            }

            return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.InvalidSpec);
        }

        /// <summary>
        /// Creates and applies a GameplayEffectSpec to the specified target.
        /// Use this when you need to configure the spec (e.g., SetByCaller) before applying.
        /// UE5: ApplyGameplayEffectSpecToTarget.
        /// </summary>
        public GameplayEffectApplicationResult ApplyGameplayEffectSpecToTarget(GameplayEffectSpec spec, AbilitySystemComponent target)
        {
            if (spec != null && target != null)
            {
                return target.ApplyGameplayEffectSpecToSelf(spec);
            }

            return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.InvalidSpec);
        }

        #endregion

        /// <summary>
        /// Creates a distinct runtime instance of this ability definition.
        /// </summary>
        public abstract GameplayAbility CreateRuntimeInstance();

        internal void CopyConfigurationFrom(GameplayAbility template)
        {
            if (template == null) throw new System.ArgumentNullException(nameof(template));
            if (!template.configurationInitialized)
            {
                throw new System.InvalidOperationException("Cannot instantiate an uninitialized GameplayAbility definition.");
            }

            Name = template.Name;
            InstancingPolicy = template.InstancingPolicy;
            ExecutionPolicy = template.ExecutionPolicy;
            CostEffectDefinition = template.CostEffectDefinition;
            CooldownEffectDefinition = template.CooldownEffectDefinition;
            AbilityTags = template.AbilityTags;
            ActivationBlockedTags = template.ActivationBlockedTags;
            ActivationRequiredTags = template.ActivationRequiredTags;
            CancelAbilitiesWithTag = template.CancelAbilitiesWithTag;
            BlockAbilitiesWithTag = template.BlockAbilitiesWithTag;
            ActivationOwnedTags = template.ActivationOwnedTags;
            ActivateAbilityOnGranted = template.ActivateAbilityOnGranted;
            SourceRequiredTags = template.SourceRequiredTags;
            SourceBlockedTags = template.SourceBlockedTags;
            TargetRequiredTags = template.TargetRequiredTags;
            TargetBlockedTags = template.TargetBlockedTags;
            AbilityTriggers = template.AbilityTriggers;
            AbilityTagsSnapshot = template.AbilityTagsSnapshot;
            ActivationBlockedTagsSnapshot = template.ActivationBlockedTagsSnapshot;
            ActivationRequiredTagsSnapshot = template.ActivationRequiredTagsSnapshot;
            SourceRequiredTagsSnapshot = template.SourceRequiredTagsSnapshot;
            SourceBlockedTagsSnapshot = template.SourceBlockedTagsSnapshot;
            TargetRequiredTagsSnapshot = template.TargetRequiredTagsSnapshot;
            TargetBlockedTagsSnapshot = template.TargetBlockedTagsSnapshot;
            CooldownGrantedTagsSnapshot = template.CooldownGrantedTagsSnapshot;
            configurationInitialized = true;
        }

        internal void MarkLeaseAcquired(GASRuntimeMemory owner, GameplayAbility definition)
        {
            if (runtimeLeaseActive || runtimeLeaseEverAcquired)
            {
                throw new System.InvalidOperationException($"GameplayAbility '{Name}' cannot receive another runtime lease.");
            }

            if (owner == null) throw new System.ArgumentNullException(nameof(owner));
            runtimeDefinition = definition ?? throw new System.ArgumentNullException(nameof(definition));
            runtimeLeaseEverAcquired = true;
            runtimeLeaseActive = true;
        }

        internal bool TryReleaseLease()
        {
            if (!runtimeLeaseActive)
            {
                return false;
            }

            runtimeLeaseActive = false;
            return true;
        }

        internal void OnRuntimeInstanceReleased()
        {
            try
            {
                ResetRuntimeState();
            }
            finally
            {
                Spec = null;
                AbilitySystemComponent = null;
                ActorInfo = default;
                CurrentActivationInfo = default;
                isEnding = false;
                activeTasks?.Clear();
                activeTaskIndexByTask?.Clear();
                tickableTasks?.Clear();
                tickableTaskIndexByTask?.Clear();
                tickableTaskTombstoneCount = 0;
                predictionRollbackTaskScratch?.Clear();
                if (predictionRollbackTaskScratch?.Capacity > MaxRetainedPredictionRollbackTaskScratchCapacity)
                {
                    predictionRollbackTaskScratch = null;
                }
                predictionTaskRollbackInProgress = false;
            }
        }

        /// <summary>
        /// Clears mutable state owned by a derived ability when the runtime instance is released.
        /// Do not release shared definition data from this hook.
        /// </summary>
        protected virtual void ResetRuntimeState()
        {
        }
    }
}
