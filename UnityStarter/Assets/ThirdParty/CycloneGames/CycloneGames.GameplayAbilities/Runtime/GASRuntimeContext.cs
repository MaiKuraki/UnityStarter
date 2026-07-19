using System;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Hard per-ASC safety limits. These bounds protect allocation and iteration work at untrusted boundaries.
    /// </summary>
    public sealed class GASRuntimeLimits
    {
        public static GASRuntimeLimits Default { get; } = new GASRuntimeLimits();

        public GASRuntimeLimits(
            int maxAttributeSets = 64,
            int maxAttributes = 1024,
            int maxGrantedAbilities = 1024,
            int maxActiveEffects = 4096,
            int maxPredictionWindows = 256,
            int maxTargetsPerTargetData = 256,
            int maxSetByCallerEntries = 256,
            int maxModifiersPerEffect = 128,
            int maxCoreModifiers = 16384,
            int maxPredictedAttributeChanges = 4096,
            int maxTagChangesPerDelta = 4096,
            int maxPeriodicEffectExecutionsPerTick = 8,
            int maxAbilityTaskRepeatExecutionsPerTick = 8)
        {
            MaxAttributeSets = ValidatePositive(maxAttributeSets, nameof(maxAttributeSets));
            MaxAttributes = ValidatePositive(maxAttributes, nameof(maxAttributes));
            MaxGrantedAbilities = ValidatePositive(maxGrantedAbilities, nameof(maxGrantedAbilities));
            MaxActiveEffects = ValidatePositive(maxActiveEffects, nameof(maxActiveEffects));
            MaxPredictionWindows = ValidatePositive(maxPredictionWindows, nameof(maxPredictionWindows));
            MaxTargetsPerTargetData = ValidatePositive(maxTargetsPerTargetData, nameof(maxTargetsPerTargetData));
            MaxSetByCallerEntries = ValidatePositive(maxSetByCallerEntries, nameof(maxSetByCallerEntries));
            MaxModifiersPerEffect = ValidatePositive(maxModifiersPerEffect, nameof(maxModifiersPerEffect));
            MaxCoreModifiers = ValidatePositive(maxCoreModifiers, nameof(maxCoreModifiers));
            MaxPredictedAttributeChanges = ValidatePositive(maxPredictedAttributeChanges, nameof(maxPredictedAttributeChanges));
            MaxTagChangesPerDelta = ValidatePositive(maxTagChangesPerDelta, nameof(maxTagChangesPerDelta));
            MaxPeriodicEffectExecutionsPerTick = ValidatePositive(maxPeriodicEffectExecutionsPerTick, nameof(maxPeriodicEffectExecutionsPerTick));
            MaxAbilityTaskRepeatExecutionsPerTick = ValidatePositive(maxAbilityTaskRepeatExecutionsPerTick, nameof(maxAbilityTaskRepeatExecutionsPerTick));
        }

        public int MaxAttributeSets { get; }
        public int MaxAttributes { get; }
        public int MaxGrantedAbilities { get; }
        public int MaxActiveEffects { get; }
        public int MaxPredictionWindows { get; }
        public int MaxTargetsPerTargetData { get; }
        public int MaxSetByCallerEntries { get; }
        public int MaxModifiersPerEffect { get; }
        public int MaxCoreModifiers { get; }
        public int MaxPredictedAttributeChanges { get; }
        public int MaxTagChangesPerDelta { get; }
        public int MaxPeriodicEffectExecutionsPerTick { get; }
        public int MaxAbilityTaskRepeatExecutionsPerTick { get; }

        private static int ValidatePositive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Limit must be greater than zero.");
            }

            return value;
        }
    }

    /// <summary>
    /// Bounded per-context storage policy for internal runtime backing data.
    /// Public runtime objects are never cached by this profile.
    /// </summary>
    public sealed class GASRuntimeCacheProfile
    {
        public const int MaxEffectSpecBackingCapacity = 4096;
        public static GASRuntimeCacheProfile Default { get; } = new GASRuntimeCacheProfile();

        public GASRuntimeCacheProfile(int effectSpecBackingCapacity = 64)
        {
            if (effectSpecBackingCapacity < 0 ||
                effectSpecBackingCapacity > MaxEffectSpecBackingCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(effectSpecBackingCapacity),
                    effectSpecBackingCapacity,
                    $"Effect spec backing capacity must be between 0 and {MaxEffectSpecBackingCapacity}.");
            }

            EffectSpecBackingCapacity = effectSpecBackingCapacity;
        }

        public int EffectSpecBackingCapacity { get; }
    }

    /// <summary>
    /// Lifetime statistics for the internal GameplayEffectSpec backing cache.
    /// </summary>
    public readonly struct GASRuntimeCacheStatistics
    {
        public GASRuntimeCacheStatistics(
            int retained,
            int capacity,
            long hits,
            long misses,
            long discards)
        {
            Retained = retained;
            Capacity = capacity;
            Hits = hits;
            Misses = misses;
            Discards = discards;
        }

        public int Retained { get; }
        public int Capacity { get; }
        public long Hits { get; }
        public long Misses { get; }
        public long Discards { get; }
    }

    /// <summary>
    /// Lifetime counters for one kind of one-shot runtime lease.
    /// </summary>
    public readonly struct GASRuntimeLeaseStatistics
    {
        public GASRuntimeLeaseStatistics(
            int active,
            int peakActive,
            long acquisitions,
            long invalidReleases,
            long releaseFailures)
        {
            Active = active;
            PeakActive = peakActive;
            Acquisitions = acquisitions;
            InvalidReleases = invalidReleases;
            ReleaseFailures = releaseFailures;
        }

        public int Active { get; }
        public int PeakActive { get; }
        public long Acquisitions { get; }
        public long InvalidReleases { get; }
        public long ReleaseFailures { get; }
    }

    /// <summary>
    /// Lifetime counters for public runtime objects owned by one context.
    /// Released objects are invalidated and discarded permanently.
    /// </summary>
    public readonly struct GASRuntimeMemoryStatistics
    {
        public GASRuntimeMemoryStatistics(
            GASRuntimeLeaseStatistics effectSpecs,
            GASRuntimeLeaseStatistics activeEffects,
            GASRuntimeLeaseStatistics effectContexts,
            GASRuntimeLeaseStatistics abilitySpecs,
            GASRuntimeLeaseStatistics tasks,
            GASRuntimeLeaseStatistics abilities,
            GASRuntimeLeaseStatistics targetData)
        {
            EffectSpecs = effectSpecs;
            ActiveEffects = activeEffects;
            EffectContexts = effectContexts;
            AbilitySpecs = abilitySpecs;
            Tasks = tasks;
            Abilities = abilities;
            TargetData = targetData;
        }

        public GASRuntimeLeaseStatistics EffectSpecs { get; }
        public GASRuntimeLeaseStatistics ActiveEffects { get; }
        public GASRuntimeLeaseStatistics EffectContexts { get; }
        public GASRuntimeLeaseStatistics AbilitySpecs { get; }
        public GASRuntimeLeaseStatistics Tasks { get; }
        public GASRuntimeLeaseStatistics Abilities { get; }
        public GASRuntimeLeaseStatistics TargetData { get; }

        public int OutstandingLeases =>
            EffectSpecs.Active +
            ActiveEffects.Active +
            EffectContexts.Active +
            AbilitySpecs.Active +
            Tasks.Active +
            Abilities.Active +
            TargetData.Active;
    }

    internal interface IGASLeasedObject
    {
        bool TryAcquireLease();
        bool TryReleaseLease();
        void OnLeaseAcquired();
        void OnLeaseReleased();
    }

    internal sealed class GASLeaseCounter
    {
        private int active;
        private int peakActive;
        private long acquisitions;
        private long invalidReleases;
        private long releaseFailures;

        public void RecordAcquisition()
        {
            acquisitions++;
            active++;
            if (active > peakActive)
            {
                peakActive = active;
            }
        }

        public void RecordRelease()
        {
            if (active > 0)
            {
                active--;
            }
        }

        public void RecordInvalidRelease()
        {
            invalidReleases++;
        }

        public void RecordReleaseFailure()
        {
            releaseFailures++;
        }

        public GASRuntimeLeaseStatistics GetStatistics()
        {
            return new GASRuntimeLeaseStatistics(
                active,
                peakActive,
                acquisitions,
                invalidReleases,
                releaseFailures);
        }
    }

    /// <summary>
    /// Owns lifetime accounting for public runtime objects in one simulation context.
    /// Public objects receive one lease only because their raw references can escape consumer callbacks.
    /// The owner thread is enforced by <see cref="GASRuntimeContext"/>; this type intentionally contains no locks.
    /// </summary>
    internal sealed class GASRuntimeMemory
    {
        private readonly Action assertOwnerThread;
        private readonly GASLeaseCounter effectSpecs = new GASLeaseCounter();
        private readonly GASLeaseCounter activeEffects = new GASLeaseCounter();
        private readonly GASLeaseCounter effectContexts = new GASLeaseCounter();
        private readonly GASLeaseCounter abilitySpecs = new GASLeaseCounter();
        private readonly GASLeaseCounter tasks = new GASLeaseCounter();
        private readonly GASLeaseCounter abilities = new GASLeaseCounter();
        private readonly GASLeaseCounter targetData = new GASLeaseCounter();
        private readonly GameplayEffectSpecBacking[] effectSpecBackingCache;
        private int retainedEffectSpecBackingCount;
        private long effectSpecBackingHits;
        private long effectSpecBackingMisses;
        private long effectSpecBackingDiscards;
        private bool disposed;

        public GASRuntimeMemory(Action assertOwnerThread, GASRuntimeCacheProfile cacheProfile)
        {
            this.assertOwnerThread = assertOwnerThread;
            if (cacheProfile == null) throw new ArgumentNullException(nameof(cacheProfile));
            effectSpecBackingCache = cacheProfile.EffectSpecBackingCapacity == 0
                ? Array.Empty<GameplayEffectSpecBacking>()
                : new GameplayEffectSpecBacking[cacheProfile.EffectSpecBackingCapacity];
        }

        public bool IsDisposed => disposed;

        internal GameplayEffectSpec AcquireEffectSpec()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            var item = new GameplayEffectSpec();
            item.SetMemoryOwner(this);
            item.AttachBacking(AcquireEffectSpecBacking());
            bool leaseAcquired = false;
            try
            {
                var leased = (IGASLeasedObject)item;
                leaseAcquired = leased.TryAcquireLease();
                if (!leaseAcquired)
                {
                    throw new InvalidOperationException("New GameplayEffectSpec rejected its first lease.");
                }

                leased.OnLeaseAcquired();
                effectSpecs.RecordAcquisition();
                return item;
            }
            catch
            {
                try
                {
                    var leased = (IGASLeasedObject)item;
                    if (leaseAcquired && leased.TryReleaseLease())
                    {
                        leased.OnLeaseReleased();
                    }
                    else
                    {
                        item.ReleaseUnacquiredBacking();
                    }
                }
                catch
                {
                    // Preserve the acquisition failure. The failed object is unreachable and will be collected.
                }

                throw;
            }
        }

        internal bool ReleaseEffectSpec(GameplayEffectSpec item)
        {
            AssertOwnerThread();
            return Release(item, effectSpecs);
        }

        internal void ReleaseEffectSpecBacking(GameplayEffectSpecBacking backing)
        {
            AssertOwnerThread();
            if (backing == null)
            {
                return;
            }

            try
            {
                backing.ClearSensitiveData();
            }
            catch
            {
                effectSpecBackingDiscards++;
                throw;
            }

            if (disposed || retainedEffectSpecBackingCount >= effectSpecBackingCache.Length)
            {
                effectSpecBackingDiscards++;
                return;
            }

            effectSpecBackingCache[retainedEffectSpecBackingCount++] = backing;
        }

        internal ActiveGameplayEffect AcquireActiveEffect()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            ActiveGameplayEffect item = Acquire<ActiveGameplayEffect>(activeEffects);
            item.SetMemoryOwner(this);
            return item;
        }

        internal bool ReleaseActiveEffect(ActiveGameplayEffect item)
        {
            AssertOwnerThread();
            return Release(item, activeEffects);
        }

        internal GameplayEffectContext AcquireEffectContext()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            GameplayEffectContext item = Acquire<GameplayEffectContext>(effectContexts);
            item.SetMemoryOwner(this);
            return item;
        }

        internal bool ReleaseEffectContext(GameplayEffectContext item)
        {
            AssertOwnerThread();
            return Release(item, effectContexts);
        }

        internal GameplayAbilitySpec AcquireAbilitySpec()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            GameplayAbilitySpec item = Acquire<GameplayAbilitySpec>(abilitySpecs);
            item.SetMemoryOwner(this);
            return item;
        }

        internal bool ReleaseAbilitySpec(GameplayAbilitySpec item)
        {
            AssertOwnerThread();
            return Release(item, abilitySpecs);
        }

        internal T AcquireTask<T>() where T : AbilityTask, new()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            T task = new T();
            task.MarkLeaseAcquired(this);
            tasks.RecordAcquisition();
            return task;
        }

        internal void ReleaseTask(AbilityTask task, bool releaseSucceeded = true)
        {
            AssertOwnerThread();
            if (task == null || !task.TryReleaseLease())
            {
                tasks.RecordInvalidRelease();
                return;
            }

            tasks.RecordRelease();
            if (!releaseSucceeded)
            {
                tasks.RecordReleaseFailure();
            }
        }

        internal GameplayAbility AcquireAbility(GameplayAbility template)
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            Type type = template.GetType();
            GameplayAbility ability = template.CreateRuntimeInstance();
            if (ability == null || ReferenceEquals(ability, template) || ability.GetType() != type)
            {
                throw new InvalidOperationException(
                    $"{type.FullName}.CreateRuntimeInstance() must return a distinct instance of the same runtime type.");
            }

            ability.CopyConfigurationFrom(template);
            ability.MarkLeaseAcquired(this, template);
            abilities.RecordAcquisition();
            return ability;
        }

        internal void ReleaseAbility(GameplayAbility ability)
        {
            AssertOwnerThread();
            if (ability == null || !ability.TryReleaseLease())
            {
                abilities.RecordInvalidRelease();
                return;
            }

            abilities.RecordRelease();
            try
            {
                ability.OnRuntimeInstanceReleased();
            }
            catch
            {
                abilities.RecordReleaseFailure();
                throw;
            }
        }

        internal T AcquireTargetData<T>(int maxTargets) where T : TargetData, new()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            T data = new T();
            data.MarkLeaseAcquired(this, maxTargets);
            targetData.RecordAcquisition();
            return data;
        }

        internal void ReleaseTargetData(TargetData data)
        {
            AssertOwnerThread();
            if (data == null || !data.TryReleaseLease())
            {
                targetData.RecordInvalidRelease();
                return;
            }

            targetData.RecordRelease();
            try
            {
                data.ResetRuntimeState();
            }
            catch
            {
                targetData.RecordReleaseFailure();
                throw;
            }
        }

        public GASRuntimeMemoryStatistics GetStatistics()
        {
            AssertOwnerThread();
            return new GASRuntimeMemoryStatistics(
                effectSpecs.GetStatistics(),
                activeEffects.GetStatistics(),
                effectContexts.GetStatistics(),
                abilitySpecs.GetStatistics(),
                tasks.GetStatistics(),
                abilities.GetStatistics(),
                targetData.GetStatistics());
        }

        internal GASRuntimeCacheStatistics GetCacheStatistics()
        {
            AssertOwnerThread();
            return new GASRuntimeCacheStatistics(
                retainedEffectSpecBackingCount,
                effectSpecBackingCache.Length,
                effectSpecBackingHits,
                effectSpecBackingMisses,
                effectSpecBackingDiscards);
        }

        internal void TrimCaches()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            TrimCachesCore();
        }

        internal void Dispose()
        {
            if (disposed)
            {
                return;
            }

            AssertOwnerThread();
            TrimCachesCore();
            disposed = true;
        }

        internal void AssertOwnerThread()
        {
            assertOwnerThread?.Invoke();
        }

        private T Acquire<T>(GASLeaseCounter counter) where T : class, IGASLeasedObject, new()
        {
            T item = new T();
            if (!item.TryAcquireLease())
            {
                throw new InvalidOperationException($"New runtime instance of {typeof(T).FullName} rejected its first lease.");
            }

            try
            {
                item.OnLeaseAcquired();
            }
            catch
            {
                item.TryReleaseLease();
                throw;
            }

            counter.RecordAcquisition();
            return item;
        }

        private GameplayEffectSpecBacking AcquireEffectSpecBacking()
        {
            if (retainedEffectSpecBackingCount == 0)
            {
                effectSpecBackingMisses++;
                return new GameplayEffectSpecBacking();
            }

            int index = --retainedEffectSpecBackingCount;
            GameplayEffectSpecBacking backing = effectSpecBackingCache[index];
            effectSpecBackingCache[index] = null;
            effectSpecBackingHits++;
            return backing;
        }

        private void TrimCachesCore()
        {
            if (retainedEffectSpecBackingCount == 0)
            {
                return;
            }

            effectSpecBackingDiscards += retainedEffectSpecBackingCount;
            Array.Clear(effectSpecBackingCache, 0, retainedEffectSpecBackingCount);
            retainedEffectSpecBackingCount = 0;
        }

        private static bool Release<T>(T item, GASLeaseCounter counter) where T : class, IGASLeasedObject
        {
            if (item == null || !item.TryReleaseLease())
            {
                counter.RecordInvalidRelease();
                return false;
            }

            counter.RecordRelease();
            try
            {
                item.OnLeaseReleased();
                return true;
            }
            catch
            {
                counter.RecordReleaseFailure();
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GASRuntimeMemory));
            }
        }
    }

    public enum GASRuntimeAuthorityMode : byte
    {
        Invalid = 0,
        Authority = 1,
        Replica = 2
    }

    /// <summary>
    /// Explicit composition root shared by AbilitySystemComponent instances in one simulation world or partition.
    /// </summary>
    public sealed class GASRuntimeContext : IDisposable
    {
        private readonly int ownerThreadId;
        private int nextEntityId;
        private int registeredAbilitySystems;
        private bool disposed;

        public GASRuntimeContext(
            GASRuntimeAuthorityMode authorityMode = GASRuntimeAuthorityMode.Authority,
            IGASDefinitionRegistry definitionRegistry = null,
            IGASAttributeRegistry attributeRegistry = null,
            IGameplayCueManager cueManager = null,
            GASRuntimeThreadPolicy threadPolicy = GASRuntimeThreadPolicy.Throw,
            GASRuntimeCacheProfile cacheProfile = null)
        {
            if (authorityMode != GASRuntimeAuthorityMode.Authority &&
                authorityMode != GASRuntimeAuthorityMode.Replica)
            {
                throw new ArgumentOutOfRangeException(nameof(authorityMode));
            }

            AuthorityMode = authorityMode;
            DefinitionRegistry = definitionRegistry ?? new GASDefaultDefinitionRegistry();
            AttributeRegistry = attributeRegistry ?? new GASDefaultAttributeRegistry();
            CueManager = cueManager ?? NullGameplayCueManager.Instance;
            ThreadPolicy = threadPolicy;
            CacheProfile = cacheProfile ?? GASRuntimeCacheProfile.Default;
            ownerThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Memory = new GASRuntimeMemory(
                threadPolicy == GASRuntimeThreadPolicy.Disabled ? null : AssertMemoryOwnerThread,
                CacheProfile);
        }

        public GASRuntimeAuthorityMode AuthorityMode { get; }
        public bool HasAuthority => AuthorityMode == GASRuntimeAuthorityMode.Authority;
        public IGASDefinitionRegistry DefinitionRegistry { get; }
        public IGASAttributeRegistry AttributeRegistry { get; }
        public IGameplayCueManager CueManager { get; }
        internal GASRuntimeMemory Memory { get; }
        public GASRuntimeThreadPolicy ThreadPolicy { get; }
        public GASRuntimeCacheProfile CacheProfile { get; }
        public int OwnerThreadId => ownerThreadId;
        public int RegisteredAbilitySystemCount => registeredAbilitySystems;
        public bool IsDisposed => disposed;

        public GASRuntimeMemoryStatistics GetMemoryStatistics()
        {
            ThrowIfDisposed();
            return Memory.GetStatistics();
        }

        public GASRuntimeCacheStatistics GetCacheStatistics()
        {
            ThrowIfDisposed();
            return Memory.GetCacheStatistics();
        }

        public void TrimCaches()
        {
            ThrowIfDisposed();
            Memory.TrimCaches();
        }

        internal GASEntityId RegisterAbilitySystem()
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            if (nextEntityId == int.MaxValue)
            {
                throw new InvalidOperationException("The GAS runtime context exhausted its entity ID space. Create a new session context.");
            }

            nextEntityId++;
            registeredAbilitySystems++;
            return new GASEntityId(nextEntityId);
        }

        internal void UnregisterAbilitySystem()
        {
            AssertOwnerThread();
            if (registeredAbilitySystems > 0)
            {
                registeredAbilitySystems--;
            }
        }

        internal void AssertOwnerThread()
        {
            if (ThreadPolicy == GASRuntimeThreadPolicy.Disabled)
            {
                return;
            }

            int current = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (current == ownerThreadId)
            {
                return;
            }

            if (ThreadPolicy == GASRuntimeThreadPolicy.LogWarning)
            {
                GASLog.Warning(sb => sb.Append("GASRuntimeContext rejected access from thread ")
                    .Append(current)
                    .Append("; owner thread is ")
                    .Append(ownerThreadId)
                    .Append('.'));
            }

            throw new InvalidOperationException($"GASRuntimeContext is owned by thread {ownerThreadId} but was accessed from thread {current}.");
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            AssertMemoryOwnerThread();
            if (registeredAbilitySystems != 0)
            {
                throw new InvalidOperationException($"Cannot dispose GASRuntimeContext while {registeredAbilitySystems} AbilitySystemComponent instance(s) are registered.");
            }

            disposed = true;
            Memory.Dispose();
        }

        private void AssertMemoryOwnerThread()
        {
            if (ThreadPolicy == GASRuntimeThreadPolicy.Disabled)
            {
                return;
            }

            int current = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (current == ownerThreadId)
            {
                return;
            }

            if (ThreadPolicy == GASRuntimeThreadPolicy.LogWarning)
            {
                GASLog.Warning(sb => sb.Append("GASRuntimeContext memory is owned by thread ")
                    .Append(ownerThreadId)
                    .Append(" but was accessed from thread ")
                    .Append(current)
                    .Append('.'));
            }

            throw new InvalidOperationException($"GASRuntimeContext memory is owned by thread {ownerThreadId} but was accessed from thread {current}.");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GASRuntimeContext));
            }
        }
    }
}
