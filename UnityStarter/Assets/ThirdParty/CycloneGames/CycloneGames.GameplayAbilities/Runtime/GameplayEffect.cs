using System;
using System.Collections;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Immutable tag data owned by a Gameplay Ability System definition.
    /// </summary>
    public sealed class GameplayDefinitionTagSet : IReadOnlyGameplayTagContainer
    {
        private readonly GameplayTagContainer values;
        private readonly ReadOnlyGameplayTagContainer snapshot;

        internal GameplayDefinitionTagSet(IReadOnlyGameplayTagContainer source = null)
        {
            values = source != null ? new GameplayTagContainer(source) : new GameplayTagContainer();
            snapshot = values.CreateSnapshot();
        }

        public bool IsEmpty => snapshot.IsEmpty;
        public int ExplicitTagCount => snapshot.ExplicitTagCount;
        public int TagCount => snapshot.TagCount;

        public GameplayTagContainerIndices Indices
        {
            get
            {
                EnsureCompatible();
                return values.Indices;
            }
        }

        internal ReadOnlyGameplayTagContainer Snapshot => snapshot;

        /// <summary>
        /// Creates an isolated mutable copy. Changes to the copy never affect the definition.
        /// </summary>
        public GameplayTagContainer ToMutableContainer()
        {
            EnsureCompatible();
            return new GameplayTagContainer(values);
        }

        public GameplayTagEnumerator GetTags()
        {
            EnsureCompatible();
            return values.GetTags();
        }

        public GameplayTagEnumerator GetExplicitTags()
        {
            EnsureCompatible();
            return values.GetExplicitTags();
        }

        public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
        {
            EnsureCompatible();
            values.GetParentTags(tag, parentTags);
        }

        public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
        {
            EnsureCompatible();
            values.GetChildTags(tag, childTags);
        }

        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
        {
            EnsureCompatible();
            values.GetExplicitParentTags(tag, parentTags);
        }

        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
        {
            EnsureCompatible();
            values.GetExplicitChildTags(tag, childTags);
        }

        public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
        {
            EnsureCompatible();
            return values.ContainsRuntimeIndex(runtimeIndex, explicitOnly);
        }

        public GameplayTagEnumerator GetEnumerator()
        {
            EnsureCompatible();
            return values.GetTags();
        }

        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static implicit operator GameplayTagContainer(GameplayDefinitionTagSet source)
        {
            return source?.ToMutableContainer();
        }

        private void EnsureCompatible()
        {
            if (!snapshot.IsCompatibleWithCurrentRegistry)
            {
                throw new InvalidOperationException(
                "Gameplay definition tag data belongs to an incompatible gameplay tag runtime-index epoch.");
            }
        }

    }

    /// <summary>
    /// Immutable required and forbidden tag data owned by a <see cref="GameplayEffect"/> definition.
    /// </summary>
    public sealed class GameplayEffectTagRequirements
    {
        internal GameplayEffectTagRequirements(GameplayTagRequirements source)
        {
            ForbiddenTags = new GameplayDefinitionTagSet(source.ForbiddenTags);
            RequiredTags = new GameplayDefinitionTagSet(source.RequiredTags);
        }

        public GameplayDefinitionTagSet ForbiddenTags { get; }
        public GameplayDefinitionTagSet RequiredTags { get; }
        public bool IsEmpty => ForbiddenTags.IsEmpty && RequiredTags.IsEmpty;

        public bool Matches<T>(in T container) where T : IReadOnlyGameplayTagContainer
        {
            return !container.HasAny(ForbiddenTags) && container.HasAll(RequiredTags);
        }

        public bool Matches<T, U>(in T staticContainer, in U dynamicContainer)
            where T : IReadOnlyGameplayTagContainer
            where U : IReadOnlyGameplayTagContainer
        {
            return !staticContainer.HasAny(ForbiddenTags) &&
                   !dynamicContainer.HasAny(ForbiddenTags) &&
                   GameplayTagContainerUtility.HasAll(staticContainer, dynamicContainer, RequiredTags);
        }

        public bool MeetsRequirements(GameplayTagCountContainer container)
        {
            return !container.HasAny(ForbiddenTags) && container.HasAll(RequiredTags);
        }

        public GameplayTagRequirements ToMutableRequirements()
        {
            return new GameplayTagRequirements(
                ForbiddenTags.ToMutableContainer(),
                RequiredTags.ToMutableContainer());
        }

        public static implicit operator GameplayTagRequirements(GameplayEffectTagRequirements source)
        {
            return source != null ? source.ToMutableRequirements() : default;
        }
    }

    /// <summary>
    /// Defines the immutable data for a gameplay effect. This class is a runtime representation of a GameplayEffectSO.
    /// It is a stateless data container that describes all properties and potential outcomes of an effect,
    /// designed to be shared and reused. An instance of this class is often referred to as a 'GE Definition' or 'CDO'.
    /// </summary>
    public sealed class GameplayEffect
    {
        public const int MaxNameLength = 256;
        public const int MaxDefinitionStringLength = 256;
        public const int MaxCustomApplicationRequirementCount = 64;
        public const int MaxOverflowEffectCount = 64;
        public static readonly int MaxModifierCount = GASRuntimeLimits.Default.MaxModifiersPerEffect;
        public static readonly int MaxGrantedAbilityCount = Math.Min(GASRuntimeLimits.Default.MaxGrantedAbilities, 256);
        public static readonly int MaxAggregateTagCount = Math.Min(GASRuntimeLimits.Default.MaxTagChangesPerDelta, 256);
        public static readonly int MaxStackCount = GASRuntimeLimits.Default.MaxActiveEffects;

        /// <summary>
        /// The unique name used to identify this effect, primarily for logging and debugging purposes.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Defines the lifetime policy of the effect (Instant, HasDuration, Infinite).
        /// </summary>
        public EDurationPolicy DurationPolicy { get; }

        /// <summary>
        /// The total duration of the effect in seconds. This is only used if DurationPolicy is <c>HasDuration</c>.
        /// </summary>
        public float Duration { get; }

        /// <summary>
        /// The interval in seconds at which the effect's instant components are re-applied.
        /// </summary>
        public float Period { get; }

        /// <summary>
        /// A list of attribute modifications to apply to the target.
        /// </summary>
        public IReadOnlyList<ModifierInfo> Modifiers { get; }

        /// <summary>
        /// A custom, non-predictable calculation class that can perform complex, multi-attribute logic.
        /// Implementations are shared strategy objects and must remain stateless after definition construction.
        /// </summary>
        public GameplayEffectExecutionCalculation Execution { get; }

        /// <summary>
        /// Defines how this effect interacts with other instances of the same effect on a target.
        /// </summary>
        public GameplayEffectStacking Stacking { get; }

        /// <summary>
        /// A list of abilities to grant to the target for the duration of this effect.
        /// Referenced ability templates are shared definitions and must not be reconfigured after construction.
        /// </summary>
        public IReadOnlyList<GameplayAbility> GrantedAbilities { get; }

        /// <summary>
        /// A list of GameplayCue tags to trigger when this effect is applied, removed, or executed.
        /// </summary>
        public GameplayDefinitionTagSet GameplayCues { get; }

        /// <summary>
        /// Tags that describe the effect itself. These are NOT granted to the target.
        /// </summary>
        public GameplayDefinitionTagSet AssetTags { get; }

        /// <summary>
        /// Tags that are temporarily granted to the target's AbilitySystemComponent for the duration of this effect.
        /// </summary>
        public GameplayDefinitionTagSet GrantedTags { get; }

        /// <summary>
        /// Defines the tag requirements on a target for this effect to be successfully applied.
        /// </summary>
        public GameplayEffectTagRequirements ApplicationTagRequirements { get; }

        /// <summary>
        /// Once applied, the effect will only be active if the target continues to meet these tag requirements.
        /// </summary>
        public GameplayEffectTagRequirements OngoingTagRequirements { get; }

        /// <summary>
        /// Upon successful application, any active effects on the target matching these tags will be removed.
        /// </summary>
        public GameplayDefinitionTagSet RemoveGameplayEffectsWithTags { get; }

        /// <summary>
        /// If true, gameplay cues (VFX/SFX) are suppressed for this effect.
        /// UE5: bSuppressGameplayCues on UGameplayEffect.
        /// Useful for silent/debug application without visual feedback.
        /// </summary>
        public bool SuppressGameplayCues { get; }

        /// <summary>
        /// If true, gameplay effects applied by the granting ability are automatically removed when the ability ends.
        /// UE5: RemoveGameplayEffectContainerOnAbilityEnd / bRemoveGameplayEffectsAfterAbilityEnds.
        /// </summary>
        public bool RemoveGameplayEffectsAfterAbilityEnds { get; }

        /// <summary>
        /// Optional custom application requirement. If set, CanApplyGameplayEffect is called before application.
        /// Requirement implementations are shared strategy objects and must remain stateless.
        /// UE5: TArray&lt;TSubclassOf&lt;UGameplayEffectCustomApplicationRequirement&gt;&gt;.
        /// </summary>
        public IReadOnlyList<ICustomApplicationRequirement> CustomApplicationRequirements { get; }

        /// <summary>
        /// If true, periodic effects execute their first tick immediately upon application.
        /// If false, the first execution waits for the full period interval.
        /// UE5: bExecutePeriodicEffectOnApplication. Default is true (UE5 default).
        /// </summary>
        public bool ExecutePeriodicEffectOnApplication { get; }

        /// <summary>
        /// Effects to apply when a stacking application attempt occurs while at the stack limit.
        /// UE5: OverflowEffects.
        /// </summary>
        public IReadOnlyList<GameplayEffect> OverflowEffects { get; }

        /// <summary>
        /// If true, the original effect application (duration refresh, etc.) is denied when overflow occurs.
        /// UE5: bDenyOverflowApplication.
        /// </summary>
        public bool DenyOverflowApplication { get; }

        internal ReadOnlyGameplayTagContainer GameplayCuesSnapshot { get; }
        internal ReadOnlyGameplayTagContainer AssetTagsSnapshot { get; }
        internal ReadOnlyGameplayTagContainer GrantedTagsSnapshot { get; }
        internal ReadOnlyGameplayTagContainer RemoveGameplayEffectsWithTagsSnapshot { get; }
        internal ReadOnlyGameplayTagContainer ApplicationRequiredTagsSnapshot { get; }
        internal ReadOnlyGameplayTagContainer ApplicationForbiddenTagsSnapshot { get; }
        internal ReadOnlyGameplayTagContainer OngoingRequiredTagsSnapshot { get; }
        internal ReadOnlyGameplayTagContainer OngoingForbiddenTagsSnapshot { get; }

        public GameplayEffect(
            string name,
            EDurationPolicy durationPolicy,
            float duration = 0,
            float period = 0,
            List<ModifierInfo> modifiers = null,
            GameplayEffectExecutionCalculation execution = null,
            GameplayEffectStacking stacking = default,
            List<GameplayAbility> grantedAbilities = null,
            IReadOnlyGameplayTagContainer assetTags = null,
            IReadOnlyGameplayTagContainer grantedTags = null,
            GameplayTagRequirements applicationTagRequirements = default,
            GameplayTagRequirements ongoingTagRequirements = default,
            IReadOnlyGameplayTagContainer removeGameplayEffectsWithTags = null,
            IReadOnlyGameplayTagContainer gameplayCues = null,
            bool suppressGameplayCues = false,
            bool removeGameplayEffectsAfterAbilityEnds = false,
            List<ICustomApplicationRequirement> customApplicationRequirements = null,
            bool executePeriodicEffectOnApplication = true,
            List<GameplayEffect> overflowEffects = null,
            bool denyOverflowApplication = false)
        {
            ValidateBoundedString(name, MaxNameLength, nameof(name), allowWhitespace: false);
            ValidateDuration(durationPolicy, duration, period);
            ValidateStacking(stacking);
            ValidateCollectionCount(modifiers, MaxModifierCount, nameof(modifiers));
            ValidateCollectionCount(grantedAbilities, MaxGrantedAbilityCount, nameof(grantedAbilities));
            ValidateCollectionCount(customApplicationRequirements, MaxCustomApplicationRequirementCount, nameof(customApplicationRequirements));
            ValidateCollectionCount(overflowEffects, MaxOverflowEffectCount, nameof(overflowEffects));
            ValidateModifiers(modifiers);
            ValidateGrantedAbilities(durationPolicy, grantedAbilities);
            ValidateRequirements(customApplicationRequirements);
            ValidateOverflowEffects(overflowEffects);

            int aggregateTagCount = 0;
            AssetTags = CreateTagSet(assetTags, nameof(assetTags), ref aggregateTagCount);
            GrantedTags = CreateTagSet(grantedTags, nameof(grantedTags), ref aggregateTagCount);
            ApplicationTagRequirements = CreateRequirements(
                applicationTagRequirements,
                nameof(applicationTagRequirements),
                ref aggregateTagCount);
            OngoingTagRequirements = CreateRequirements(
                ongoingTagRequirements,
                nameof(ongoingTagRequirements),
                ref aggregateTagCount);
            RemoveGameplayEffectsWithTags = CreateTagSet(
                removeGameplayEffectsWithTags,
                nameof(removeGameplayEffectsWithTags),
                ref aggregateTagCount);
            GameplayCues = CreateTagSet(gameplayCues, nameof(gameplayCues), ref aggregateTagCount);

            Name = name;
            DurationPolicy = durationPolicy;
            Duration = duration;
            Period = period;
            Modifiers = FreezeModifiers(modifiers);
            Execution = execution;
            Stacking = stacking;
            GrantedAbilities = Freeze(grantedAbilities);
            SuppressGameplayCues = suppressGameplayCues;
            RemoveGameplayEffectsAfterAbilityEnds = removeGameplayEffectsAfterAbilityEnds;
            CustomApplicationRequirements = Freeze(customApplicationRequirements);
            ExecutePeriodicEffectOnApplication = executePeriodicEffectOnApplication;
            OverflowEffects = Freeze(overflowEffects);
            DenyOverflowApplication = denyOverflowApplication;

            GameplayCuesSnapshot = GameplayCues.Snapshot;
            AssetTagsSnapshot = AssetTags.Snapshot;
            GrantedTagsSnapshot = GrantedTags.Snapshot;
            RemoveGameplayEffectsWithTagsSnapshot = RemoveGameplayEffectsWithTags.Snapshot;
            ApplicationRequiredTagsSnapshot = ApplicationTagRequirements.RequiredTags.Snapshot;
            ApplicationForbiddenTagsSnapshot = ApplicationTagRequirements.ForbiddenTags.Snapshot;
            OngoingRequiredTagsSnapshot = OngoingTagRequirements.RequiredTags.Snapshot;
            OngoingForbiddenTagsSnapshot = OngoingTagRequirements.ForbiddenTags.Snapshot;
        }

        private static void ValidateDuration(EDurationPolicy durationPolicy, float duration, float period)
        {
            if ((int)durationPolicy < (int)EDurationPolicy.Instant || (int)durationPolicy > (int)EDurationPolicy.Infinite)
            {
                throw new ArgumentOutOfRangeException(nameof(durationPolicy), durationPolicy, "Unknown duration policy.");
            }

            ValidateFinite(duration, nameof(duration));
            if (durationPolicy == EDurationPolicy.HasDuration && duration <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "HasDuration effects require a positive duration.");
            }

            if (float.IsNaN(period) || float.IsInfinity(period) || period < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be finite and non-negative.");
            }

            if (durationPolicy == EDurationPolicy.Instant && period > 0f)
            {
                throw new ArgumentException("Instant effects cannot be periodic.", nameof(period));
            }
        }

        private static void ValidateStacking(GameplayEffectStacking stacking)
        {
            if ((int)stacking.Type < (int)EGameplayEffectStackingType.None ||
                (int)stacking.Type > (int)EGameplayEffectStackingType.AggregateByTarget)
            {
                throw new ArgumentOutOfRangeException(nameof(stacking), stacking.Type, "Unknown stacking type.");
            }

            if (stacking.Type != EGameplayEffectStackingType.None &&
                (stacking.Limit <= 0 || stacking.Limit > MaxStackCount))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stacking),
                    stacking.Limit,
                    $"Stack limits must be between 1 and {MaxStackCount}.");
            }

            if (stacking.Type != EGameplayEffectStackingType.None &&
                ((int)stacking.DurationPolicy < (int)EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication ||
                 (int)stacking.DurationPolicy > (int)EGameplayEffectStackingDurationPolicy.NeverRefresh ||
                 (int)stacking.ExpirationPolicy < (int)EGameplayEffectStackingExpirationPolicy.ClearEntireStack ||
                 (int)stacking.ExpirationPolicy > (int)EGameplayEffectStackingExpirationPolicy.RefreshDuration))
            {
                throw new ArgumentOutOfRangeException(nameof(stacking), "Stacking policies must use known enum values.");
            }
        }

        private static GameplayEffectTagRequirements CreateRequirements(
            GameplayTagRequirements source,
            string parameterName,
            ref int aggregateTagCount)
        {
            ValidateAndAccumulateTags(source.ForbiddenTags, parameterName, ref aggregateTagCount);
            ValidateAndAccumulateTags(source.RequiredTags, parameterName, ref aggregateTagCount);
            return new GameplayEffectTagRequirements(source);
        }

        private static GameplayDefinitionTagSet CreateTagSet(
            IReadOnlyGameplayTagContainer source,
            string parameterName,
            ref int aggregateTagCount)
        {
            ValidateAndAccumulateTags(source, parameterName, ref aggregateTagCount);
            return new GameplayDefinitionTagSet(source);
        }

        private static void ValidateAndAccumulateTags(
            IReadOnlyGameplayTagContainer source,
            string parameterName,
            ref int aggregateTagCount)
        {
            if (source == null || source.IsEmpty)
            {
                return;
            }

            if (source.TagCount > MaxAggregateTagCount - aggregateTagCount)
            {
                throw new ArgumentException(
                    $"GameplayEffect tag data exceeds the aggregate limit of {MaxAggregateTagCount} tags.",
                    parameterName);
            }

            foreach (GameplayTag tag in source.GetExplicitTags())
            {
                if (tag.IsNone || !tag.IsValid)
                {
                    throw new ArgumentException("GameplayEffect tag data contains an invalid tag.", parameterName);
                }

                ValidateBoundedString(tag.Name, MaxDefinitionStringLength, parameterName, allowWhitespace: false);
            }

            aggregateTagCount += source.TagCount;
        }

        private static IReadOnlyList<ModifierInfo> FreezeModifiers(List<ModifierInfo> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0)
            {
                return EmptyReadOnlyList<ModifierInfo>.Value;
            }

            var copy = new ModifierInfo[modifiers.Count];
            for (int i = 0; i < modifiers.Count; i++)
            {
                copy[i] = CloneModifier(modifiers[i]);
            }

            return Array.AsReadOnly(copy);
        }

        private static ModifierInfo CloneModifier(ModifierInfo modifier)
        {
            switch (modifier.MagnitudeCalculationType)
            {
                case EGameplayEffectMagnitudeCalculation.AttributeBased:
                    return new ModifierInfo(
                        modifier.AttributeName,
                        modifier.Operation,
                        modifier.AttributeBasedMagnitude,
                        modifier.EvaluationChannel);
                case EGameplayEffectMagnitudeCalculation.CustomCalculation:
                    return new ModifierInfo(
                        modifier.AttributeName,
                        modifier.Operation,
                        modifier.CustomCalculation,
                        modifier.SnapshotPolicy,
                        modifier.EvaluationChannel);
                case EGameplayEffectMagnitudeCalculation.SetByCaller:
                    return new ModifierInfo(
                        modifier.AttributeName,
                        modifier.Operation,
                        modifier.SetByCallerMagnitude,
                        modifier.EvaluationChannel);
                default:
                    return new ModifierInfo(
                        modifier.AttributeName,
                        modifier.Operation,
                        modifier.Magnitude,
                        modifier.EvaluationChannel);
            }
        }

        private static IReadOnlyList<T> Freeze<T>(List<T> source)
        {
            return source == null || source.Count == 0
                ? EmptyReadOnlyList<T>.Value
                : Array.AsReadOnly(source.ToArray());
        }

        private static void ValidateModifiers(List<ModifierInfo> modifiers)
        {
            if (modifiers == null) return;
            for (int i = 0; i < modifiers.Count; i++)
            {
                ModifierInfo modifier = modifiers[i] ??
                    throw new ArgumentException($"GameplayEffect modifier {i} is null.", nameof(modifiers));
                ValidateBoundedString(
                    modifier.AttributeName,
                    MaxDefinitionStringLength,
                    nameof(modifiers),
                    allowWhitespace: false);

                if ((int)modifier.Operation < (int)EAttributeModifierOperation.Add ||
                    (int)modifier.Operation > (int)EAttributeModifierOperation.Override)
                {
                    throw new ArgumentException($"GameplayEffect modifier {i} uses an unknown operation.", nameof(modifiers));
                }
                if ((int)modifier.MagnitudeCalculationType < (int)EGameplayEffectMagnitudeCalculation.ScalableFloat ||
                    (int)modifier.MagnitudeCalculationType > (int)EGameplayEffectMagnitudeCalculation.SetByCaller)
                {
                    throw new ArgumentException($"GameplayEffect modifier {i} uses an unknown magnitude calculation type.", nameof(modifiers));
                }
                if ((int)modifier.SnapshotPolicy < (int)EGameplayEffectAttributeCaptureSnapshot.Snapshot ||
                    (int)modifier.SnapshotPolicy > (int)EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                {
                    throw new ArgumentException($"GameplayEffect modifier {i} uses an unknown snapshot policy.", nameof(modifiers));
                }

                switch (modifier.MagnitudeCalculationType)
                {
                    case EGameplayEffectMagnitudeCalculation.ScalableFloat:
                        ValidateFinite(modifier.Magnitude.BaseValue, nameof(modifiers));
                        ValidateFinite(modifier.Magnitude.ScalingFactorPerLevel, nameof(modifiers));
                        break;
                    case EGameplayEffectMagnitudeCalculation.AttributeBased:
                        ValidateBoundedString(
                            modifier.AttributeBasedMagnitude.AttributeName,
                            MaxDefinitionStringLength,
                            nameof(modifiers),
                            allowWhitespace: false);
                        if ((int)modifier.AttributeBasedMagnitude.CaptureSource < (int)EGameplayEffectAttributeCaptureSource.Source ||
                            (int)modifier.AttributeBasedMagnitude.CaptureSource > (int)EGameplayEffectAttributeCaptureSource.Target ||
                            (int)modifier.AttributeBasedMagnitude.CalculationType < (int)EAttributeBasedFloatCalculationType.AttributeMagnitude ||
                            (int)modifier.AttributeBasedMagnitude.CalculationType > (int)EAttributeBasedFloatCalculationType.AttributeBonusMagnitude)
                        {
                            throw new ArgumentException($"GameplayEffect modifier {i} uses an invalid attribute capture policy.", nameof(modifiers));
                        }
                        ValidateFinite(modifier.AttributeBasedMagnitude.Coefficient.BaseValue, nameof(modifiers));
                        ValidateFinite(modifier.AttributeBasedMagnitude.Coefficient.ScalingFactorPerLevel, nameof(modifiers));
                        ValidateFinite(modifier.AttributeBasedMagnitude.PreMultiplyAdditiveValue.BaseValue, nameof(modifiers));
                        ValidateFinite(modifier.AttributeBasedMagnitude.PreMultiplyAdditiveValue.ScalingFactorPerLevel, nameof(modifiers));
                        ValidateFinite(modifier.AttributeBasedMagnitude.PostMultiplyAdditiveValue.BaseValue, nameof(modifiers));
                        ValidateFinite(modifier.AttributeBasedMagnitude.PostMultiplyAdditiveValue.ScalingFactorPerLevel, nameof(modifiers));
                        break;
                    case EGameplayEffectMagnitudeCalculation.CustomCalculation:
                        if (modifier.CustomCalculation == null)
                        {
                            throw new ArgumentException($"GameplayEffect modifier {i} requires a custom calculation instance.", nameof(modifiers));
                        }
                        break;
                    case EGameplayEffectMagnitudeCalculation.SetByCaller:
                        if ((modifier.SetByCallerMagnitude.DataTag.IsNone || !modifier.SetByCallerMagnitude.DataTag.IsValid) &&
                            string.IsNullOrWhiteSpace(modifier.SetByCallerMagnitude.DataName))
                        {
                            throw new ArgumentException($"GameplayEffect modifier {i} requires a SetByCaller tag or name.", nameof(modifiers));
                        }
                        if (!string.IsNullOrEmpty(modifier.SetByCallerMagnitude.DataName))
                        {
                            ValidateBoundedString(
                                modifier.SetByCallerMagnitude.DataName,
                                MaxDefinitionStringLength,
                                nameof(modifiers),
                                allowWhitespace: false);
                        }
                        ValidateFinite(modifier.SetByCallerMagnitude.DefaultValue, nameof(modifiers));
                        break;
                }
            }
        }

        private static void ValidateGrantedAbilities(EDurationPolicy durationPolicy, List<GameplayAbility> grantedAbilities)
        {
            if (grantedAbilities == null) return;
            if (durationPolicy == EDurationPolicy.Instant && grantedAbilities.Count > 0)
            {
                throw new ArgumentException(
                    "Instant GameplayEffects cannot grant abilities because they have no owning active-effect lifetime.",
                    nameof(grantedAbilities));
            }
            for (int i = 0; i < grantedAbilities.Count; i++)
            {
                GameplayAbility ability = grantedAbilities[i] ??
                    throw new ArgumentException($"Granted ability {i} is null.", nameof(grantedAbilities));
                if (!ability.IsConfigurationInitialized)
                {
                    throw new ArgumentException($"Granted ability {i} is not initialized.", nameof(grantedAbilities));
                }
                ValidateBoundedString(
                    ability.Name,
                    MaxDefinitionStringLength,
                    nameof(grantedAbilities),
                    allowWhitespace: false);
                if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.NonInstanced)
                {
                    throw new ArgumentException(
                        $"Granted ability '{ability.Name}' uses NonInstanced, which is not supported by the Unity Runtime ability model.",
                        nameof(grantedAbilities));
                }
            }
        }

        private static void ValidateRequirements(List<ICustomApplicationRequirement> requirements)
        {
            if (requirements == null) return;
            for (int i = 0; i < requirements.Count; i++)
            {
                if (requirements[i] == null)
                {
                    throw new ArgumentException($"Custom application requirement {i} is null.", nameof(requirements));
                }
            }
        }

        private static void ValidateOverflowEffects(List<GameplayEffect> overflowEffects)
        {
            if (overflowEffects == null) return;
            for (int i = 0; i < overflowEffects.Count; i++)
            {
                if (overflowEffects[i] == null)
                {
                    throw new ArgumentException($"Overflow GameplayEffect {i} is null.", nameof(overflowEffects));
                }
            }
        }

        private static void ValidateCollectionCount<T>(List<T> values, int maximum, string parameterName)
        {
            if (values != null && values.Count > maximum)
            {
                throw new ArgumentException(
                    $"GameplayEffect {parameterName} contains {values.Count} entries; the limit is {maximum}.",
                    parameterName);
            }
        }

        private static void ValidateBoundedString(
            string value,
            int maximumLength,
            string parameterName,
            bool allowWhitespace)
        {
            if (value == null || (!allowWhitespace && string.IsNullOrWhiteSpace(value)))
            {
                throw new ArgumentException("GameplayEffect definition strings must be non-empty.", parameterName);
            }

            if (value.Length > maximumLength)
            {
                throw new ArgumentException(
                    $"GameplayEffect definition strings cannot exceed {maximumLength} characters.",
                    parameterName);
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    throw new ArgumentException(
                        "GameplayEffect definition strings cannot contain control characters.",
                        parameterName);
                }
            }
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "GameplayEffect numeric definition values must be finite.");
            }
        }

        private static class EmptyReadOnlyList<T>
        {
            internal static readonly IReadOnlyList<T> Value = Array.AsReadOnly(Array.Empty<T>());
        }
    }
}
