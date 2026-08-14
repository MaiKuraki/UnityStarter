using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Selects the Unity PlayerLoop phase used by an Actor's primary Tick.
    /// An Actor participates in at most one phase.
    /// </summary>
    public enum ActorTickPhase : byte
    {
        None = 0,
        Update = 1,
        FixedUpdate = 2,
        LateUpdate = 3,
    }

    public enum ActorLifecycleState : byte
    {
        Constructed = 0,
        Initialized = 1,
        Playing = 2,
        Ending = 3,
        Ended = 4,
        Destroyed = 5,
    }

    public delegate void DamageEventHandler(
        float damage,
        in DamageEvent damageEvent,
        Controller eventInstigator,
        Actor damageCauser);

    /// <summary>
    /// Unity-facing gameplay object. Actor provides world membership, lifecycle, transform,
    /// lightweight tags, visibility, and damage hooks. Network migration and persistence are
    /// integration responsibilities.
    /// </summary>
    [DisallowMultipleComponent]
    public class Actor : MonoBehaviour
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        [SerializeField] private float initialLifeSpanSec;
        [SerializeField] private bool bCanBeDamaged = true;
        [SerializeField] private bool bHidden;
        [SerializeField, ActorTag] private List<string> tags;
        [SerializeField] private ActorTickPhase PrimaryTickPhase = ActorTickPhase.None;
        [SerializeField] private bool StartWithTickEnabled = true;

        private Actor owner;
        private Actor instigator;
        private World world;
        private List<Renderer> rendererBuffer;
        private CancellationTokenSource lifeSpanCancellation;
        private double lifeSpanDeadline;
        private bool lifeSpanCancellationCommitted;
        private bool lifeSpanCleanupInProgress;
        private int actorOwnerThreadId;
        private ActorLifecycleState lifecycleState = ActorLifecycleState.Constructed;
        private EndPlayReason endPlayReason;
        private int stableInstanceId;
        private bool worldUnboundNotified;
        private bool actorTickEnabled;
        private bool actorTickStateInitialized;
        private Action<Actor>[] destroyedObservers = Array.Empty<Action<Actor>>();
        private Action[] ownerChangedObservers = Array.Empty<Action>();
        private DamageEventHandler[] pointDamageObservers = Array.Empty<DamageEventHandler>();
        private DamageEventHandler[] radialDamageObservers = Array.Empty<DamageEventHandler>();

        public event Action<Actor> OnDestroyed
        {
            add
            {
                AssertActorOwnerThread();
                destroyedObservers = AddDestroyedObservers(destroyedObservers, value);
            }
            remove
            {
                AssertActorOwnerThread();
                destroyedObservers = RemoveDestroyedObservers(destroyedObservers, value);
            }
        }

        public event Action OwnerChanged
        {
            add
            {
                AssertActorOwnerThread();
                ownerChangedObservers = AddActionObservers(ownerChangedObservers, value);
            }
            remove
            {
                AssertActorOwnerThread();
                ownerChangedObservers = RemoveActionObservers(ownerChangedObservers, value);
            }
        }

        public event DamageEventHandler OnTakePointDamage
        {
            add
            {
                AssertActorOwnerThread();
                pointDamageObservers = AddDamageObservers(pointDamageObservers, value);
            }
            remove
            {
                AssertActorOwnerThread();
                pointDamageObservers = RemoveDamageObservers(pointDamageObservers, value);
            }
        }

        public event DamageEventHandler OnTakeRadialDamage
        {
            add
            {
                AssertActorOwnerThread();
                radialDamageObservers = AddDamageObservers(radialDamageObservers, value);
            }
            remove
            {
                AssertActorOwnerThread();
                radialDamageObservers = RemoveDamageObservers(radialDamageObservers, value);
            }
        }

        public World World
        {
            get
            {
                AssertActorOwnerThread();
                return world;
            }
        }

        public ActorLifecycleState LifecycleState
        {
            get
            {
                AssertActorOwnerThread();
                return lifecycleState;
            }
        }

        public bool HasBegunPlay
        {
            get
            {
                AssertActorOwnerThread();
                return lifecycleState == ActorLifecycleState.Playing;
            }
        }

        public bool CanEverTick
        {
            get
            {
                AssertActorOwnerThread();
                return PrimaryTickPhase != ActorTickPhase.None;
            }
        }

        public ActorTickPhase TickPhase
        {
            get
            {
                AssertActorOwnerThread();
                return PrimaryTickPhase;
            }
        }

        public bool IsTickEnabledAtStart
        {
            get
            {
                AssertActorOwnerThread();
                return StartWithTickEnabled;
            }
        }

        public World GetWorld()
        {
            AssertActorOwnerThread();
            return world;
        }

        public GameInstance GetGameInstance()
        {
            AssertActorOwnerThread();
            return world?.GetGameInstance();
        }

        public GameMode GetAuthGameMode()
        {
            AssertActorOwnerThread();
            return world?.GetAuthGameMode();
        }

        public T GetAuthGameMode<T>() where T : GameMode
        {
            AssertActorOwnerThread();
            return world?.GetAuthGameMode<T>();
        }

        public GameState GetGameState()
        {
            AssertActorOwnerThread();
            return world?.GetGameState();
        }

        public T GetGameState<T>() where T : GameState
        {
            AssertActorOwnerThread();
            return world?.GetGameState<T>();
        }

        /// <summary>
        /// Returns the Unity instance identifier captured while the Actor is alive. Destruction
        /// bookkeeping uses the cached value because native Unity object access is no longer
        /// valid once OnDestroy has begun.
        /// </summary>
        internal int GetStableInstanceId()
        {
            if (stableInstanceId == 0)
            {
                stableInstanceId = GetInstanceID();
            }

            return stableInstanceId;
        }

        #region Primary tick
        /// <summary>
        /// Returns whether this Actor's primary Tick is enabled. World lifecycle and component
        /// activity are additional dispatch gates.
        /// </summary>
        public bool IsActorTickEnabled()
        {
            AssertActorOwnerThread();
            return CanEverTick && actorTickEnabled;
        }

        /// <summary>
        /// Enables or disables this Actor's primary Tick. Enabling requires a configured phase.
        /// </summary>
        public void SetActorTickEnabled(bool enabled)
        {
            AssertActorOwnerThread();
            if (enabled && !CanEverTick)
            {
                throw new InvalidOperationException(
                    "Actor Tick cannot be enabled while TickPhase is None. Configure a phase first.");
            }

            bool previousEnabled = IsActorTickEnabled();
            actorTickStateInitialized = true;
            actorTickEnabled = enabled;
            bool nextEnabled = IsActorTickEnabled();
            if (previousEnabled != nextEnabled)
            {
                world?.NotifyActorTickConfigurationChanged(
                    this,
                    PrimaryTickPhase,
                    previousEnabled,
                    PrimaryTickPhase,
                    nextEnabled);
            }
        }

        /// <summary>
        /// Changes the primary Tick phase. Selecting None disables Tick immediately.
        /// </summary>
        public void SetActorTickPhase(ActorTickPhase phase)
        {
            AssertActorOwnerThread();
            ValidateTickPhase(phase);
            if (PrimaryTickPhase == phase)
            {
                return;
            }

            ActorTickPhase previousPhase = PrimaryTickPhase;
            bool previousEnabled = IsActorTickEnabled();
            PrimaryTickPhase = phase;
            actorTickStateInitialized = true;
            if (phase == ActorTickPhase.None)
            {
                actorTickEnabled = false;
            }

            world?.NotifyActorTickConfigurationChanged(
                this,
                previousPhase,
                previousEnabled,
                phase,
                IsActorTickEnabled());
        }

        /// <summary>
        /// Establishes code-owned Tick defaults for a specialized Actor type.
        /// Call from Awake after base.Awake().
        /// </summary>
        protected void ConfigureActorTick(ActorTickPhase phase, bool startWithTickEnabled)
        {
            ValidateTickPhase(phase);
            AssertActorOwnerThread();

            ActorTickPhase previousPhase = PrimaryTickPhase;
            bool previousEnabled = IsActorTickEnabled();
            PrimaryTickPhase = phase;
            StartWithTickEnabled = startWithTickEnabled;
            actorTickEnabled = phase != ActorTickPhase.None && startWithTickEnabled;
            actorTickStateInitialized = true;

            bool nextEnabled = IsActorTickEnabled();
            if (previousPhase != phase || previousEnabled != nextEnabled)
            {
                world?.NotifyActorTickConfigurationChanged(
                    this,
                    previousPhase,
                    previousEnabled,
                    phase,
                    nextEnabled);
            }
        }

        /// <summary>
        /// Per-frame gameplay hook dispatched by World in the configured phase.
        /// </summary>
        protected virtual void Tick(float deltaSeconds) { }

        internal void DispatchTick(float deltaSeconds)
        {
            Tick(deltaSeconds);
        }

        private void InitializeActorTickState()
        {
            if (actorTickStateInitialized)
            {
                return;
            }

            actorTickEnabled = PrimaryTickPhase != ActorTickPhase.None && StartWithTickEnabled;
            actorTickStateInitialized = true;
        }

        private static void ValidateTickPhase(ActorTickPhase phase)
        {
            if (phase < ActorTickPhase.None || phase > ActorTickPhase.LateUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown Actor Tick phase.");
            }
        }
        #endregion

        #region Owner and instigator
        public Actor GetOwner()
        {
            AssertActorOwnerThread();
            return owner;
        }

        public T GetOwner<T>() where T : Actor
        {
            AssertActorOwnerThread();
            return owner as T;
        }

        /// <summary>
        /// Changes the lifetime owner reference. A World-bound Actor must be mutated on the
        /// World owner thread; <see cref="OwnerChanged"/> is invoked inline on that thread.
        /// </summary>
        public void SetOwner(Actor newOwner)
        {
            AssertActorOwnerThread();
            if (ReferenceEquals(newOwner, this))
            {
                throw new InvalidOperationException("An Actor cannot own itself.");
            }

            ValidateWorldRelationship(newOwner, world, "Owner");

            if (ReferenceEquals(owner, newOwner))
            {
                return;
            }

            owner = newOwner;
            Action[] observers = ownerChangedObservers;
            for (int i = 0; i < observers.Length; i++)
            {
                try
                {
                    observers[i].Invoke();
                }
                catch (Exception exception)
                {
                    ThrowNestedOutOfMemory(exception);
                    Log.Error(exception, $"Actor '{name}' OwnerChanged observer failed.");
                }
            }
        }

        public Actor GetInstigator()
        {
            AssertActorOwnerThread();
            return instigator;
        }

        public T GetInstigator<T>() where T : Actor
        {
            AssertActorOwnerThread();
            return instigator as T;
        }

        /// <summary>
        /// Changes the instigator reference. A World-bound Actor must be mutated on the
        /// World owner thread.
        /// </summary>
        public void SetInstigator(Actor newInstigator)
        {
            AssertActorOwnerThread();
            ValidateWorldRelationship(newInstigator, world, "Instigator");
            instigator = newInstigator;
        }

        private static void ValidateWorldRelationship(Actor relatedActor, World expectedWorld, string relationship)
        {
            if (ReferenceEquals(relatedActor, null))
            {
                return;
            }

            if (relatedActor == null)
            {
                throw new InvalidOperationException($"{relationship} must be null or reference a live Actor.");
            }

            if (!ReferenceEquals(relatedActor.world, expectedWorld))
            {
                throw new InvalidOperationException($"{relationship} must be null or belong to the same World.");
            }
        }
        #endregion

        #region Name and transform
        // These reads forward to Unity-native gameObject/transform state and use a lenient
        // thread check (AssertActorReadThread) so they remain callable in editor contexts
        // (Gizmos, inspectors) before Awake binds the lifecycle owner thread, while still
        // rejecting worker-thread access once the owner thread is bound.
        public string GetName()
        {
            AssertActorReadThread();
            return gameObject.name;
        }

        public Vector3 GetActorLocation()
        {
            AssertActorReadThread();
            return transform.position;
        }

        public Quaternion GetActorRotation()
        {
            AssertActorReadThread();
            return transform.rotation;
        }

        public Vector3 GetActorScale()
        {
            AssertActorReadThread();
            return transform.localScale;
        }

        public float GetYaw()
        {
            AssertActorReadThread();
            return transform.eulerAngles.y;
        }

        public Vector3 GetActorForwardVector()
        {
            AssertActorReadThread();
            return transform.forward;
        }

        public Vector3 GetActorRightVector()
        {
            AssertActorReadThread();
            return transform.right;
        }

        public Vector3 GetActorUpVector()
        {
            AssertActorReadThread();
            return transform.up;
        }
        public void SetActorLocation(Vector3 newLocation)
        {
            AssertActorOwnerThread();
            transform.position = newLocation;
        }

        public void SetActorRotation(Quaternion newRotation)
        {
            AssertActorOwnerThread();
            transform.rotation = newRotation;
        }

        public void SetActorScale(Vector3 newScale)
        {
            AssertActorOwnerThread();
            transform.localScale = newScale;
        }

        public void SetActorLocationAndRotation(Vector3 newLocation, Quaternion newRotation)
        {
            AssertActorOwnerThread();
            transform.SetPositionAndRotation(newLocation, newRotation);
        }
        #endregion

        #region Camera
        public virtual void GetActorEyesViewPoint(out Vector3 outLocation, out Quaternion outRotation)
        {
            outLocation = GetActorLocation();
            outRotation = GetActorRotation();
        }

        public virtual void CalcCamera(float deltaTime, out CameraPose outResult, float fallbackFov)
        {
            GetActorEyesViewPoint(out Vector3 location, out Quaternion rotation);
            outResult = new CameraPose(location, rotation, fallbackFov);
        }
        #endregion

        #region Visibility
        public bool IsHidden()
        {
            AssertActorOwnerThread();
            return bHidden;
        }

        public virtual void SetActorHiddenInGame(bool hidden)
        {
            AssertActorOwnerThread();
            ApplyActorHiddenInGame(hidden, forceRendererSync: false);
        }

        internal void ApplyActorHiddenInGame(bool hidden, bool forceRendererSync)
        {
            if (bHidden == hidden && !forceRendererSync)
            {
                return;
            }

            bHidden = hidden;
            rendererBuffer ??= new List<Renderer>(16);
            rendererBuffer.Clear();
            GetComponentsInChildren(includeInactive: true, rendererBuffer);
            for (int i = 0; i < rendererBuffer.Count; i++)
            {
                Renderer renderer = rendererBuffer[i];
                if (renderer != null)
                {
                    renderer.enabled = !bHidden;
                }
            }

            rendererBuffer.Clear();
        }
        #endregion

        #region Tags
        public int TagCount
        {
            get
            {
                AssertActorOwnerThread();
                return tags?.Count ?? 0;
            }
        }

        public string GetTagAt(int index)
        {
            AssertActorOwnerThread();
            if (tags == null || index < 0 || index >= tags.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return tags[index];
        }

        public bool ActorHasTag(string tag)
        {
            AssertActorOwnerThread();
            if (tags == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            return ContainsTag(tags, tag);
        }

        private static bool ContainsTag(List<string> source, string tag)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (string.Equals(source[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool AddTag(string tag)
        {
            AssertActorOwnerThread();
            ValidateTag(tag);
            tags ??= new List<string>(4);
            if (ContainsTag(tags, tag))
            {
                return false;
            }

            if (tags.Count >= ActorTagLimits.MaximumTagCount)
            {
                throw new InvalidOperationException(
                    $"Actor tag capacity ({ActorTagLimits.MaximumTagCount}) was exceeded.");
            }

            tags.Add(tag);
            return true;
        }

        public bool RemoveTag(string tag)
        {
            AssertActorOwnerThread();
            return tags != null && tags.Remove(tag);
        }

        public int CopyTagsTo(string[] destination, int destinationIndex = 0)
        {
            AssertActorOwnerThread();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            int count = TagCount;
            if (destinationIndex < 0 || destinationIndex > destination.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            }

            for (int i = 0; i < count; i++)
            {
                destination[destinationIndex + i] = tags[i];
            }

            return count;
        }

        public int CopyTagsTo(Span<string> destination)
        {
            AssertActorOwnerThread();
            int count = TagCount;
            if (destination.Length < count)
            {
                throw new ArgumentException(
                    "Destination span is smaller than the Actor tag count.",
                    nameof(destination));
            }

            for (int i = 0; i < count; i++)
            {
                destination[i] = tags[i];
            }

            return count;
        }

        public void ReplaceTags(ReadOnlySpan<string> replacement)
        {
            AssertActorOwnerThread();
            int count = replacement.Length;
            if (count > ActorTagLimits.MaximumTagCount)
            {
                throw new ArgumentException(
                    $"At most {ActorTagLimits.MaximumTagCount} Actor tags are allowed.",
                    nameof(replacement));
            }

            // Validate the complete input before mutating the current tag set.
            for (int i = 0; i < count; i++)
            {
                ValidateTag(replacement[i]);
            }

            if (count == 0)
            {
                tags?.Clear();
                return;
            }

            if (tags == null)
            {
                tags = new List<string>(count);
            }
            else if (tags.Capacity < count)
            {
                // Capacity growth is the only allocation this operation can require. Complete
                // it before clearing so an allocation failure leaves every existing tag intact.
                tags.Capacity = count;
            }

            tags.Clear();
            for (int i = 0; i < count; i++)
            {
                string tag = replacement[i];
                if (!ContainsTag(tags, tag))
                {
                    tags.Add(tag);
                }
            }
        }

        private static void ValidateTag(string tag)
        {
            if (ActorTagLimits.TryValidate(tag, out ActorTagValidationResult result))
            {
                return;
            }

            switch (result)
            {
                case ActorTagValidationResult.NullOrWhiteSpace:
                    throw new ArgumentException(
                        "Actor tags cannot be null, empty, or whitespace.",
                        nameof(tag));
                case ActorTagValidationResult.TooLong:
                    throw new ArgumentException(
                        $"Actor tags cannot exceed {ActorTagLimits.MaximumTagLength} characters.",
                        nameof(tag));
                default:
                    throw new InvalidOperationException("Actor tag validation returned an invalid result.");
            }
        }
        #endregion

        #region Damage
        public bool CanBeDamaged()
        {
            AssertActorOwnerThread();
            return bCanBeDamaged;
        }
        public void SetCanBeDamaged(bool value)
        {
            AssertActorOwnerThread();
            bCanBeDamaged = value;
        }

        public virtual float TakeDamage(
            float damageAmount,
            Controller eventInstigator = null,
            Actor damageCauser = null)
        {
            AssertActorOwnerThread();
            return TakeDamage(damageAmount, DamageEvent.MakeGenericDamage(), eventInstigator, damageCauser);
        }

        public virtual float TakeDamage(
            float damageAmount,
            in DamageEvent damageEvent,
            Controller eventInstigator = null,
            Actor damageCauser = null)
        {
            AssertActorOwnerThread();
            DamageEventValidationResult validationResult = damageEvent.Validate();
            if (validationResult != DamageEventValidationResult.Valid)
            {
                throw new ArgumentException(
                    $"Damage event is invalid ({validationResult}).",
                    nameof(damageEvent));
            }

            if (!bCanBeDamaged || damageAmount <= 0f || float.IsNaN(damageAmount) || float.IsInfinity(damageAmount))
            {
                return 0f;
            }

            float actualDamage = InternalTakeDamage(damageAmount, eventInstigator, damageCauser);
            if (actualDamage <= 0f || float.IsNaN(actualDamage) || float.IsInfinity(actualDamage))
            {
                return 0f;
            }

            switch (damageEvent.EventType)
            {
                case EDamageEventType.Point:
                    try
                    {
                        ReceivePointDamage(actualDamage, in damageEvent, eventInstigator, damageCauser);
                    }
                    catch (Exception exception)
                    {
                        ThrowNestedOutOfMemory(exception);
                        Log.Error(exception, $"Actor '{name}' point-damage receiver failed.");
                    }

                    InvokeDamageObservers(
                        pointDamageObservers,
                        actualDamage,
                        in damageEvent,
                        eventInstigator,
                        damageCauser,
                        "OnTakePointDamage");
                    break;
                case EDamageEventType.Radial:
                    try
                    {
                        ReceiveRadialDamage(actualDamage, in damageEvent, eventInstigator, damageCauser);
                    }
                    catch (Exception exception)
                    {
                        ThrowNestedOutOfMemory(exception);
                        Log.Error(exception, $"Actor '{name}' radial-damage receiver failed.");
                    }

                    InvokeDamageObservers(
                        radialDamageObservers,
                        actualDamage,
                        in damageEvent,
                        eventInstigator,
                        damageCauser,
                        "OnTakeRadialDamage");
                    break;
            }

            try
            {
                ReceiveAnyDamage(actualDamage, eventInstigator, damageCauser);
            }
            catch (Exception exception)
            {
                ThrowNestedOutOfMemory(exception);
                Log.Error(exception, $"Actor '{name}' generic-damage receiver failed.");
            }

            return actualDamage;
        }

        protected virtual float InternalTakeDamage(float damageAmount, Controller eventInstigator, Actor damageCauser)
        {
            return damageAmount;
        }

        protected virtual void ReceiveAnyDamage(float damage, Controller eventInstigator, Actor damageCauser) { }
        protected virtual void ReceivePointDamage(float damage, in DamageEvent damageEvent, Controller eventInstigator, Actor damageCauser) { }
        protected virtual void ReceiveRadialDamage(float damage, in DamageEvent damageEvent, Controller eventInstigator, Actor damageCauser) { }

        private void InvokeDamageObservers(
            DamageEventHandler[] observers,
            float damage,
            in DamageEvent damageEvent,
            Controller eventInstigator,
            Actor damageCauser,
            string eventName)
        {
            for (int i = 0; i < observers.Length; i++)
            {
                try
                {
                    observers[i].Invoke(
                        damage,
                        in damageEvent,
                        eventInstigator,
                        damageCauser);
                }
                catch (Exception exception)
                {
                    ThrowNestedOutOfMemory(exception);
                    Log.Error(exception, $"Actor '{name}' {eventName} observer failed.");
                }
            }
        }

        private static void ThrowNestedOutOfMemory(Exception exception)
        {
            OutOfMemoryException outOfMemory = FindTerminalOutOfMemory(exception);
            if (outOfMemory != null)
            {
                throw outOfMemory;
            }
        }

        private static Action<Actor>[] AddDestroyedObservers(
            Action<Actor>[] current,
            Action<Actor> value)
        {
            if (value == null)
            {
                return current;
            }

            Delegate[] additions = value.GetInvocationList();
            var next = new Action<Actor>[checked(current.Length + additions.Length)];
            Array.Copy(current, next, current.Length);
            for (int i = 0; i < additions.Length; i++)
            {
                next[current.Length + i] = (Action<Actor>)additions[i];
            }

            return next;
        }

        private static Action<Actor>[] RemoveDestroyedObservers(
            Action<Actor>[] current,
            Action<Actor> value)
        {
            if (value == null || current.Length == 0)
            {
                return current;
            }

            Delegate[] removals = value.GetInvocationList();
            for (int start = current.Length - removals.Length; start >= 0; start--)
            {
                bool matches = true;
                for (int i = 0; i < removals.Length; i++)
                {
                    if (!current[start + i].Equals(removals[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                if (removals.Length == current.Length)
                {
                    return Array.Empty<Action<Actor>>();
                }

                var next = new Action<Actor>[current.Length - removals.Length];
                Array.Copy(current, 0, next, 0, start);
                Array.Copy(
                    current,
                    start + removals.Length,
                    next,
                    start,
                    current.Length - start - removals.Length);
                return next;
            }

            return current;
        }

        private static Action[] AddActionObservers(
            Action[] current,
            Action value)
        {
            if (value == null)
            {
                return current;
            }

            Delegate[] additions = value.GetInvocationList();
            var next = new Action[checked(current.Length + additions.Length)];
            Array.Copy(current, next, current.Length);
            for (int i = 0; i < additions.Length; i++)
            {
                next[current.Length + i] = (Action)additions[i];
            }

            return next;
        }

        private static Action[] RemoveActionObservers(
            Action[] current,
            Action value)
        {
            if (value == null || current.Length == 0)
            {
                return current;
            }

            Delegate[] removals = value.GetInvocationList();
            for (int start = current.Length - removals.Length; start >= 0; start--)
            {
                bool matches = true;
                for (int i = 0; i < removals.Length; i++)
                {
                    if (!current[start + i].Equals(removals[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                if (removals.Length == current.Length)
                {
                    return Array.Empty<Action>();
                }

                var next = new Action[current.Length - removals.Length];
                Array.Copy(current, 0, next, 0, start);
                Array.Copy(
                    current,
                    start + removals.Length,
                    next,
                    start,
                    current.Length - start - removals.Length);
                return next;
            }

            return current;
        }

        private static DamageEventHandler[] AddDamageObservers(
            DamageEventHandler[] current,
            DamageEventHandler value)
        {
            if (value == null)
            {
                return current;
            }

            Delegate[] additions = value.GetInvocationList();
            var next = new DamageEventHandler[checked(current.Length + additions.Length)];
            Array.Copy(current, next, current.Length);
            for (int i = 0; i < additions.Length; i++)
            {
                next[current.Length + i] = (DamageEventHandler)additions[i];
            }

            return next;
        }

        private static DamageEventHandler[] RemoveDamageObservers(
            DamageEventHandler[] current,
            DamageEventHandler value)
        {
            if (value == null || current.Length == 0)
            {
                return current;
            }

            Delegate[] removals = value.GetInvocationList();
            for (int start = current.Length - removals.Length; start >= 0; start--)
            {
                bool matches = true;
                for (int i = 0; i < removals.Length; i++)
                {
                    if (!current[start + i].Equals(removals[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                if (removals.Length == current.Length)
                {
                    return Array.Empty<DamageEventHandler>();
                }

                var next = new DamageEventHandler[current.Length - removals.Length];
                Array.Copy(current, 0, next, 0, start);
                Array.Copy(
                    current,
                    start + removals.Length,
                    next,
                    start,
                    current.Length - start - removals.Length);
                return next;
            }

            return current;
        }
        #endregion

        #region Lifespan
        public float GetLifeSpan()
        {
            AssertActorOwnerThread();
            return initialLifeSpanSec;
        }

        public float GetRemainingLifeSpan()
        {
            AssertActorOwnerThread();
            if (lifeSpanCancellation == null || lifeSpanDeadline <= 0d)
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(lifeSpanDeadline - Time.timeAsDouble));
        }

        public void SetLifeSpan(float newLifeSpan)
        {
            AssertActorOwnerThread();
            if (float.IsNaN(newLifeSpan) || float.IsInfinity(newLifeSpan) || newLifeSpan < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(newLifeSpan));
            }

            if (lifeSpanCleanupInProgress)
            {
                throw new InvalidOperationException(
                    "A new Actor lifespan cannot begin while the previous lifespan owner is being released.");
            }

            CancelLifeSpan();

            if (newLifeSpan <= 0.001f || lifecycleState == ActorLifecycleState.Destroyed)
            {
                initialLifeSpanSec = newLifeSpan;
                return;
            }

            double deadline = Time.timeAsDouble + newLifeSpan;
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());
            lifeSpanCancellation = cancellation;
            lifeSpanCancellationCommitted = false;
            lifeSpanDeadline = deadline;
            initialLifeSpanSec = newLifeSpan;
            ExpireAfterAsync(newLifeSpan, cancellation).Forget();
        }

        private async UniTask ExpireAfterAsync(float seconds, CancellationTokenSource cancellation)
        {
            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(seconds),
                    DelayType.DeltaTime,
                    PlayerLoopTiming.Update,
                    cancellation.Token);

                if (ReferenceEquals(lifeSpanCancellation, cancellation))
                {
                    var terminalExceptions = new TerminalExceptionAccumulator();
                    bool disposed = false;
                    if (lifeSpanCleanupInProgress)
                    {
                        terminalExceptions.CaptureForPropagation(
                            new InvalidOperationException(
                                "Actor lifespan expiration re-entered owner cleanup."));
                    }
                    else
                    {
                        lifeSpanCleanupInProgress = true;
                        try
                        {
                            lifeSpanCancellationCommitted = true;
                            lifeSpanDeadline = 0d;
                            try
                            {
                                cancellation.Dispose();
                                disposed = true;
                            }
                            catch (Exception exception)
                            {
                                terminalExceptions.CaptureForPropagation(exception);
                            }

                            if (disposed && ReferenceEquals(lifeSpanCancellation, cancellation))
                            {
                                lifeSpanCancellation = null;
                                lifeSpanCancellationCommitted = false;
                            }
                        }
                        finally
                        {
                            lifeSpanCleanupInProgress = false;
                        }
                    }

                    try
                    {
                        if (world != null)
                        {
                            world.DestroyActor(this, EndPlayReason.Destroyed);
                        }
                        else if (this != null)
                        {
                            Destroy(gameObject);
                        }
                    }
                    catch (Exception exception)
                    {
                        terminalExceptions.CaptureForPropagation(exception);
                    }

                    terminalExceptions.ThrowIfCaptured();
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal path when lifespan changes or the Actor ends.
            }
        }

        private void CancelLifeSpan()
        {
            CancellationTokenSource cancellation = lifeSpanCancellation;
            if (cancellation == null)
            {
                lifeSpanDeadline = 0d;
                lifeSpanCancellationCommitted = false;
                return;
            }

            if (lifeSpanCleanupInProgress)
            {
                throw new InvalidOperationException(
                    "Actor lifespan owner cleanup cannot be re-entered.");
            }

            var terminalExceptions = new TerminalExceptionAccumulator();
            bool disposed = false;
            lifeSpanCleanupInProgress = true;
            try
            {
                if (!lifeSpanCancellationCommitted)
                {
                    try
                    {
                        cancellation.Cancel();
                        lifeSpanCancellationCommitted = true;
                        lifeSpanDeadline = 0d;
                    }
                    catch (Exception exception)
                    {
                        if (cancellation.IsCancellationRequested)
                        {
                            lifeSpanCancellationCommitted = true;
                            lifeSpanDeadline = 0d;
                        }

                        terminalExceptions.CaptureForPropagation(exception);
                    }
                }

                if (lifeSpanCancellationCommitted)
                {
                    try
                    {
                        cancellation.Dispose();
                        disposed = true;
                    }
                    catch (Exception exception)
                    {
                        terminalExceptions.CaptureForPropagation(exception);
                    }
                }

                if (lifeSpanCancellationCommitted &&
                    disposed &&
                    ReferenceEquals(lifeSpanCancellation, cancellation))
                {
                    lifeSpanCancellation = null;
                    lifeSpanDeadline = 0d;
                    lifeSpanCancellationCommitted = false;
                }
            }
            finally
            {
                lifeSpanCleanupInProgress = false;
            }

            terminalExceptions.ThrowIfCaptured();
        }
        #endregion

        #region World and lifecycle
        public virtual void FellOutOfWorld()
        {
            AssertActorOwnerThread();
            if (world != null)
            {
                world.DestroyActor(this, EndPlayReason.Destroyed);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public virtual void OutsideWorldBounds()
        {
            AssertActorOwnerThread();
        }

        public virtual bool HasAuthority()
        {
            AssertActorOwnerThread();
            return world == null || world.IsAuthority;
        }

        protected virtual void Awake()
        {
            BindActorOwnerThread();
            lifecycleState = ActorLifecycleState.Initialized;
            InitializeActorTickState();
        }

        protected virtual void Start()
        {
            world?.NotifyActorEnabled(this);
        }

        protected virtual void OnEnable()
        {
            // Handles an inactive registered Actor becoming active after its one-time Unity
            // Start callback. World still enforces the deferred-spawn publication barrier.
            world?.NotifyActorEnabled(this);
        }

        internal void BindToWorld(World targetWorld, bool allowReentry)
        {
            PrepareForWorldRegistration(targetWorld, allowReentry);
            if (lifecycleState == ActorLifecycleState.Ended)
            {
                lifecycleState = ActorLifecycleState.Initialized;
            }
            else if (lifecycleState == ActorLifecycleState.Constructed)
            {
                lifecycleState = ActorLifecycleState.Initialized;
            }

            world = targetWorld;
            worldUnboundNotified = false;
            InitializeActorTickState();
        }

        /// <summary>
        /// Establishes the World-authorized owner thread and validates registration without
        /// publishing the Actor into that World. This is the only pre-binding path allowed to
        /// initialize lifecycle ownership when Unity has not invoked Awake yet.
        /// </summary>
        internal void PrepareForWorldRegistration(World targetWorld, bool allowReentry)
        {
            if (targetWorld == null)
            {
                throw new ArgumentNullException(nameof(targetWorld));
            }

            targetWorld.AssertOwnerThread();
            BindActorOwnerThread();
            if (world != null && !ReferenceEquals(world, targetWorld))
            {
                throw new InvalidOperationException("Actor already belongs to another World.");
            }

            ValidateWorldRelationship(owner, targetWorld, "Owner");
            ValidateWorldRelationship(instigator, targetWorld, "Instigator");
            if (lifecycleState == ActorLifecycleState.Ending ||
                lifecycleState == ActorLifecycleState.Destroyed)
            {
                throw new InvalidOperationException("An ended Actor cannot enter a World.");
            }

            if (lifecycleState == ActorLifecycleState.Ended && !allowReentry)
            {
                throw new InvalidOperationException(
                    "An ended World-owned Actor cannot enter another World.");
            }
        }

        /// <summary>
        /// Enforces the Actor's World owner thread, or the Unity lifecycle thread captured by
        /// Awake/World binding while the Actor is not currently registered.
        /// </summary>
        protected void AssertActorOwnerThread()
        {
            World currentWorld = world;
            if (currentWorld != null)
            {
                currentWorld.AssertOwnerThread();
                return;
            }

            int expectedThreadId = actorOwnerThreadId;
            if (expectedThreadId == 0)
            {
                throw new InvalidOperationException(
                    "Actor lifecycle ownership has not been initialized.");
            }

            if (Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "Actor live state must be accessed on its Unity lifecycle owner thread.");
            }
        }

        /// <summary>
        /// Enforces the owner-thread contract for read accessors while allowing reads before the
        /// Actor's lifecycle owner thread has been bound (for example editor Gizmos and
        /// inspectors that read Unity-native state before Awake). Once bound, a read from a
        /// different thread still fails immediately.
        /// </summary>
        protected void AssertActorReadThread()
        {
            World currentWorld = world;
            if (currentWorld != null)
            {
                currentWorld.AssertOwnerThread();
                return;
            }

            int expectedThreadId = actorOwnerThreadId;
            if (expectedThreadId == 0)
            {
                // Not yet bound to a lifecycle owner thread; there is no captured thread to
                // validate against, so the read is allowed.
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "Actor live state must be accessed on its Unity lifecycle owner thread.");
            }
        }

        private void BindActorOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (actorOwnerThreadId != 0 && actorOwnerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "Actor lifecycle ownership cannot move between threads.");
            }

            actorOwnerThreadId = currentThreadId;
        }

        internal void NotifyWorldBeginPlay()
        {
            if (lifecycleState == ActorLifecycleState.Playing ||
                lifecycleState == ActorLifecycleState.Ending ||
                lifecycleState == ActorLifecycleState.Ended ||
                lifecycleState == ActorLifecycleState.Destroyed)
            {
                return;
            }

            lifecycleState = ActorLifecycleState.Playing;
            if (initialLifeSpanSec > 0.001f && lifeSpanCancellation == null)
            {
                SetLifeSpan(initialLifeSpanSec);
            }

            BeginPlay();
        }

        internal void UnbindFromWorld(World sourceWorld, EndPlayReason reason)
        {
            if (!ReferenceEquals(world, sourceWorld))
            {
                return;
            }

            var terminalExceptions = new TerminalExceptionAccumulator();
            try
            {
                NotifyEndPlay(reason);
            }
            catch (Exception exception)
            {
                terminalExceptions.CaptureForPropagation(exception);
            }

            try
            {
                NotifyWorldUnboundOnce(reason);
            }
            catch (Exception exception)
            {
                terminalExceptions.CaptureForPropagation(exception);
            }

            owner = null;
            instigator = null;
            world = null;
            actorTickEnabled = false;
            actorTickStateInitialized = false;
            terminalExceptions.ThrowIfCaptured();
        }

        protected virtual void BeginPlay() { }

        protected virtual void EndPlay(EndPlayReason reason) { }

        /// <summary>
        /// Releases World-scoped resources even when the Actor never reached BeginPlay.
        /// </summary>
        protected virtual void OnWorldUnbound(EndPlayReason reason) { }

        private void NotifyEndPlay(EndPlayReason reason)
        {
            if (lifecycleState != ActorLifecycleState.Playing)
            {
                if (lifecycleState != ActorLifecycleState.Ending &&
                    lifecycleState != ActorLifecycleState.Destroyed)
                {
                    endPlayReason = reason;
                    lifecycleState = ActorLifecycleState.Ended;
                }
                return;
            }

            endPlayReason = reason;
            lifecycleState = ActorLifecycleState.Ending;
            try
            {
                CancelLifeSpan();
                EndPlay(reason);
            }
            finally
            {
                if (this == null || lifecycleState == ActorLifecycleState.Destroyed)
                {
                    lifecycleState = ActorLifecycleState.Destroyed;
                }
                else
                {
                    lifecycleState = ActorLifecycleState.Ended;
                }
            }
        }

        private void NotifyWorldUnboundOnce(EndPlayReason reason)
        {
            if (worldUnboundNotified)
            {
                return;
            }

            worldUnboundNotified = true;
            OnWorldUnbound(reason);
        }

        protected virtual void OnDestroy()
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            try
            {
                CancelLifeSpan();
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "Actor lifespan cancellation failed during destruction.");
            }

            try
            {
                NotifyEndPlay(EndPlayReason.Destroyed);
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "Actor EndPlay callback failed during destruction.");
            }

            try
            {
                NotifyWorldUnboundOnce(endPlayReason);
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "Actor World-unbound callback failed during destruction.");
            }

            World previousWorld = world;
            world = null;
            try
            {
                previousWorld?.NotifyActorDestroyed(this);
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "Actor destruction bookkeeping notification failed.");
            }

            lifecycleState = ActorLifecycleState.Destroyed;
            Action<Actor>[] destroyedHandlers = destroyedObservers;
            destroyedObservers = Array.Empty<Action<Actor>>();
            pointDamageObservers = Array.Empty<DamageEventHandler>();
            radialDamageObservers = Array.Empty<DamageEventHandler>();
            ownerChangedObservers = Array.Empty<Action>();
            owner = null;
            instigator = null;
            actorTickEnabled = false;
            actorTickStateInitialized = false;
            rendererBuffer?.Clear();

            for (int i = 0; i < destroyedHandlers.Length; i++)
            {
                try
                {
                    destroyedHandlers[i].Invoke(this);
                }
                catch (Exception exception)
                {
                    terminalExceptions.HandleAndLog(
                        exception,
                        "Actor OnDestroyed observer failed.");
                }
            }

            terminalExceptions.ThrowIfCaptured();
        }

        /// <summary>
        /// Accumulates terminal callback failures without allocating a delegate or collection.
        /// Normal extension failures are logged and isolated; the first nested or direct
        /// OutOfMemoryException is rethrown only after required cleanup has completed.
        /// </summary>
        protected struct TerminalExceptionAccumulator
        {
            private Exception firstPropagatedException;
            private OutOfMemoryException firstOutOfMemory;

            public void CaptureForPropagation(Exception exception)
            {
                if (exception == null)
                {
                    return;
                }

                firstPropagatedException ??= exception;
                CaptureOutOfMemory(exception);
            }

            public void HandleAndLog(Exception exception, string failureDescription)
            {
                if (exception == null || CaptureOutOfMemory(exception))
                {
                    return;
                }

                try
                {
                    Log.Error(exception, failureDescription);
                }
                catch (Exception loggingException)
                {
                    CaptureOutOfMemory(loggingException);
                }
            }

            public void LogFailure(string failureDescription)
            {
                try
                {
                    Log.Error(failureDescription);
                }
                catch (Exception loggingException)
                {
                    CaptureOutOfMemory(loggingException);
                }
            }

            public void ThrowIfCaptured()
            {
                if (firstOutOfMemory != null)
                {
                    throw firstOutOfMemory;
                }

                if (firstPropagatedException != null)
                {
                    throw firstPropagatedException;
                }
            }

            private bool CaptureOutOfMemory(Exception exception)
            {
                OutOfMemoryException captured = FindTerminalOutOfMemory(exception);
                if (captured == null)
                {
                    return false;
                }

                firstOutOfMemory ??= captured;
                return true;
            }
        }

        protected static OutOfMemoryException FindTerminalOutOfMemory(Exception exception)
        {
            if (exception is OutOfMemoryException outOfMemory)
            {
                return outOfMemory;
            }

            if (exception is AggregateException aggregateException)
            {
                for (int i = 0; i < aggregateException.InnerExceptions.Count; i++)
                {
                    OutOfMemoryException nested = FindTerminalOutOfMemory(
                        aggregateException.InnerExceptions[i]);
                    if (nested != null)
                    {
                        return nested;
                    }
                }

                return null;
            }

            return exception.InnerException != null
                ? FindTerminalOutOfMemory(exception.InnerException)
                : null;
        }
        #endregion
    }
}
