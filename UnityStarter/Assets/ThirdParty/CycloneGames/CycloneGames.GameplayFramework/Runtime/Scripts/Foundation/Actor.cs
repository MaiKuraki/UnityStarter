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
        private ActorLifecycleState lifecycleState = ActorLifecycleState.Constructed;
        private EndPlayReason endPlayReason;
        private int stableInstanceId;
        private bool worldUnboundNotified;
        private bool actorTickEnabled;
        private bool actorTickStateInitialized;

        public event Action<Actor> OnDestroyed;
        public event Action OwnerChanged;
        public event Action<float, DamageEvent, Controller, Actor> OnTakePointDamage;
        public event Action<float, DamageEvent, Controller, Actor> OnTakeRadialDamage;

        public World World => world;
        public ActorLifecycleState LifecycleState => lifecycleState;
        public bool HasBegunPlay => lifecycleState == ActorLifecycleState.Playing;
        public bool CanEverTick => PrimaryTickPhase != ActorTickPhase.None;
        public ActorTickPhase TickPhase => PrimaryTickPhase;
        public bool IsTickEnabledAtStart => StartWithTickEnabled;

        public World GetWorld() => world;
        public GameInstance GetGameInstance() => world?.GetGameInstance();
        public GameMode GetAuthGameMode() => world?.GetAuthGameMode();
        public T GetAuthGameMode<T>() where T : GameMode => world?.GetAuthGameMode<T>();
        public GameState GetGameState() => world?.GetGameState();
        public T GetGameState<T>() where T : GameState => world?.GetGameState<T>();

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
            return CanEverTick && actorTickEnabled;
        }

        /// <summary>
        /// Enables or disables this Actor's primary Tick. Enabling requires a configured phase.
        /// </summary>
        public void SetActorTickEnabled(bool enabled)
        {
            world?.AssertOwnerThread();
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
            world?.AssertOwnerThread();
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
            world?.AssertOwnerThread();

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
        public Actor GetOwner() => owner;
        public T GetOwner<T>() where T : Actor => owner as T;

        /// <summary>
        /// Changes the lifetime owner reference. A World-bound Actor must be mutated on the
        /// World owner thread; <see cref="OwnerChanged"/> is invoked inline on that thread.
        /// </summary>
        public void SetOwner(Actor newOwner)
        {
            world?.AssertOwnerThread();
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
            OwnerChanged?.Invoke();
        }

        public Actor GetInstigator() => instigator;
        public T GetInstigator<T>() where T : Actor => instigator as T;

        /// <summary>
        /// Changes the instigator reference. A World-bound Actor must be mutated on the
        /// World owner thread.
        /// </summary>
        public void SetInstigator(Actor newInstigator)
        {
            world?.AssertOwnerThread();
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
        public string GetName() => gameObject.name;
        public Vector3 GetActorLocation() => transform.position;
        public Quaternion GetActorRotation() => transform.rotation;
        public Vector3 GetActorScale() => transform.localScale;
        public float GetYaw() => transform.eulerAngles.y;
        public Vector3 GetActorForwardVector() => transform.forward;
        public Vector3 GetActorRightVector() => transform.right;
        public Vector3 GetActorUpVector() => transform.up;
        public void SetActorLocation(Vector3 newLocation)
        {
            world?.AssertOwnerThread();
            transform.position = newLocation;
        }

        public void SetActorRotation(Quaternion newRotation)
        {
            world?.AssertOwnerThread();
            transform.rotation = newRotation;
        }

        public void SetActorScale(Vector3 newScale)
        {
            world?.AssertOwnerThread();
            transform.localScale = newScale;
        }

        public void SetActorLocationAndRotation(Vector3 newLocation, Quaternion newRotation)
        {
            world?.AssertOwnerThread();
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
        public bool IsHidden() => bHidden;

        public virtual void SetActorHiddenInGame(bool hidden)
        {
            world?.AssertOwnerThread();
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
        public int TagCount => tags?.Count ?? 0;

        public string GetTagAt(int index)
        {
            if (tags == null || index < 0 || index >= tags.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return tags[index];
        }

        public bool ActorHasTag(string tag)
        {
            if (tags == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool AddTag(string tag)
        {
            world?.AssertOwnerThread();
            ValidateTag(tag);
            tags ??= new List<string>(4);
            if (ActorHasTag(tag))
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
            world?.AssertOwnerThread();
            return tags != null && tags.Remove(tag);
        }

        public int CopyTagsTo(string[] destination, int destinationIndex = 0)
        {
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
            world?.AssertOwnerThread();
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

            tags?.Clear();
            if (count == 0)
            {
                return;
            }

            tags ??= new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string tag = replacement[i];
                if (!ActorHasTag(tag))
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
        public bool CanBeDamaged() => bCanBeDamaged;
        public void SetCanBeDamaged(bool value)
        {
            world?.AssertOwnerThread();
            bCanBeDamaged = value;
        }

        public virtual float TakeDamage(
            float damageAmount,
            Controller eventInstigator = null,
            Actor damageCauser = null)
        {
            world?.AssertOwnerThread();
            return TakeDamage(damageAmount, DamageEvent.MakeGenericDamage(), eventInstigator, damageCauser);
        }

        public virtual float TakeDamage(
            float damageAmount,
            DamageEvent damageEvent,
            Controller eventInstigator = null,
            Actor damageCauser = null)
        {
            world?.AssertOwnerThread();
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
                    ReceivePointDamage(actualDamage, damageEvent, eventInstigator, damageCauser);
                    OnTakePointDamage?.Invoke(actualDamage, damageEvent, eventInstigator, damageCauser);
                    break;
                case EDamageEventType.Radial:
                    ReceiveRadialDamage(actualDamage, damageEvent, eventInstigator, damageCauser);
                    OnTakeRadialDamage?.Invoke(actualDamage, damageEvent, eventInstigator, damageCauser);
                    break;
            }

            ReceiveAnyDamage(actualDamage, eventInstigator, damageCauser);
            return actualDamage;
        }

        protected virtual float InternalTakeDamage(float damageAmount, Controller eventInstigator, Actor damageCauser)
        {
            return damageAmount;
        }

        protected virtual void ReceiveAnyDamage(float damage, Controller eventInstigator, Actor damageCauser) { }
        protected virtual void ReceivePointDamage(float damage, DamageEvent damageEvent, Controller eventInstigator, Actor damageCauser) { }
        protected virtual void ReceiveRadialDamage(float damage, DamageEvent damageEvent, Controller eventInstigator, Actor damageCauser) { }
        #endregion

        #region Lifespan
        public float GetLifeSpan() => initialLifeSpanSec;

        public float GetRemainingLifeSpan()
        {
            if (lifeSpanCancellation == null || lifeSpanDeadline <= 0d)
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(lifeSpanDeadline - Time.timeAsDouble));
        }

        public void SetLifeSpan(float newLifeSpan)
        {
            world?.AssertOwnerThread();
            if (float.IsNaN(newLifeSpan) || float.IsInfinity(newLifeSpan) || newLifeSpan < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(newLifeSpan));
            }

            initialLifeSpanSec = newLifeSpan;
            CancelLifeSpan();

            if (newLifeSpan <= 0.001f || lifecycleState == ActorLifecycleState.Destroyed)
            {
                return;
            }

            lifeSpanDeadline = Time.timeAsDouble + newLifeSpan;
            lifeSpanCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());
            ExpireAfterAsync(newLifeSpan, lifeSpanCancellation).Forget();
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
                    lifeSpanCancellation = null;
                    lifeSpanDeadline = 0d;
                    cancellation.Dispose();

                    if (world != null)
                    {
                        world.DestroyActor(this, EndPlayReason.Destroyed);
                    }
                    else if (this != null)
                    {
                        Destroy(gameObject);
                    }
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
            lifeSpanCancellation = null;
            lifeSpanDeadline = 0d;
            if (cancellation == null)
            {
                return;
            }

            cancellation.Cancel();
            cancellation.Dispose();
        }
        #endregion

        #region World and lifecycle
        public virtual void FellOutOfWorld()
        {
            world?.AssertOwnerThread();
            if (world != null)
            {
                world.DestroyActor(this, EndPlayReason.Destroyed);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public virtual void OutsideWorldBounds() { }
        public virtual bool HasAuthority() => world == null || world.IsAuthority;

        protected virtual void Awake()
        {
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
            if (targetWorld == null)
            {
                throw new ArgumentNullException(nameof(targetWorld));
            }

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

            if (lifecycleState == ActorLifecycleState.Ended)
            {
                if (!allowReentry)
                {
                    throw new InvalidOperationException("An ended World-owned Actor cannot enter another World.");
                }

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

            try
            {
                NotifyEndPlay(reason);
            }
            finally
            {
                try
                {
                    NotifyWorldUnboundOnce(reason);
                }
                finally
                {
                    owner = null;
                    instigator = null;
                    world = null;
                    actorTickEnabled = false;
                    actorTickStateInitialized = false;
                }
            }
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
            CancelLifeSpan();
            try
            {
                NotifyEndPlay(EndPlayReason.Destroyed);
            }
            catch (Exception exception)
            {
                Log.Error(exception, $"Actor '{name}' EndPlay callback failed during destruction.");
            }

            try
            {
                NotifyWorldUnboundOnce(endPlayReason);
            }
            catch (Exception exception)
            {
                Log.Error(exception, $"Actor '{name}' World-unbound callback failed during destruction.");
            }

            World previousWorld = world;
            world = null;
            try
            {
                previousWorld?.NotifyActorDestroyed(this);
            }
            catch (Exception exception)
            {
                Log.Error(exception, $"Actor '{name}' destruction bookkeeping notification failed.");
            }

            lifecycleState = ActorLifecycleState.Destroyed;
            Action<Actor> destroyedHandlers = OnDestroyed;
            OnDestroyed = null;
            OnTakePointDamage = null;
            OnTakeRadialDamage = null;
            OwnerChanged = null;
            owner = null;
            instigator = null;
            actorTickEnabled = false;
            actorTickStateInitialized = false;
            rendererBuffer?.Clear();

            if (destroyedHandlers != null)
            {
                try
                {
                    destroyedHandlers.Invoke(this);
                }
                catch (Exception exception)
                {
                    Log.Error(exception, $"Actor '{name}' OnDestroyed observer failed.");
                }
            }
        }
        #endregion
    }
}
