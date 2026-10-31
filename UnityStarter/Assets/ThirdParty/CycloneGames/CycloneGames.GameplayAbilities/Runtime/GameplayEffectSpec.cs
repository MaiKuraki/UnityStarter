using System;
using System.Collections;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A generation-stamped view over one dynamic tag set owned by an active GameplayEffectSpec lease.
    /// The view never owns storage and rejects every operation after its originating spec is released.
    /// </summary>
    public readonly struct GameplayEffectSpecTagView : IGameplayTagContainer, IGameplayTagEpochProvider
    {
        private readonly GameplayEffectSpec owner;
        private readonly ulong leaseGeneration;
        private readonly bool grantedTags;

        internal GameplayEffectSpecTagView(
            GameplayEffectSpec owner,
            ulong leaseGeneration,
            bool grantedTags)
        {
            this.owner = owner;
            this.leaseGeneration = leaseGeneration;
            this.grantedTags = grantedTags;
        }

        /// <summary>The epoch of the spec's dynamic tag container, so a copy can reject this view when stale.</summary>
        int IGameplayTagEpochProvider.RuntimeIndexEpoch => GetReadContainer().RuntimeIndexEpoch;

        public bool IsEmpty => GetReadContainer().IsEmpty;
        public int ExplicitTagCount => GetReadContainer().ExplicitTagCount;
        public int TagCount => GetReadContainer().TagCount;

        /// <summary>The expanded tag at <paramref name="index"/>, in ascending index order.</summary>
        public GameplayTag GetTag(int index)
        {
            return GetReadContainer().GetTag(index);
        }

        /// <summary>The explicit tag at <paramref name="index"/>, in ascending index order.</summary>
        public GameplayTag GetExplicitTag(int index)
        {
            return GetReadContainer().GetExplicitTag(index);
        }

        public void AddTag(GameplayTag gameplayTag)
        {
            GameplayTagContainer inner = GetWriteContainer();
            owner.EnsureDynamicTagCapacity(inner, gameplayTag);
            inner.AddTag(gameplayTag);
        }

        public void RemoveTag(GameplayTag gameplayTag)
        {
            GetWriteContainer().RemoveTag(gameplayTag);
        }

        public GameplayTagEnumerator GetTags()
        {
            return GetReadContainer().GetTags();
        }

        public GameplayTagEnumerator GetExplicitTags()
        {
            return GetReadContainer().GetExplicitTags();
        }

        public void AddTags<T>(in T other) where T : IReadOnlyGameplayTagContainer
        {
            GameplayTagContainer inner = GetWriteContainer();
            owner.EnsureDynamicTagCapacity(inner, other);
            inner.AddTags(other);
        }

        public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
        {
            GetReadContainer().GetParentTags(tag, parentTags);
        }

        public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
        {
            GetReadContainer().GetChildTags(tag, childTags);
        }

        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
        {
            GetReadContainer().GetExplicitParentTags(tag, parentTags);
        }

        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
        {
            GetReadContainer().GetExplicitChildTags(tag, childTags);
        }

        public void RemoveTags<T>(in T other) where T : IReadOnlyGameplayTagContainer
        {
            GetWriteContainer().RemoveTags(other);
        }

        public void Clear()
        {
            GetWriteContainer().Clear();
        }

        public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
        {
            return GetReadContainer().ContainsRuntimeIndex(runtimeIndex, explicitOnly);
        }

        public GameplayTagEnumerator GetEnumerator()
        {
            return GetTags();
        }

        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator()
        {
            return GetTags();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetTags();
        }

        private GameplayTagContainer GetReadContainer()
        {
            if (owner == null)
            {
                throw new ObjectDisposedException(nameof(GameplayEffectSpecTagView));
            }

            return owner.ResolveDynamicTagContainer(leaseGeneration, grantedTags, requireMutation: false);
        }

        private GameplayTagContainer GetWriteContainer()
        {
            if (owner == null)
            {
                throw new ObjectDisposedException(nameof(GameplayEffectSpecTagView));
            }

            return owner.ResolveDynamicTagContainer(leaseGeneration, grantedTags, requireMutation: true);
        }
    }

    internal sealed class GameplayEffectSpecBacking
    {
        internal readonly GameplayTagContainer DynamicGrantedTags = new GameplayTagContainer();
        internal readonly GameplayTagContainer DynamicAssetTags = new GameplayTagContainer();
        internal Dictionary<GameplayTag, long> SetByCallerMagnitudes;
        internal Dictionary<string, long> SetByCallerMagnitudesByName;
        internal float[] ModifierMagnitudes = Array.Empty<float>();
        internal long[] ModifierMagnitudeRawValues = Array.Empty<long>();
        internal GameplayAttribute[] TargetAttributes = Array.Empty<GameplayAttribute>();
        internal GameplayAttribute[] CapturedAttributes = Array.Empty<GameplayAttribute>();
        internal GameplayAttribute[] CapturedDeclaredAttributes = Array.Empty<GameplayAttribute>();
        internal GameplayTag[] ReplicatedStateTagScratch = Array.Empty<GameplayTag>();
        internal long[] ReplicatedStateValueScratch = Array.Empty<long>();
        internal string[] ReplicatedStateNameScratch = Array.Empty<string>();
        internal long[] ReplicatedStateNameValueScratch = Array.Empty<long>();
        internal GameplayTag[] ReplicatedDynamicGrantedTagScratch = Array.Empty<GameplayTag>();
        internal GameplayTag[] ReplicatedDynamicAssetTagScratch = Array.Empty<GameplayTag>();
        internal long[] ReplicatedModifierMagnitudeScratch = Array.Empty<long>();
        internal GameplayAttribute[] ModifierGroupAttributes = Array.Empty<GameplayAttribute>();
        internal int[] ModifierGroupStarts = Array.Empty<int>();
        internal int[] ModifierGroupIndices = Array.Empty<int>();

        internal void ClearSensitiveData()
        {
            DynamicGrantedTags.Clear();
            DynamicAssetTags.Clear();
            SetByCallerMagnitudes?.Clear();
            SetByCallerMagnitudesByName?.Clear();
            Clear(ModifierMagnitudes);
            Clear(ModifierMagnitudeRawValues);
            Clear(TargetAttributes);
            Clear(CapturedAttributes);
            Clear(CapturedDeclaredAttributes);
            Clear(ReplicatedStateTagScratch);
            Clear(ReplicatedStateValueScratch);
            Clear(ReplicatedStateNameScratch);
            Clear(ReplicatedStateNameValueScratch);
            Clear(ReplicatedDynamicGrantedTagScratch);
            Clear(ReplicatedDynamicAssetTagScratch);
            Clear(ReplicatedModifierMagnitudeScratch);
            Clear(ModifierGroupAttributes);
            Clear(ModifierGroupStarts);
            Clear(ModifierGroupIndices);
        }

        private static void Clear<T>(T[] values)
        {
            if (values != null && values.Length != 0)
            {
                Array.Clear(values, 0, values.Length);
            }
        }
    }

    /// <summary>
    /// Represents a stateful, runtime instance of a GameplayEffect.
    /// This class encapsulates all the necessary context for an effect's application,
    /// such as its source, target, level, and pre-calculated modifier magnitudes.
    /// Owned by the source runtime context until its one-shot lease is released.
    /// The spec owns and disposes its effect context.
    /// </summary>
    public sealed class GameplayEffectSpec : IGASLeasedObject
    {
        private static readonly LogChannel Log = GameplayAbilitiesLog.Channel;

        private enum SpecOwnership : byte
        {
            None,
            Caller,
            AbilitySystem,
            ActiveEffect
        }

        private GASRuntimeMemory memoryOwner;
        private GameplayEffectSpecBacking backing;
        private bool leaseActive;
        private bool leaseEverAcquired;
        private ulong leaseGeneration;
        private SpecOwnership ownership;
        private bool ownsContext;
        private int modifierCount;
        private int maxSetByCallerEntries = GASRuntimeLimits.Default.MaxSetByCallerEntries;
        private GameplayEffect definition;
        private AbilitySystemComponent source;
        private AbilitySystemComponent target;
        private GameplayEffectContext context;
        private int level;
        private long durationRaw;
        // Set by an explicit SetDuration* call. While set, declarative DurationMagnitude
        // re-evaluation (e.g. triggered by SetByCaller changes) no longer touches durationRaw:
        // an explicit caller override always wins over data-driven re-evaluation.
        private bool durationExplicitlyOverridden;
        /// <summary>
        /// The stateless definition (template) of this effect.
        /// </summary>
        public GameplayEffect Def
        {
            get { EnsurePublicReadAllowed(); return definition; }
            private set { definition = value; }
        }

        /// <summary>
        /// The AbilitySystemComponent that created and applied this effect.
        /// </summary>
        public AbilitySystemComponent Source
        {
            get { EnsurePublicReadAllowed(); return source; }
            private set { source = value; }
        }

        /// <summary>
        /// Stable identity of the source, kept after the source component detaches or is disposed.
        /// </summary>
        public GASEntityId SourceEntity { get; private set; }

        /// <summary>Drops the live source reference; captures and <see cref="SourceEntity"/> stay valid.</summary>
        internal void DetachSource()
        {
            source = null;
            context?.DetachInstigator();
        }

        /// <summary>
        /// The AbilitySystemComponent that this effect is applied to.
        /// </summary>
        public AbilitySystemComponent Target
        {
            get { EnsurePublicReadAllowed(); return target; }
            private set { target = value; }
        }

        /// <summary>
        /// A context object carrying metadata about the effect's application.
        /// </summary>
        public GameplayEffectContext Context
        {
            get { EnsurePublicReadAllowed(); return context; }
            private set { context = value; }
        }

        /// <summary>
        /// The level at which this effect spec was created.
        /// </summary>
        public int Level
        {
            get { EnsurePublicReadAllowed(); return level; }
            private set { level = value; }
        }

        /// <summary>
        /// The duration for this specific instance of the effect.
        /// </summary>
        public float Duration
        {
            get { EnsurePublicReadAllowed(); return GASFixedValue.FromRaw(durationRaw).ToFloat(); }
        }

        public long DurationRaw
        {
            get { EnsurePublicReadAllowed(); return durationRaw; }
            private set { durationRaw = value; }
        }

        #region Duration Override

        /// <summary>
        /// Overrides the duration of this specific spec instance before submission.
        /// The shared GameplayEffect definition is never mutated; only this caller-owned
        /// spec instance is affected. ActiveGameplayEffect initialization, stacking
        /// duration refresh, and the authoritative replication channel all read the
        /// per-spec duration, so the override flows through every consumption path.
        /// Mutation is only valid while the caller owns the spec; submission is the lock.
        /// Typical usage: override GameplayAbility.CreateCooldownEffectSpec and call
        /// SetDuration with a computed cooldown (attack speed, cooldown reduction, ...).
        /// </summary>
        /// <param name="seconds">The new duration in seconds. Must be positive and finite.</param>
        /// <exception cref="InvalidOperationException">
        /// The spec is no longer caller-owned (already submitted or released), an
        /// extension callback is evaluating it, or the definition's DurationPolicy
        /// is not <see cref="EDurationPolicy.HasDuration"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
        public void SetDuration(float seconds)
        {
            EnsureDurationOverrideAllowed();
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Effect duration override must be positive and finite.");
            }

            durationRaw = GASFixedValue.FromFloat(seconds).RawValue;
            durationExplicitlyOverridden = true;
        }

        /// <summary>
        /// Fixed-point overload of <see cref="SetDuration(float)"/> for simulation paths
        /// that must avoid float at the API edge.
        /// </summary>
        public void SetDuration(GASFixedValue seconds)
        {
            SetDurationRaw(seconds.RawValue);
        }

        /// <summary>
        /// Raw fixed-point overload of <see cref="SetDuration(float)"/>.
        /// Zero is accepted (the effect expires on its first tick); negative is rejected.
        /// </summary>
        /// <exception cref="InvalidOperationException">Same gating as <see cref="SetDuration(float)"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public void SetDurationRaw(long durationRaw)
        {
            EnsureDurationOverrideAllowed();
            if (durationRaw < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(durationRaw), durationRaw, "Effect duration override cannot be negative.");
            }

            this.durationRaw = durationRaw;
            durationExplicitlyOverridden = true;
        }

        private void EnsureDurationOverrideAllowed()
        {
            EnsureCallerOwned();
            ThrowIfEvaluatingReplicatedState();
            if (definition == null || definition.DurationPolicy != EDurationPolicy.HasDuration)
            {
                throw new InvalidOperationException(
                    $"SetDuration requires DurationPolicy.HasDuration; effect '{definition?.Name ?? "<null>"}' uses '{definition?.DurationPolicy}'.");
            }
        }

        #endregion

        private float[] modifierMagnitudes = System.Array.Empty<float>();
        private long[] modifierMagnitudeRawValues = System.Array.Empty<long>();
        private GameplayAttribute[] targetAttributes = System.Array.Empty<GameplayAttribute>();
        private GameplayAttribute[] capturedAttributes = System.Array.Empty<GameplayAttribute>();
        private GameplayAttribute[] capturedDeclaredAttributes = System.Array.Empty<GameplayAttribute>();

        // Lazily-built grouping of modifier indices by resolved target attribute. The aggregation
        // path (AbilitySystemComponent.RecalculateDirtyAttributes) uses it to iterate only the
        // modifiers affecting a dirty attribute. Indices within each group are in ascending
        // modifier order, which defines the per-attribute modifier evaluation order.
        // Rebuilt lazily whenever targetAttributes content changes (modifierGroupingDirty).
        // The arrays ride the pooled spec backing (swapped in AttachBacking/ReleaseBackingToOwner);
        // standalone specs without a backing own their arrays directly.
        private GameplayAttribute[] modifierGroupAttributes = System.Array.Empty<GameplayAttribute>();
        private int[] modifierGroupStarts = System.Array.Empty<int>();
        private int[] modifierGroupIndices = System.Array.Empty<int>();
        private int modifierGroupCount;
        private bool modifierGroupingDirty = true;

        // Lazily-resolved attribute references for live (non-snapshot) attribute-based modifier
        // magnitudes. Cached per spec because Source/Target are fixed once the effect is applied;
        // invalidated together with the modifier grouping whenever target attributes change.
        private GameplayAttribute[] dependencySourceAttributes = System.Array.Empty<GameplayAttribute>();
        private bool dependencySourceAttributesResolved;
        private GameplayTag[] replicatedStateTagScratch = System.Array.Empty<GameplayTag>();
        private long[] replicatedStateValueScratch = System.Array.Empty<long>();
        private string[] replicatedStateNameScratch = System.Array.Empty<string>();
        private long[] replicatedStateNameValueScratch = System.Array.Empty<long>();
        private GameplayTag[] replicatedDynamicGrantedTagScratch = System.Array.Empty<GameplayTag>();
        private GameplayTag[] replicatedDynamicAssetTagScratch = System.Array.Empty<GameplayTag>();
        private long[] replicatedModifierMagnitudeScratch = System.Array.Empty<long>();
        private int replicatedStateCount;
        private int replicatedStateNameCount;
        private bool evaluatingReplicatedState;
        private int externalEvaluationDepth;
        private int setByCallerWarningSuppressionDepth;
        private bool setByCallerSubmissionWarningsValidated;

        public ReadOnlySpan<float> ModifierMagnitudes
        {
            get { EnsurePublicReadAllowed(); return modifierMagnitudes.AsSpan(0, modifierCount); }
        }

        public ReadOnlySpan<long> ModifierMagnitudeRawValues
        {
            get { EnsurePublicReadAllowed(); return modifierMagnitudeRawValues.AsSpan(0, modifierCount); }
        }

        public ReadOnlySpan<GameplayAttribute> TargetAttributes
        {
            get { EnsurePublicReadAllowed(); return targetAttributes.AsSpan(0, modifierCount); }
        }

        /// <summary>
        /// Tags added at runtime to this specific spec instance, supplementing the definition's GrantedTags.
        /// These tags are granted to the target in addition to Def.GrantedTags.
        /// </summary>
        public GameplayEffectSpecTagView DynamicGrantedTags
        {
            get
            {
                EnsureLeaseIsActive(leaseGeneration);
                return new GameplayEffectSpecTagView(this, leaseGeneration, grantedTags: true);
            }
        }

        /// <summary>
        /// Tags added at runtime to this specific spec instance, supplementing the definition's AssetTags.
        /// These tags describe this specific instance and can be used for immunity/removal checks.
        /// </summary>
        public GameplayEffectSpecTagView DynamicAssetTags
        {
            get
            {
                EnsureLeaseIsActive(leaseGeneration);
                return new GameplayEffectSpecTagView(this, leaseGeneration, grantedTags: false);
            }
        }

        private GameplayTagContainer dynamicGrantedTags;
        private GameplayTagContainer dynamicAssetTags;

        // SetByCaller magnitude storage --null-lazy to avoid Dictionary allocation on specs that never use SetByCaller.
        // The vast majority of effect specs (damage, buffs, cooldowns) do not use this API.
        private Dictionary<GameplayTag, long> setByCallerMagnitudes;
        private Dictionary<string, long> setByCallerMagnitudesByName;

        private Dictionary<GameplayTag, long> GetOrCreateTagMagnitudes()
            => setByCallerMagnitudes ??= new Dictionary<GameplayTag, long>();

        private Dictionary<string, long> GetOrCreateNameMagnitudes()
            => setByCallerMagnitudesByName ??= new Dictionary<string, long>(System.StringComparer.Ordinal);

        internal GameplayEffectSpec() { }

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
            if (backing == null)
            {
                throw new InvalidOperationException("GameplayEffectSpec requires attached backing storage before lease acquisition.");
            }

            if (leaseGeneration == ulong.MaxValue)
            {
                throw new InvalidOperationException("GameplayEffectSpec lease generation is exhausted.");
            }

            leaseGeneration++;
            ownership = SpecOwnership.Caller;
            ownsContext = false;
            evaluatingReplicatedState = false;
            externalEvaluationDepth = 0;
            setByCallerWarningSuppressionDepth = 0;
            setByCallerSubmissionWarningsValidated = false;
            replicatedStateCount = 0;
            replicatedStateNameCount = 0;
        }

        void IGASLeasedObject.OnLeaseReleased()
        {
            ownership = SpecOwnership.None;
            source?.UnregisterOutboundSpec(this);
            try
            {
                if (ownsContext)
                {
                    context?.ReturnFromSpec(this);
                }
            }
            finally
            {
                ownsContext = false;
                Def = null;
                Source = null;
                SourceEntity = default;
                Target = null;
                Context = null;
                Level = 0;
                DurationRaw = 0L;
                modifierCount = 0;
                replicatedStateCount = 0;
                replicatedStateNameCount = 0;
                evaluatingReplicatedState = false;
                externalEvaluationDepth = 0;
                setByCallerWarningSuppressionDepth = 0;
                setByCallerSubmissionWarningsValidated = false;
                ReleaseBackingToOwner();
            }
        }

        internal void SetMemoryOwner(GASRuntimeMemory owner) => memoryOwner = owner;

        internal void AttachBacking(GameplayEffectSpecBacking value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (backing != null)
            {
                throw new InvalidOperationException("GameplayEffectSpec already has attached backing storage.");
            }

            backing = value;
            dynamicGrantedTags = value.DynamicGrantedTags;
            dynamicAssetTags = value.DynamicAssetTags;
            setByCallerMagnitudes = value.SetByCallerMagnitudes;
            setByCallerMagnitudesByName = value.SetByCallerMagnitudesByName;
            modifierMagnitudes = value.ModifierMagnitudes;
            modifierMagnitudeRawValues = value.ModifierMagnitudeRawValues;
            targetAttributes = value.TargetAttributes;
            capturedAttributes = value.CapturedAttributes;
            capturedDeclaredAttributes = value.CapturedDeclaredAttributes;
            replicatedStateTagScratch = value.ReplicatedStateTagScratch;
            replicatedStateValueScratch = value.ReplicatedStateValueScratch;
            replicatedStateNameScratch = value.ReplicatedStateNameScratch;
            replicatedStateNameValueScratch = value.ReplicatedStateNameValueScratch;
            replicatedDynamicGrantedTagScratch = value.ReplicatedDynamicGrantedTagScratch;
            replicatedDynamicAssetTagScratch = value.ReplicatedDynamicAssetTagScratch;
            replicatedModifierMagnitudeScratch = value.ReplicatedModifierMagnitudeScratch;
            modifierGroupAttributes = value.ModifierGroupAttributes;
            modifierGroupStarts = value.ModifierGroupStarts;
            modifierGroupIndices = value.ModifierGroupIndices;
        }

        internal void ReleaseUnacquiredBacking()
        {
            ReleaseBackingToOwner();
        }

        private void ReleaseBackingToOwner()
        {
            GameplayEffectSpecBacking released = backing;
            GASRuntimeMemory owner = memoryOwner;
            backing = null;
            memoryOwner = null;

            if (released == null)
            {
                ResetDetachedBackingReferences();
                return;
            }

            released.SetByCallerMagnitudes = setByCallerMagnitudes;
            released.SetByCallerMagnitudesByName = setByCallerMagnitudesByName;
            released.ModifierMagnitudes = modifierMagnitudes;
            released.ModifierMagnitudeRawValues = modifierMagnitudeRawValues;
            released.TargetAttributes = targetAttributes;
            released.CapturedAttributes = capturedAttributes;
            released.CapturedDeclaredAttributes = capturedDeclaredAttributes;
            released.ReplicatedStateTagScratch = replicatedStateTagScratch;
            released.ReplicatedStateValueScratch = replicatedStateValueScratch;
            released.ReplicatedStateNameScratch = replicatedStateNameScratch;
            released.ReplicatedStateNameValueScratch = replicatedStateNameValueScratch;
            released.ReplicatedDynamicGrantedTagScratch = replicatedDynamicGrantedTagScratch;
            released.ReplicatedDynamicAssetTagScratch = replicatedDynamicAssetTagScratch;
            released.ReplicatedModifierMagnitudeScratch = replicatedModifierMagnitudeScratch;
            released.ModifierGroupAttributes = modifierGroupAttributes;
            released.ModifierGroupStarts = modifierGroupStarts;
            released.ModifierGroupIndices = modifierGroupIndices;
            ResetDetachedBackingReferences();

            if (owner != null)
            {
                owner.ReleaseEffectSpecBacking(released);
            }
            else
            {
                released.ClearSensitiveData();
            }
        }

        private void ResetDetachedBackingReferences()
        {
            dynamicGrantedTags = null;
            dynamicAssetTags = null;
            setByCallerMagnitudes = null;
            setByCallerMagnitudesByName = null;
            modifierMagnitudes = Array.Empty<float>();
            modifierMagnitudeRawValues = Array.Empty<long>();
            targetAttributes = Array.Empty<GameplayAttribute>();
            capturedAttributes = Array.Empty<GameplayAttribute>();
            capturedDeclaredAttributes = Array.Empty<GameplayAttribute>();
            modifierGroupingDirty = true;
            dependencySourceAttributesResolved = false;
            replicatedStateTagScratch = Array.Empty<GameplayTag>();
            replicatedStateValueScratch = Array.Empty<long>();
            replicatedStateNameScratch = Array.Empty<string>();
            replicatedStateNameValueScratch = Array.Empty<long>();
            replicatedDynamicGrantedTagScratch = Array.Empty<GameplayTag>();
            replicatedDynamicAssetTagScratch = Array.Empty<GameplayTag>();
            replicatedModifierMagnitudeScratch = Array.Empty<long>();
            modifierGroupAttributes = Array.Empty<GameplayAttribute>();
            modifierGroupStarts = Array.Empty<int>();
            modifierGroupIndices = Array.Empty<int>();
        }

        #region Factory Methods

        /// <summary>
        /// Creates a caller-owned GameplayEffectSpec lease.
        /// </summary>
        public static GameplayEffectSpec Create(GameplayEffect def, AbilitySystemComponent source, int level = 1)
        {
            return Create(def, source, null, level);
        }

        /// <summary>
        /// Factory method that allows callers to provide a custom effect context.
        /// </summary>
        public static GameplayEffectSpec Create(GameplayEffect def, AbilitySystemComponent source, GameplayEffectContext context, int level = 1)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (level <= 0 || level > GASRuntimeDataContract.MaxGameplayLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(level),
                    level,
                    $"Effect level must be between 1 and {GASRuntimeDataContract.MaxGameplayLevel}.");
            }

            GameplayEffectSpec spec;
            if (source != null)
            {
                spec = source.RuntimeContext.Memory.AcquireEffectSpec();
            }
            else
            {
                spec = new GameplayEffectSpec();
                spec.AttachBacking(new GameplayEffectSpecBacking());
                if (!((IGASLeasedObject)spec).TryAcquireLease())
                {
                    throw new InvalidOperationException("New GameplayEffectSpec rejected its one-shot lease.");
                }
                ((IGASLeasedObject)spec).OnLeaseAcquired();
            }

            try
            {
                spec.Initialize(def, source, context, level);
                return spec;
            }
            catch
            {
                spec.ReleaseRuntimeLease();
                throw;
            }
        }

        private void Initialize(GameplayEffect def, AbilitySystemComponent source, GameplayEffectContext context, int level)
        {
            Def = def;
            Source = source;
            SourceEntity = source != null ? source.CoreEntity : default;
            Level = level;
            // A configured DurationMagnitude wins over the static def.Duration. Evaluation reuses
            // the fixed-point magnitude pipeline and travels through the same replicated
            // durationRaw channel.
            DurationRaw = def.DurationMagnitude != null
                ? CalculateDurationMagnitudeRawGuarded(def.DurationMagnitude, level)
                : GASFixedValue.FromFloat(def.Duration).RawValue;
            maxSetByCallerEntries = source?.Limits.MaxSetByCallerEntries ?? GASRuntimeLimits.Default.MaxSetByCallerEntries;

            Context = context ?? (source != null ? source.MakeEffectContext() : new GameplayEffectContext());
            if (source != null && Context.MemoryOwner != null &&
                !ReferenceEquals(Context.MemoryOwner, source.RuntimeContext.Memory))
            {
                throw new InvalidOperationException("GameplayEffectContext and source must belong to the same GASRuntimeContext memory owner.");
            }
            if (source != null)
            {
                if (Context.Instigator == null)
                {
                    Context.AddInstigator(source, null);
                }
                else if (!ReferenceEquals(Context.Instigator, source))
                {
                    throw new InvalidOperationException("GameplayEffectContext instigator must match the spec source AbilitySystemComponent.");
                }
            }
            Context.AttachToSpec(this);
            ownsContext = true;

            int modCount = def.Modifiers.Count;
            int maxModifiers = source?.Limits.MaxModifiersPerEffect ?? GASRuntimeLimits.Default.MaxModifiersPerEffect;
            if (modCount > maxModifiers)
            {
                throw new InvalidOperationException($"GameplayEffect '{def.Name}' defines {modCount} modifiers; the runtime limit is {maxModifiers}.");
            }

            modifierCount = modCount;
            EnsureCapacity(modCount);
            EnsureDeclaredCaptureCapacity(def.DeclaredCaptures.Count);

            for (int i = modCount; i < capturedAttributes.Length; i++)
            {
                capturedAttributes[i] = null;
            }
            for (int i = 0; i < capturedDeclaredAttributes.Length; i++)
            {
                capturedDeclaredAttributes[i] = null;
            }
            CaptureAttributeSources(EGameplayEffectAttributeCaptureSource.Source);

            for (int i = 0; i < modCount; i++)
            {
                var mod = def.Modifiers[i];
                StoreCalculatedMagnitudeRaw(i, CalculateMagnitudeRawGuarded(mod, level, i));
                targetAttributes[i] = null;
            }
            modifierGroupingDirty = true;
            dependencySourceAttributesResolved = false;
        }

        private void EnsureCapacity(int count)
        {
            if (count <= 0)
            {
                return;
            }

            // Arrays are checked independently: a pooled backing can supply a longer magnitude
            // array than this spec's own arrays, so a single gate can leave one array short.
            int newSize = System.Math.Max(
                count,
                modifierMagnitudes.Length == 0 ? 8 : modifierMagnitudes.Length * 2);

            if (modifierMagnitudes.Length < count)
            {
                System.Array.Resize(ref modifierMagnitudes, newSize);
            }
            if (modifierMagnitudeRawValues.Length < count)
            {
                System.Array.Resize(ref modifierMagnitudeRawValues, newSize);
            }
            if (targetAttributes.Length < count)
            {
                System.Array.Resize(ref targetAttributes, newSize);
            }
            if (capturedAttributes.Length < count)
            {
                System.Array.Resize(ref capturedAttributes, newSize);
            }
            if (dependencySourceAttributes.Length < count)
            {
                System.Array.Resize(ref dependencySourceAttributes, newSize);
            }
        }

        /// <summary>
        /// Binds one capture side at creation or application; later evaluation only reads attributes.
        /// </summary>
        private void CaptureAttributeSources(EGameplayEffectAttributeCaptureSource captureSide)
        {
            if (Def?.Modifiers == null)
            {
                return;
            }

            AbilitySystemComponent resolver = captureSide == EGameplayEffectAttributeCaptureSource.Source
                ? Source
                : Target;
            int count = System.Math.Min(modifierCount, Def.Modifiers.Count);
            for (int i = 0; i < count; i++)
            {
                ModifierInfo modifier = Def.Modifiers[i];
                if (modifier.MagnitudeCalculationType != EGameplayEffectMagnitudeCalculation.AttributeBased ||
                    modifier.AttributeBasedMagnitude.CaptureSource != captureSide)
                {
                    continue;
                }

                capturedAttributes[i] = resolver != null
                    ? resolver.GetAttribute(modifier.AttributeBasedMagnitude.AttributeName)
                    : null;
            }

            IReadOnlyList<GameplayAttributeCaptureDefinition> executionCaptures = Def.DeclaredCaptures;
            for (int i = 0; i < executionCaptures.Count; i++)
            {
                if (executionCaptures[i].CaptureSource != captureSide)
                {
                    continue;
                }

                capturedDeclaredAttributes[i] = resolver != null
                    ? resolver.GetAttribute(executionCaptures[i].AttributeName)
                    : null;
            }
        }

        /// <summary>Reads a binding declared by the effect definition; undeclared attributes return false.</summary>
        public bool TryGetCapturedAttribute(
            EGameplayEffectAttributeCaptureSource captureSource,
            string attributeName,
            out GameplayAttribute attribute)
        {
            int count = System.Math.Min(modifierCount, Def.Modifiers.Count);
            for (int i = 0; i < count; i++)
            {
                ModifierInfo modifier = Def.Modifiers[i];
                if (modifier.MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.AttributeBased &&
                    modifier.AttributeBasedMagnitude.CaptureSource == captureSource &&
                    string.Equals(modifier.AttributeBasedMagnitude.AttributeName, attributeName, System.StringComparison.Ordinal))
                {
                    attribute = capturedAttributes[i];
                    return attribute != null;
                }
            }

            IReadOnlyList<GameplayAttributeCaptureDefinition> executionCaptures = Def.DeclaredCaptures;
            for (int i = 0; i < executionCaptures.Count; i++)
            {
                if (executionCaptures[i].CaptureSource == captureSource &&
                    string.Equals(executionCaptures[i].AttributeName, attributeName, System.StringComparison.Ordinal))
                {
                    attribute = capturedDeclaredAttributes[i];
                    return attribute != null;
                }
            }

            attribute = null;
            return false;
        }

        private void EnsureDeclaredCaptureCapacity(int count)
        {
            if (capturedDeclaredAttributes.Length < count)
            {
                System.Array.Resize(ref capturedDeclaredAttributes, count);
            }
        }

        /// <summary>
        /// Returns the modifier indices targeting the given attribute in ascending modifier order,
        /// or 0 when no modifier targets it.
        /// </summary>
        internal int GetModifierIndicesForAttribute(GameplayAttribute attribute, out int[] indices, out int start)
        {
            if (modifierGroupingDirty)
            {
                RebuildModifierGrouping();
            }

            for (int group = 0; group < modifierGroupCount; group++)
            {
                if (ReferenceEquals(modifierGroupAttributes[group], attribute))
                {
                    indices = modifierGroupIndices;
                    start = modifierGroupStarts[group];
                    return modifierGroupStarts[group + 1] - start;
                }
            }

            indices = null;
            start = 0;
            return 0;
        }

        /// <summary>
        /// Returns the live-dependency source attribute resolved for the given modifier index
        /// (non-snapshot attribute-based magnitudes only), or null for modifiers without one.
        /// Resolved once per spec lease; used by the dependents scan as a reference comparison.
        /// </summary>
        internal GameplayAttribute GetDependencySourceAttribute(int modifierIndex)
        {
            if (!dependencySourceAttributesResolved)
            {
                ResolveDependencySourceAttributes();
            }

            return (uint)modifierIndex < (uint)modifierCount ? dependencySourceAttributes[modifierIndex] : null;
        }

        private void ResolveDependencySourceAttributes()
        {
            EnsureCapacity(modifierCount);
            for (int i = 0; i < modifierCount; i++)
            {
                if (i >= dependencySourceAttributes.Length)
                {
                    throw new System.InvalidOperationException(
                        $"GAS spec dependency array under-sized: modifierCount={modifierCount}, " +
                        $"dependencySourceAttributes={dependencySourceAttributes.Length}, " +
                        $"modifierMagnitudes={modifierMagnitudes.Length}, " +
                        $"def={(Def == null ? "<null>" : Def.Name)}, defModifiers={(Def == null || Def.Modifiers == null ? -1 : Def.Modifiers.Count)}");
                }

                dependencySourceAttributes[i] = null;
                ModifierInfo modifier = Def?.Modifiers?[i];
                if (modifier == null ||
                    modifier.MagnitudeCalculationType != EGameplayEffectMagnitudeCalculation.AttributeBased ||
                    modifier.SnapshotPolicy != EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                {
                    continue;
                }

                dependencySourceAttributes[i] = capturedAttributes[i];
            }

            dependencySourceAttributesResolved = true;
        }

        private void RebuildModifierGrouping()
        {
            EnsureModifierGroupingCapacity(modifierCount + 1);
            modifierGroupCount = 0;

            // Pass 1: count modifiers per distinct resolved target attribute.
            // modifierGroupStarts temporarily holds per-group hit counts.
            for (int i = 0; i < modifierCount; i++)
            {
                GameplayAttribute attribute = targetAttributes[i];
                if (attribute == null)
                {
                    // Unresolved modifiers never match a dirty attribute.
                    continue;
                }

                int group = FindModifierGroup(attribute);
                if (group < 0)
                {
                    group = modifierGroupCount++;
                    modifierGroupAttributes[group] = attribute;
                    modifierGroupStarts[group] = 0;
                }
                modifierGroupStarts[group]++;
            }

            // Convert counts to prefix-sum start offsets; the extra slot holds the total.
            int total = 0;
            for (int group = 0; group < modifierGroupCount; group++)
            {
                int count = modifierGroupStarts[group];
                modifierGroupStarts[group] = total;
                total += count;
            }
            modifierGroupStarts[modifierGroupCount] = total;

            // Pass 2: place modifier indices into their group slots, preserving ascending order.
            // modifierGroupStarts doubles as the fill cursor; after the fill each entry holds the
            // end of its group, and one right-shift restores the begin offsets.
            for (int i = 0; i < modifierCount; i++)
            {
                GameplayAttribute attribute = targetAttributes[i];
                if (attribute == null)
                {
                    continue;
                }

                int group = FindModifierGroup(attribute);
                modifierGroupIndices[modifierGroupStarts[group]++] = i;
            }

            for (int group = modifierGroupCount; group > 0; group--)
            {
                modifierGroupStarts[group] = modifierGroupStarts[group - 1];
            }
            modifierGroupStarts[0] = 0;

            modifierGroupingDirty = false;
        }

        private int FindModifierGroup(GameplayAttribute attribute)
        {
            for (int group = 0; group < modifierGroupCount; group++)
            {
                if (ReferenceEquals(modifierGroupAttributes[group], attribute))
                {
                    return group;
                }
            }
            return -1;
        }

        private void EnsureModifierGroupingCapacity(int required)
        {
            if (modifierGroupAttributes.Length < required)
            {
                int newSize = System.Math.Max(required, modifierGroupAttributes.Length == 0 ? 8 : modifierGroupAttributes.Length * 2);
                System.Array.Resize(ref modifierGroupAttributes, newSize);
                System.Array.Resize(ref modifierGroupStarts, newSize);
                System.Array.Resize(ref modifierGroupIndices, newSize);
            }
        }

        /// <summary>
        /// Discards a caller-owned spec that will not be submitted for application.
        /// </summary>
        public void Discard()
        {
            EnsureCallerOwned();
            ReleaseRuntimeLease();
        }

        internal bool TryDiscardCallerOwned()
        {
            memoryOwner?.AssertOwnerThread();
            if (ownership != SpecOwnership.Caller || externalEvaluationDepth != 0)
            {
                return false;
            }

            ReleaseRuntimeLease();
            return true;
        }

        internal void ReleaseRuntimeLease()
        {
            if (memoryOwner != null)
            {
                memoryOwner.ReleaseEffectSpec(this);
            }
            else if (((IGASLeasedObject)this).TryReleaseLease())
            {
                ((IGASLeasedObject)this).OnLeaseReleased();
            }
        }

        internal bool TryTransferToAbilitySystem()
        {
            if (ownership != SpecOwnership.Caller || externalEvaluationDepth != 0)
            {
                return false;
            }

            ValidateSetByCallerWarningsForSubmission();
            ownership = SpecOwnership.AbilitySystem;
            return true;
        }

        internal bool IsOwnedByAbilitySystem => ownership == SpecOwnership.AbilitySystem;
        internal bool IsExternalEvaluationInProgress => externalEvaluationDepth != 0;

        internal void TransferToActiveEffect()
        {
            if (ownership != SpecOwnership.Caller && ownership != SpecOwnership.AbilitySystem)
            {
                throw new InvalidOperationException("GameplayEffectSpec ownership cannot be transferred to an ActiveGameplayEffect from its current state.");
            }

            ValidateSetByCallerWarningsForSubmission();
            ownership = SpecOwnership.ActiveEffect;
        }

        internal void SetPredictionKeyFromOwner(GASPredictionKey predictionKey)
        {
            if (ownership != SpecOwnership.AbilitySystem && ownership != SpecOwnership.ActiveEffect)
            {
                throw new InvalidOperationException("Only the current GameplayEffectSpec owner can update its prediction key after submission.");
            }

            Context?.SetPredictionKeyFromSpec(this, predictionKey);
        }

        private void EnsureCallerOwned()
        {
            EnsurePublicReadAllowed();
            if (ownership != SpecOwnership.Caller || externalEvaluationDepth != 0)
            {
                throw new InvalidOperationException("GameplayEffectSpec mutation is only valid while the caller owns the spec and no extension callback is evaluating it.");
            }
        }

        private void EnsurePublicReadAllowed()
        {
            EnsureLeaseIsActive(leaseGeneration);
        }

        internal GameplayTagContainer ResolveDynamicTagContainer(
            ulong expectedLeaseGeneration,
            bool grantedTags,
            bool requireMutation)
        {
            EnsureLeaseIsActive(expectedLeaseGeneration);
            if (requireMutation)
            {
                EnsureCallerOwned();
            }

            return grantedTags ? dynamicGrantedTags : dynamicAssetTags;
        }

        private void EnsureLeaseIsActive(ulong expectedLeaseGeneration)
        {
            memoryOwner?.AssertOwnerThread();
            if (!leaseActive ||
                backing == null ||
                expectedLeaseGeneration == 0UL ||
                expectedLeaseGeneration != leaseGeneration)
            {
                throw new ObjectDisposedException(
                    nameof(GameplayEffectSpec),
                    "GameplayEffectSpec lease has been released.");
            }
        }

        /// <summary>
        /// Evaluates the declarative DurationMagnitude under the same external-evaluation and
        /// warning-suppression guards as modifier magnitudes. Negative results clamp to zero
        /// (the effect expires on its first tick); evaluation itself stays deterministic.
        /// </summary>
        private long CalculateDurationMagnitudeRawGuarded(DurationMagnitudeInfo info, int level)
        {
            BeginExternalEvaluation();
            setByCallerWarningSuppressionDepth++;
            try
            {
                long raw = info.CalculationType == EGameplayEffectMagnitudeCalculation.SetByCaller
                    ? CalculateSetByCallerMagnitudeRawWithoutWarning(info.SetByCaller)
                    : info.CalculateMagnitudeRaw(this, level);
                return raw < 0L ? 0L : raw;
            }
            finally
            {
                setByCallerWarningSuppressionDepth--;
                EndExternalEvaluation();
            }
        }

        private long CalculateMagnitudeRawGuarded(ModifierInfo modifier, int level, int modifierIndex)
        {
            BeginExternalEvaluation();
            setByCallerWarningSuppressionDepth++;
            try
            {
                if (modifier.MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.SetByCaller)
                {
                    return CalculateSetByCallerMagnitudeRawWithoutWarning(modifier.SetByCallerMagnitude);
                }

                GameplayAttribute capturedAttribute =
                    (uint)modifierIndex < (uint)capturedAttributes.Length ? capturedAttributes[modifierIndex] : null;
                return modifier.CalculateMagnitudeRaw(this, level, capturedAttribute);
            }
            finally
            {
                setByCallerWarningSuppressionDepth--;
                EndExternalEvaluation();
            }
        }

        private long CalculateSetByCallerMagnitudeRawWithoutWarning(SetByCallerMagnitude magnitude)
        {
            long defaultValueRaw = GASFixedValue.FromFloat(magnitude.DefaultValue).RawValue;
            if (!magnitude.DataTag.IsNone)
            {
                return GetSetByCallerMagnitudeRaw(
                    magnitude.DataTag,
                    warnIfNotFound: false,
                    defaultValueRaw: defaultValueRaw);
            }

            if (!string.IsNullOrEmpty(magnitude.DataName))
            {
                return GetSetByCallerMagnitudeRaw(
                    magnitude.DataName,
                    warnIfNotFound: false,
                    defaultValueRaw: defaultValueRaw);
            }

            return defaultValueRaw;
        }

        private void ValidateSetByCallerWarningsForSubmission()
        {
            if (setByCallerSubmissionWarningsValidated || definition?.Modifiers == null)
            {
                return;
            }

            int count = Math.Min(modifierCount, definition.Modifiers.Count);
            for (int i = 0; i < count; i++)
            {
                ModifierInfo modifier = definition.Modifiers[i];
                if (modifier == null ||
                    modifier.MagnitudeCalculationType != EGameplayEffectMagnitudeCalculation.SetByCaller ||
                    !modifier.SetByCallerMagnitude.WarnIfNotFound)
                {
                    continue;
                }

                modifier.SetByCallerMagnitude.CalculateMagnitudeRaw(this);
            }

            setByCallerSubmissionWarningsValidated = true;
        }

        internal void BeginExternalEvaluation()
        {
            externalEvaluationDepth++;
        }

        internal void EndExternalEvaluation()
        {
            if (externalEvaluationDepth <= 0)
            {
                throw new InvalidOperationException("GameplayEffectSpec extension evaluation scopes are unbalanced.");
            }

            externalEvaluationDepth--;
        }

        internal void EnsureDynamicTagCapacity<T>(GameplayTagContainer destination, in T source)
            where T : IReadOnlyGameplayTagContainer
        {
            if (source is null || source.IsEmpty)
            {
                return;
            }

            int additions = 0;
            GameplayTagEnumerator tags = source.GetExplicitTags();
            while (tags.MoveNext())
            {
                GameplayTag tag = tags.Current;
                if (!tag.IsNone && !destination.HasTagExact(tag))
                {
                    additions++;
                }
            }

            EnsureDynamicTagCapacity(additions);
        }

        internal void EnsureDynamicTagCapacity(GameplayTagContainer destination, GameplayTag tag)
        {
            if (!tag.IsNone && !destination.HasTagExact(tag))
            {
                EnsureDynamicTagCapacity(1);
            }
        }

        private void EnsureDynamicTagCapacity(int additions)
        {
            if (additions <= 0)
            {
                return;
            }

            int currentCount = dynamicGrantedTags.ExplicitTagCount + dynamicAssetTags.ExplicitTagCount;
            if (additions > GameplayEffect.MaxAggregateTagCount - currentCount)
            {
                throw new InvalidOperationException(
                    $"Dynamic effect tags exceed the aggregate limit of {GameplayEffect.MaxAggregateTagCount}.");
            }
        }

        #endregion

        #region SetByCaller API

        public void SetSetByCallerMagnitude(GameplayTag dataTag, float magnitude)
        {
            SetSetByCallerMagnitudeRaw(dataTag, GASFixedValue.FromFloat(magnitude).RawValue);
        }

        public void SetSetByCallerMagnitude(GameplayTag dataTag, GASFixedValue magnitude)
        {
            SetSetByCallerMagnitudeRaw(dataTag, magnitude.RawValue);
        }

        public void SetSetByCallerMagnitudeRaw(GameplayTag dataTag, long magnitudeRaw)
        {
            EnsureCallerOwned();
            ThrowIfEvaluatingReplicatedState();
            if (dataTag.IsNone) return;
            if ((setByCallerMagnitudes == null || !setByCallerMagnitudes.ContainsKey(dataTag)) &&
                SetByCallerMagnitudeCount >= maxSetByCallerEntries)
            {
                throw new InvalidOperationException($"GameplayEffectSpec exceeded the combined SetByCaller limit of {maxSetByCallerEntries}.");
            }
            GetOrCreateTagMagnitudes()[dataTag] = magnitudeRaw;
            RecalculateSetByCallerMagnitudes();
        }

        public float GetSetByCallerMagnitude(GameplayTag dataTag, bool warnIfNotFound = true, float defaultValue = 0f)
        {
            return GASFixedValue.FromRaw(GetSetByCallerMagnitudeRaw(
                dataTag,
                warnIfNotFound,
                GASFixedValue.FromFloat(defaultValue).RawValue)).ToFloat();
        }

        public long GetSetByCallerMagnitudeRaw(GameplayTag dataTag, bool warnIfNotFound = true, long defaultValueRaw = 0L)
        {
            EnsurePublicReadAllowed();
            if (evaluatingReplicatedState)
            {
                for (int i = 0; i < replicatedStateCount; i++)
                {
                    if (replicatedStateTagScratch[i].Equals(dataTag))
                    {
                        return replicatedStateValueScratch[i];
                    }
                }
            }

            if (setByCallerMagnitudes != null && setByCallerMagnitudes.TryGetValue(dataTag, out long magnitudeRaw))
            {
                return magnitudeRaw;
            }

            if (warnIfNotFound && setByCallerWarningSuppressionDepth == 0)
            {
                string tagName = dataTag.IsNone ? "<None>" : dataTag.Name;
                Log.Warning(
                    (TagName: tagName, EffectName: Def?.Name),
                    static (state, sb) => sb.Append("GetSetByCallerMagnitude: Tag '").Append(state.TagName)
                        .Append("' not found in spec for effect '").Append(state.EffectName).Append("'."));
            }
            return defaultValueRaw;
        }

        public void SetSetByCallerMagnitude(string dataName, float magnitude)
        {
            SetSetByCallerMagnitudeRaw(dataName, GASFixedValue.FromFloat(magnitude).RawValue);
        }

        public void SetSetByCallerMagnitude(string dataName, GASFixedValue magnitude)
        {
            SetSetByCallerMagnitudeRaw(dataName, magnitude.RawValue);
        }

        public void SetSetByCallerMagnitudeRaw(string dataName, long magnitudeRaw)
        {
            EnsureCallerOwned();
            ThrowIfEvaluatingReplicatedState();
            if (string.IsNullOrEmpty(dataName))
            {
                Log.Warning("SetSetByCallerMagnitude: dataName cannot be null or empty.");
                return;
            }
            if ((setByCallerMagnitudesByName == null || !setByCallerMagnitudesByName.ContainsKey(dataName)) &&
                SetByCallerMagnitudeCount >= maxSetByCallerEntries)
            {
                throw new InvalidOperationException($"GameplayEffectSpec exceeded the combined SetByCaller limit of {maxSetByCallerEntries}.");
            }
            GetOrCreateNameMagnitudes()[dataName] = magnitudeRaw;
            RecalculateSetByCallerMagnitudes();
        }

        public float GetSetByCallerMagnitude(string dataName, bool warnIfNotFound = true, float defaultValue = 0f)
        {
            return GASFixedValue.FromRaw(GetSetByCallerMagnitudeRaw(
                dataName,
                warnIfNotFound,
                GASFixedValue.FromFloat(defaultValue).RawValue)).ToFloat();
        }

        public long GetSetByCallerMagnitudeRaw(string dataName, bool warnIfNotFound = true, long defaultValueRaw = 0L)
        {
            EnsurePublicReadAllowed();
            if (evaluatingReplicatedState)
            {
                for (int i = 0; i < replicatedStateNameCount; i++)
                {
                    if (string.Equals(replicatedStateNameScratch[i], dataName, StringComparison.Ordinal))
                    {
                        return replicatedStateNameValueScratch[i];
                    }
                }

                return defaultValueRaw;
            }

            if (setByCallerMagnitudesByName != null && setByCallerMagnitudesByName.TryGetValue(dataName, out long magnitudeRaw))
            {
                return magnitudeRaw;
            }

            if (warnIfNotFound && setByCallerWarningSuppressionDepth == 0)
            {
                Log.Warning(
                    (DataName: dataName, EffectName: Def?.Name),
                    static (state, sb) => sb.Append("GetSetByCallerMagnitude: Name '").Append(state.DataName)
                        .Append("' not found in spec for effect '").Append(state.EffectName).Append("'."));
            }
            return defaultValueRaw;
        }

        public bool HasSetByCallerMagnitude(string dataName)
        {
            EnsurePublicReadAllowed();
            return !string.IsNullOrEmpty(dataName) && setByCallerMagnitudesByName != null && setByCallerMagnitudesByName.ContainsKey(dataName);
        }

        public bool HasSetByCallerMagnitude(GameplayTag dataTag)
        {
            EnsurePublicReadAllowed();
            return !dataTag.IsNone && setByCallerMagnitudes != null && setByCallerMagnitudes.ContainsKey(dataTag);
        }

        public int SetByCallerTagMagnitudeCount
        {
            get { EnsurePublicReadAllowed(); return setByCallerMagnitudes?.Count ?? 0; }
        }

        public int SetByCallerNameMagnitudeCount
        {
            get { EnsurePublicReadAllowed(); return setByCallerMagnitudesByName?.Count ?? 0; }
        }

        public int SetByCallerMagnitudeCount => SetByCallerTagMagnitudeCount + SetByCallerNameMagnitudeCount;

        public int CopySetByCallerTagMagnitudes(GameplayTag[] destinationTags, float[] destinationValues)
        {
            EnsurePublicReadAllowed();
            if (destinationTags == null || destinationValues == null || setByCallerMagnitudes == null || setByCallerMagnitudes.Count == 0)
            {
                return 0;
            }

            int capacity = System.Math.Min(destinationTags.Length, destinationValues.Length);
            if (capacity <= 0)
            {
                return 0;
            }

            int index = 0;
            foreach (var pair in setByCallerMagnitudes)
            {
                if (index >= capacity)
                {
                    break;
                }

                destinationTags[index] = pair.Key;
                destinationValues[index] = GASFixedValue.FromRaw(pair.Value).ToFloat();
                index++;
            }

            return index;
        }

        public int CopySetByCallerTagMagnitudesRaw(GameplayTag[] destinationTags, long[] destinationValuesRaw)
        {
            EnsurePublicReadAllowed();
            if (destinationTags == null || destinationValuesRaw == null || setByCallerMagnitudes == null || setByCallerMagnitudes.Count == 0)
            {
                return 0;
            }

            int capacity = System.Math.Min(destinationTags.Length, destinationValuesRaw.Length);
            if (capacity <= 0)
            {
                return 0;
            }

            int index = 0;
            foreach (var pair in setByCallerMagnitudes)
            {
                if (index >= capacity)
                {
                    break;
                }

                destinationTags[index] = pair.Key;
                destinationValuesRaw[index] = pair.Value;
                index++;
            }

            return index;
        }

        public int CopySetByCallerTagStateData(GASSetByCallerTagStateData[] destination)
        {
            EnsurePublicReadAllowed();
            if (destination == null || setByCallerMagnitudes == null || setByCallerMagnitudes.Count == 0)
            {
                return 0;
            }

            int index = 0;
            foreach (var pair in setByCallerMagnitudes)
            {
                if (index >= destination.Length)
                {
                    break;
                }

                destination[index++] = GASSetByCallerTagStateData.FromRaw(pair.Key, pair.Value);
            }

            return index;
        }

        public int CopySetByCallerNameMagnitudesRaw(string[] destinationNames, long[] destinationValuesRaw)
        {
            EnsurePublicReadAllowed();
            if (destinationNames == null ||
                destinationValuesRaw == null ||
                setByCallerMagnitudesByName == null ||
                setByCallerMagnitudesByName.Count == 0)
            {
                return 0;
            }

            int capacity = System.Math.Min(destinationNames.Length, destinationValuesRaw.Length);
            int index = 0;
            foreach (var pair in setByCallerMagnitudesByName)
            {
                if (index >= capacity)
                {
                    break;
                }

                destinationNames[index] = pair.Key;
                destinationValuesRaw[index] = pair.Value;
                index++;
            }

            return index;
        }

        public int CopySetByCallerNameStateData(GASSetByCallerNameStateData[] destination)
        {
            EnsurePublicReadAllowed();
            if (destination == null || setByCallerMagnitudesByName == null || setByCallerMagnitudesByName.Count == 0)
            {
                return 0;
            }

            int index = 0;
            foreach (var pair in setByCallerMagnitudesByName)
            {
                if (index >= destination.Length) break;
                destination[index++] = GASSetByCallerNameStateData.FromRaw(pair.Key, pair.Value);
            }
            return index;
        }

        internal void ApplyReplicatedState(
            int level,
            float duration,
            GameplayTag[] setByCallerTags,
            float[] setByCallerValues,
            int setByCallerCount)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "Effect duration must be finite and non-negative.");
            }

            ValidateReplicatedState(level, GASFixedValue.FromFloat(duration).RawValue, setByCallerTags, setByCallerValues, setByCallerCount);
            EnsureReplicatedStateScratchCapacity(setByCallerCount, 0, 0, 0, modifierCount);
            for (int i = 0; i < setByCallerCount; i++)
            {
                replicatedStateValueScratch[i] = GASFixedValue.FromFloat(setByCallerValues[i]).RawValue;
            }

            ApplyReplicatedStateRawCore(
                level,
                GASFixedValue.FromFloat(duration).RawValue,
                setByCallerTags,
                replicatedStateValueScratch,
                setByCallerCount,
                Array.Empty<string>(),
                Array.Empty<long>(),
                0,
                Array.Empty<GameplayTag>(),
                0,
                Array.Empty<GameplayTag>(),
                0);
        }

        internal void ApplyReplicatedStateRaw(
            int level,
            long durationRaw,
            GameplayTag[] setByCallerTags,
            long[] setByCallerValuesRaw,
            int setByCallerCount)
        {
            ValidateReplicatedState(level, durationRaw, setByCallerTags, setByCallerValuesRaw, setByCallerCount);
            ApplyReplicatedStateRawCore(
                level,
                durationRaw,
                setByCallerTags,
                setByCallerValuesRaw,
                setByCallerCount,
                Array.Empty<string>(),
                Array.Empty<long>(),
                0,
                Array.Empty<GameplayTag>(),
                0,
                Array.Empty<GameplayTag>(),
                0);
        }

        internal void ApplyReplicatedStateRaw(
            int level,
            long durationRaw,
            GameplayTag[] setByCallerTags,
            long[] setByCallerValuesRaw,
            int setByCallerCount,
            string[] setByCallerNames,
            long[] setByCallerNameValuesRaw,
            int setByCallerNameCount,
            GameplayTag[] dynamicGrantedTags,
            int dynamicGrantedTagCount,
            GameplayTag[] dynamicAssetTags,
            int dynamicAssetTagCount)
        {
            ValidateReplicatedState(level, durationRaw, setByCallerTags, setByCallerValuesRaw, setByCallerCount);
            ValidateExtendedReplicatedState(
                setByCallerCount,
                setByCallerNames,
                setByCallerNameValuesRaw,
                setByCallerNameCount,
                dynamicGrantedTags,
                dynamicGrantedTagCount,
                dynamicAssetTags,
                dynamicAssetTagCount);
            ApplyReplicatedStateRawCore(
                level,
                durationRaw,
                setByCallerTags,
                setByCallerValuesRaw,
                setByCallerCount,
                setByCallerNames,
                setByCallerNameValuesRaw,
                setByCallerNameCount,
                dynamicGrantedTags,
                dynamicGrantedTagCount,
                dynamicAssetTags,
                dynamicAssetTagCount);
        }

        private void ApplyReplicatedStateRawCore(
            int level,
            long durationRaw,
            GameplayTag[] setByCallerTags,
            long[] setByCallerValuesRaw,
            int setByCallerCount,
            string[] setByCallerNames,
            long[] setByCallerNameValuesRaw,
            int setByCallerNameCount,
            GameplayTag[] dynamicGrantedTags,
            int dynamicGrantedTagCount,
            GameplayTag[] dynamicAssetTags,
            int dynamicAssetTagCount)
        {
            EnsureReplicatedStateScratchCapacity(
                setByCallerCount,
                setByCallerNameCount,
                dynamicGrantedTagCount,
                dynamicAssetTagCount,
                modifierCount);
            for (int i = 0; i < setByCallerCount; i++)
            {
                GameplayTag tag = setByCallerTags[i];
                if (tag.IsNone || !tag.IsValid)
                {
                    throw new ArgumentException("Replicated SetByCaller entries require valid tags.", nameof(setByCallerTags));
                }

                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (replicatedStateTagScratch[earlier].Equals(tag))
                    {
                        throw new ArgumentException("Replicated SetByCaller entries cannot contain duplicate tags.", nameof(setByCallerTags));
                    }
                }

                replicatedStateTagScratch[i] = tag;
                if (!ReferenceEquals(setByCallerValuesRaw, replicatedStateValueScratch))
                {
                    replicatedStateValueScratch[i] = setByCallerValuesRaw[i];
                }
            }

            for (int i = 0; i < setByCallerNameCount; i++)
            {
                string name = setByCallerNames[i];
                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (string.Equals(replicatedStateNameScratch[earlier], name, StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Replicated SetByCaller entries cannot contain duplicate names.", nameof(setByCallerNames));
                    }
                }

                replicatedStateNameScratch[i] = name;
                replicatedStateNameValueScratch[i] = setByCallerNameValuesRaw[i];
            }

            CopyValidatedTags(dynamicGrantedTags, dynamicGrantedTagCount, replicatedDynamicGrantedTagScratch, nameof(dynamicGrantedTags));
            CopyValidatedTags(dynamicAssetTags, dynamicAssetTagCount, replicatedDynamicAssetTagScratch, nameof(dynamicAssetTags));

            int previousLevel = Level;
            long previousDurationRaw = DurationRaw;
            replicatedStateCount = setByCallerCount;
            replicatedStateNameCount = setByCallerNameCount;
            evaluatingReplicatedState = true;
            try
            {
                Level = level;
                DurationRaw = durationRaw;
                for (int i = 0; i < modifierCount; i++)
                {
                    replicatedModifierMagnitudeScratch[i] = CalculateMagnitudeRawGuarded(Def.Modifiers[i], level, i);
                }
            }
            catch
            {
                Level = previousLevel;
                DurationRaw = previousDurationRaw;
                replicatedStateCount = 0;
                replicatedStateNameCount = 0;
                throw;
            }
            finally
            {
                evaluatingReplicatedState = false;
            }

            setByCallerMagnitudes?.Clear();
            setByCallerMagnitudesByName?.Clear();
            for (int i = 0; i < setByCallerCount; i++)
            {
                GetOrCreateTagMagnitudes().Add(replicatedStateTagScratch[i], replicatedStateValueScratch[i]);
            }
            for (int i = 0; i < setByCallerNameCount; i++)
            {
                GetOrCreateNameMagnitudes().Add(replicatedStateNameScratch[i], replicatedStateNameValueScratch[i]);
            }

            this.dynamicGrantedTags.Clear();
            for (int i = 0; i < dynamicGrantedTagCount; i++)
            {
                this.dynamicGrantedTags.AddTag(replicatedDynamicGrantedTagScratch[i]);
            }
            this.dynamicAssetTags.Clear();
            for (int i = 0; i < dynamicAssetTagCount; i++)
            {
                this.dynamicAssetTags.AddTag(replicatedDynamicAssetTagScratch[i]);
            }

            for (int i = 0; i < modifierCount; i++)
            {
                StoreCalculatedMagnitudeRaw(i, replicatedModifierMagnitudeScratch[i]);
                if (Target != null)
                {
                    targetAttributes[i] = Target.GetAttribute(Def.Modifiers[i].AttributeName);
                }
            }
            modifierGroupingDirty = true;
            dependencySourceAttributesResolved = false;

            replicatedStateCount = 0;
            replicatedStateNameCount = 0;
        }

        private void EnsureReplicatedStateScratchCapacity(
            int setByCallerCount,
            int setByCallerNameCount,
            int dynamicGrantedTagCount,
            int dynamicAssetTagCount,
            int requiredModifierCount)
        {
            if (replicatedStateTagScratch.Length < setByCallerCount)
            {
                int newSize = System.Math.Max(setByCallerCount, replicatedStateTagScratch.Length == 0 ? 4 : replicatedStateTagScratch.Length * 2);
                System.Array.Resize(ref replicatedStateTagScratch, newSize);
                System.Array.Resize(ref replicatedStateValueScratch, newSize);
            }

            if (replicatedStateNameScratch.Length < setByCallerNameCount)
            {
                int newSize = System.Math.Max(setByCallerNameCount, replicatedStateNameScratch.Length == 0 ? 4 : replicatedStateNameScratch.Length * 2);
                System.Array.Resize(ref replicatedStateNameScratch, newSize);
                System.Array.Resize(ref replicatedStateNameValueScratch, newSize);
            }

            EnsureTagScratchCapacity(ref replicatedDynamicGrantedTagScratch, dynamicGrantedTagCount);
            EnsureTagScratchCapacity(ref replicatedDynamicAssetTagScratch, dynamicAssetTagCount);

            if (replicatedModifierMagnitudeScratch.Length < requiredModifierCount)
            {
                int newSize = System.Math.Max(requiredModifierCount, replicatedModifierMagnitudeScratch.Length == 0 ? 8 : replicatedModifierMagnitudeScratch.Length * 2);
                System.Array.Resize(ref replicatedModifierMagnitudeScratch, newSize);
            }
        }

        private static void EnsureTagScratchCapacity(ref GameplayTag[] scratch, int count)
        {
            if (scratch.Length >= count) return;
            int newSize = System.Math.Max(count, scratch.Length == 0 ? 4 : scratch.Length * 2);
            System.Array.Resize(ref scratch, newSize);
        }

        private static void CopyValidatedTags(
            GameplayTag[] source,
            int count,
            GameplayTag[] destination,
            string parameterName)
        {
            for (int i = 0; i < count; i++)
            {
                GameplayTag tag = source[i];
                if (tag.IsNone || !tag.IsValid)
                {
                    throw new ArgumentException("Replicated dynamic tag entries must be valid.", parameterName);
                }

                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (destination[earlier].Equals(tag))
                    {
                        throw new ArgumentException("Replicated dynamic tag entries cannot contain duplicates.", parameterName);
                    }
                }

                destination[i] = tag;
            }
        }

        private void ThrowIfEvaluatingReplicatedState()
        {
            if (evaluatingReplicatedState)
            {
                throw new InvalidOperationException("SetByCaller state cannot mutate while authoritative magnitudes are being evaluated.");
            }
        }

        private void ValidateReplicatedState<T>(int level, long durationRaw, GameplayTag[] tags, T[] values, int count)
        {
            if (level <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "Effect level must be greater than zero.");
            }

            if (durationRaw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationRaw), durationRaw, "Effect duration cannot be negative.");
            }

            if (count < 0 || count > maxSetByCallerEntries)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, $"SetByCaller count must be between zero and {maxSetByCallerEntries}.");
            }

            if (count > 0 && (tags == null || values == null || tags.Length < count || values.Length < count))
            {
                throw new ArgumentException("Replicated SetByCaller arrays must contain at least count entries.");
            }
        }

        private void ValidateExtendedReplicatedState(
            int setByCallerTagCount,
            string[] setByCallerNames,
            long[] setByCallerNameValuesRaw,
            int setByCallerNameCount,
            GameplayTag[] dynamicGrantedTags,
            int dynamicGrantedTagCount,
            GameplayTag[] dynamicAssetTags,
            int dynamicAssetTagCount)
        {
            if (setByCallerNameCount < 0 ||
                setByCallerNameCount > maxSetByCallerEntries - setByCallerTagCount ||
                (setByCallerNameCount > 0 &&
                 (setByCallerNames == null ||
                  setByCallerNameValuesRaw == null ||
                  setByCallerNames.Length < setByCallerNameCount ||
                  setByCallerNameValuesRaw.Length < setByCallerNameCount)))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(setByCallerNameCount),
                    setByCallerNameCount,
                    $"Combined SetByCaller count cannot exceed {maxSetByCallerEntries}.");
            }

            if (dynamicGrantedTagCount < 0 ||
                dynamicAssetTagCount < 0 ||
                dynamicGrantedTagCount > GameplayEffect.MaxAggregateTagCount ||
                dynamicAssetTagCount > GameplayEffect.MaxAggregateTagCount ||
                dynamicGrantedTagCount > GameplayEffect.MaxAggregateTagCount - dynamicAssetTagCount ||
                (dynamicGrantedTagCount > 0 &&
                 (dynamicGrantedTags == null || dynamicGrantedTags.Length < dynamicGrantedTagCount)) ||
                (dynamicAssetTagCount > 0 &&
                 (dynamicAssetTags == null || dynamicAssetTags.Length < dynamicAssetTagCount)))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dynamicGrantedTagCount),
                    "Replicated dynamic tag counts exceed their buffers or the aggregate definition budget.");
            }

            for (int i = 0; i < setByCallerNameCount; i++)
            {
                string name = setByCallerNames[i];
                if (string.IsNullOrWhiteSpace(name) || name.Length > GameplayEffect.MaxDefinitionStringLength)
                {
                    throw new ArgumentException("Replicated SetByCaller names must be non-empty and bounded.", nameof(setByCallerNames));
                }

                for (int characterIndex = 0; characterIndex < name.Length; characterIndex++)
                {
                    if (char.IsControl(name[characterIndex]))
                    {
                        throw new ArgumentException("Replicated SetByCaller names cannot contain control characters.", nameof(setByCallerNames));
                    }
                }
            }
        }

        #endregion

        #region Magnitude Lookup

        public float GetCalculatedMagnitude(ModifierInfo modifier)
        {
            EnsurePublicReadAllowed();
            if (definition == null || definition.Modifiers == null) return 0f;

            int index = -1;
            for (int i = 0; i < definition.Modifiers.Count; i++)
            {
                if (definition.Modifiers[i].Equals(modifier))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0 && index < modifierCount)
            {
                return modifierMagnitudes[index];
            }
            return 0f;
        }

        public long GetCalculatedMagnitudeRaw(int index)
        {
            EnsurePublicReadAllowed();
            if (index >= 0 && index < modifierCount)
            {
                return modifierMagnitudeRawValues[index];
            }
            return 0L;
        }

        public float GetCalculatedMagnitude(int index)
        {
            EnsurePublicReadAllowed();
            if (index >= 0 && index < modifierCount)
            {
                return modifierMagnitudes[index];
            }
            return 0f;
        }

        public void SetCalculatedMagnitude(int index, float magnitude)
        {
            EnsureCallerOwned();
            if (index < 0 || index >= modifierCount)
            {
                return;
            }

            StoreCalculatedMagnitudeRaw(index, GASFixedValue.FromFloat(magnitude).RawValue);
        }

        public void SetCalculatedMagnitudeRaw(int index, long magnitudeRaw)
        {
            EnsureCallerOwned();
            if (index < 0 || index >= modifierCount)
            {
                return;
            }

            StoreCalculatedMagnitudeRaw(index, magnitudeRaw);
        }

        /// <summary>
        /// Re-evaluates NotSnapshot attribute-based magnitudes. Periodic effects bypass continuous
        /// aggregation, so each execution refreshes them here.
        /// </summary>
        internal void RefreshLiveAttributeMagnitudes()
        {
            IReadOnlyList<ModifierInfo> modifiers = Def?.Modifiers;
            if (modifiers == null)
            {
                return;
            }

            int count = Math.Min(modifierCount, modifiers.Count);
            for (int i = 0; i < count; i++)
            {
                if (modifiers[i].ShouldRecalculateLiveMagnitude)
                {
                    RecalculateCalculatedMagnitude(i);
                }
            }
        }

        /// <summary>
        /// Re-captures Snapshot+Target magnitudes at application time: the target is unknown when
        /// the spec is created, while Snapshot+Source stays frozen at creation.
        /// </summary>
        internal void RefreshSnapshotTargetMagnitudes()
        {
            IReadOnlyList<ModifierInfo> modifiers = Def?.Modifiers;
            if (modifiers == null)
            {
                return;
            }

            int count = Math.Min(modifierCount, modifiers.Count);
            for (int i = 0; i < count; i++)
            {
                ModifierInfo modifier = modifiers[i];
                if (modifier.MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.AttributeBased &&
                    modifier.SnapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.Snapshot &&
                    modifier.AttributeBasedMagnitude.CaptureSource == EGameplayEffectAttributeCaptureSource.Target)
                {
                    RecalculateCalculatedMagnitude(i);
                }
            }
        }

        internal void RecalculateCalculatedMagnitude(int index)
        {
            if (Def == null || index < 0 || index >= modifierCount || index >= Def.Modifiers.Count)
            {
                return;
            }

            StoreCalculatedMagnitudeRaw(index, CalculateMagnitudeRawGuarded(Def.Modifiers[index], Level, index));
        }

        private void StoreCalculatedMagnitudeRaw(int index, long magnitudeRaw)
        {
            modifierMagnitudeRawValues[index] = magnitudeRaw;
            modifierMagnitudes[index] = GASFixedValue.FromRaw(magnitudeRaw).ToFloat();

            // Dynamic magnitudes are only known here. Reports a non-positive stacked multiplier once per
            // definition and leaves the resolved value untouched.
            if (magnitudeRaw <= 0L)
            {
                ReportNonPositiveStackedMagnitude(index, magnitudeRaw);
            }
        }

        private void ReportNonPositiveStackedMagnitude(int modifierIndex, long magnitudeRaw)
        {
            GameplayEffectStacking stacking = Def.Stacking;
            if (stacking.Type == EGameplayEffectStackingType.None || stacking.ResolveStackLimit() <= 1)
            {
                return;
            }

            ModifierInfo modifier = Def.Modifiers[modifierIndex];
            if (modifier.Operation != EAttributeModifierOperation.Multiply &&
                modifier.Operation != EAttributeModifierOperation.Division)
            {
                return;
            }

            if (!Def.TryClaimStackSignWarning())
            {
                return;
            }

            Log.Warning(
                (Name: Def.Name, Attribute: modifier.AttributeName, Magnitude: magnitudeRaw),
                static (state, sb) => sb.Append("GameplayEffect '")
                    .Append(state.Name)
                    .Append("' resolved a non-positive stacked magnitude for '")
                    .Append(state.Attribute)
                    .Append("' (raw ").Append(state.Magnitude)
                    .Append("). Stacking can drive the attribute to zero or negative; verify the dynamic magnitude source."));
        }

        #endregion

        /// <summary>
        /// Assigns the target AbilitySystemComponent and resolves attribute cache.
        /// </summary>
        internal void SetTarget(AbilitySystemComponent target)
        {
            if (externalEvaluationDepth != 0)
            {
                throw new InvalidOperationException("GameplayEffectSpec target assignment cannot re-enter an extension evaluation callback.");
            }

            if (target != null && Context?.MemoryOwner != null &&
                !ReferenceEquals(Context.MemoryOwner, target.RuntimeContext.Memory))
            {
                throw new InvalidOperationException("GameplayEffectContext and target must belong to the same GASRuntimeContext memory owner.");
            }

            if (target != null && Source != null && !ReferenceEquals(target.RuntimeContext, Source.RuntimeContext))
            {
                throw new InvalidOperationException("Source and target must belong to the same GASRuntimeContext.");
            }

            Target = target;
            if (Def != null && Def.Modifiers != null)
            {
                for (int i = 0; i < Def.Modifiers.Count; i++)
                {
                    if (i < targetAttributes.Length)
                    {
                        targetAttributes[i] = target != null ? target.GetAttribute(Def.Modifiers[i].AttributeName) : null;
                    }
                }
                modifierGroupingDirty = true;
                dependencySourceAttributesResolved = false;

                EnsureDeclaredCaptureCapacity(Def.DeclaredCaptures.Count);
                CaptureAttributeSources(EGameplayEffectAttributeCaptureSource.Target);
                RecalculateTargetDependentMagnitudes();
            }
        }

        private void RecalculateModifierMagnitudes()
        {
            if (Def == null)
            {
                return;
            }

            int modCount = Def.Modifiers.Count;
            EnsureCapacity(modCount);

            for (int i = 0; i < modCount; i++)
            {
                var mod = Def.Modifiers[i];
                StoreCalculatedMagnitudeRaw(i, CalculateMagnitudeRawGuarded(mod, Level, i));

                if (Target != null)
                {
                    targetAttributes[i] = Target.GetAttribute(mod.AttributeName);
                }
            }
            modifierGroupingDirty = true;
            dependencySourceAttributesResolved = false;
        }

        private void RecalculateTargetDependentMagnitudes()
        {
            if (Def == null)
            {
                return;
            }

            int modCount = Def.Modifiers.Count;
            EnsureCapacity(modCount);

            for (int i = 0; i < modCount; i++)
            {
                var mod = Def.Modifiers[i];
                if (mod.ShouldRecalculateWhenTargetAssigned)
                {
                    StoreCalculatedMagnitudeRaw(i, CalculateMagnitudeRawGuarded(mod, Level, i));
                }
            }
        }

        private void RecalculateSetByCallerMagnitudes()
        {
            if (Def == null)
            {
                return;
            }

            // A SetByCaller-sourced duration follows SetByCaller updates, unless the caller
            // explicitly overrode the duration via SetDuration*.
            if (!durationExplicitlyOverridden &&
                Def.DurationMagnitude != null &&
                Def.DurationMagnitude.CalculationType == EGameplayEffectMagnitudeCalculation.SetByCaller)
            {
                durationRaw = CalculateDurationMagnitudeRawGuarded(Def.DurationMagnitude, Level);
            }

            int modCount = Def.Modifiers.Count;
            EnsureCapacity(modCount);
            for (int i = 0; i < modCount; i++)
            {
                var mod = Def.Modifiers[i];
                if (mod.MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.SetByCaller)
                {
                    StoreCalculatedMagnitudeRaw(i, CalculateMagnitudeRawGuarded(mod, Level, i));
                }
            }
        }
    }
}
