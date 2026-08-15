using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Read-only registration data for an Actor owned or observed by a World. The value is
    /// returned by index without allocating a collection snapshot.
    /// </summary>
    public readonly struct WorldActorRegistration
    {
        internal WorldActorRegistration(Actor actor, bool isWorldOwned, bool isDeferred)
        {
            Actor = actor;
            IsWorldOwned = isWorldOwned;
            IsDeferred = isDeferred;
        }

        public Actor Actor { get; }
        public bool IsWorldOwned { get; }
        public bool IsDeferred { get; }
    }

    /// <summary>
    /// One gameplay scope. World is a typed lifetime owner, not a general-purpose service
    /// locator: it exposes only framework-owned state and explicit actor operations.
    /// </summary>
    public sealed class World : IDisposable
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        private struct ActorEntry
        {
            public Actor Actor;
            public bool Owned;
            public bool Deferred;
            public bool ActivateOnFinish;
            public ActorTickPhase TickPhase;
            public int TickListIndex;
            public bool TeardownDetached;
            public bool TeardownUnbound;
            public bool TeardownLifetimeReleased;
        }

        private enum ActorReleasePolicy : byte
        {
            None = 0,
            DestroyRegisteredActor = 1,
            ReleaseWorldOwnedActor = 2,
            ObserveDestroyedActor = 3,
        }

        private sealed class WorldActorCollector : IWorldActorCollector
        {
            private readonly World world;
            private readonly HashSet<int> candidateIds = new HashSet<int>();
            private bool isActive;
            private bool capacityExceeded;

            public WorldActorCollector(World world)
            {
                this.world = world;
            }

            public int Count
            {
                get
                {
                    EnsureActive();
                    world.EnsureOwnerThread();
                    world.EnsureActorDiscoveryTransactionActive();
                    return world.lifecycleScratch.Count;
                }
            }

            public int RemainingCapacity
            {
                get
                {
                    EnsureActive();
                    world.EnsureOwnerThread();
                    world.EnsureActorDiscoveryTransactionActive();
                    return Math.Max(
                        0,
                        world.runtimeLimits.MaximumActorCount -
                        world.actors.Count -
                        world.lifecycleScratch.Count);
                }
            }

            public bool TryAdd(Actor actor)
            {
                EnsureActive();
                world.EnsureOwnerThread();
                world.EnsureActorDiscoveryTransactionActive();
                if (actor == null)
                {
                    return true;
                }

                int instanceId = actor.GetStableInstanceId();
                if (world.actorIndices.ContainsKey(instanceId) || candidateIds.Contains(instanceId))
                {
                    return true;
                }

                if (RemainingCapacity == 0)
                {
                    if (!capacityExceeded)
                    {
                        capacityExceeded = true;
                        world.IncrementRejectedActorAdmissionCount();
                    }

                    return false;
                }

                candidateIds.Add(instanceId);
                world.lifecycleScratch.Add(actor);
                return true;
            }

            public void Begin()
            {
                if (isActive)
                {
                    throw new InvalidOperationException("World Actor collection is already active.");
                }

                candidateIds.Clear();
                capacityExceeded = false;
                isActive = true;
            }

            public bool End()
            {
                if (!isActive)
                {
                    return false;
                }

                bool exceeded = capacityExceeded;
                candidateIds.Clear();
                capacityExceeded = false;
                isActive = false;
                return exceeded;
            }

            private void EnsureActive()
            {
                if (!isActive)
                {
                    throw new InvalidOperationException(
                        "The World Actor collector is only valid during its CollectActors callback.");
                }
            }
        }

        private readonly GameInstance gameInstance;
        private readonly IActorLifetime actorLifetime;
        private readonly WorldDefinition definition;
        private readonly IGameSession configuredGameSession;
        private readonly ISceneTransitionHandler sceneTransitionHandler;
        private readonly int ownerThreadId;
        private readonly WorldRuntimeLimits runtimeLimits;
        private readonly IWorldActorSource actorSource;
        private readonly WorldActorCollector actorCollector;
        private readonly IMatchClock matchClock;
        private readonly ICameraOutputLeaseArbiter cameraOutputLeaseArbiter;
        private readonly WorldShutdownIncompleteException shutdownIncompleteException;
        private readonly List<ActorEntry> actors;
        private readonly List<Actor> lifecycleScratch;
        private readonly List<Actor> updateTickActors;
        private readonly List<Actor> fixedUpdateTickActors;
        private readonly List<Actor> lateUpdateTickActors;
        private readonly List<Actor> tickScratch;
        private readonly Dictionary<int, int> actorIndices;
        private readonly List<PlayerController> playerControllers = new List<PlayerController>(8);
        private readonly List<PlayerState> participantPlayerStates = new List<PlayerState>(8);
        private readonly List<PlayerStart> playerStarts = new List<PlayerStart>(16);
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        private WorldLifecycleState lifecycleState = WorldLifecycleState.Created;
        private GameMode gameMode;
        // The live Unity component and its terminal cleanup owner have different lifetimes.
        // Unity destruction removes the live component immediately, while the managed wrapper
        // must remain reachable until participant and session ownership reaches a terminal state.
        private GameMode terminalGameModeOwner;
        private GameMode gameModeDestructionStageOwner;
        private IGameSession terminalGameSession;
        private GameState gameState;
        private int ownedActorCount;
        private int peakActorCount;
        private long rejectedActorAdmissionCount;
        private OwnerThreadReadOnlyList<PlayerController> playerControllerView;
        private OwnerThreadReadOnlyList<PlayerStart> playerStartView;
        private bool tickDispatchReady;
        private bool isDispatchingActorTick;
        private bool isTerminalCleanupInProgress;
        private bool isGameplayShutdownInProgress;
        private bool gameplayShutdownCompleted;
        private bool lifetimeCancellationDisposed;
        private bool pendingGameplayCleanup;
        private bool pendingActorCleanup;
        private bool pendingCameraOutputCleanup;
        private bool pendingLifetimeTokenCleanup;
        private EndPlayReason shutdownReason;
        private CameraOutputTerminalReleasePass activeCameraOutputTerminalReleasePass;
        private ActorTickPhase activeTickPhase;

        internal World(
            GameInstance gameInstance,
            IActorLifetime actorLifetime,
            WorldDefinition definition,
            WorldNetMode netMode,
            IGameSession gameSession,
            ISceneTransitionHandler sceneTransitionHandler,
            int ownerThreadId,
            WorldRuntimeLimits runtimeLimits,
            IWorldActorSource actorSource,
            IMatchClock matchClock,
            ICameraOutputLeaseArbiter cameraOutputLeaseArbiter)
        {
            ValidateNetMode(netMode);
            this.gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            this.actorLifetime = actorLifetime ?? throw new ArgumentNullException(nameof(actorLifetime));
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            configuredGameSession = gameSession;
            this.sceneTransitionHandler = sceneTransitionHandler;
            this.ownerThreadId = ownerThreadId;
            this.runtimeLimits = runtimeLimits ?? throw new ArgumentNullException(nameof(runtimeLimits));
            this.actorSource = actorSource;
            this.matchClock = matchClock ?? throw new ArgumentNullException(nameof(matchClock));
            this.cameraOutputLeaseArbiter = cameraOutputLeaseArbiter ??
                throw new ArgumentNullException(nameof(cameraOutputLeaseArbiter));
            shutdownIncompleteException = new WorldShutdownIncompleteException(this);
            actors = new List<ActorEntry>(runtimeLimits.InitialActorCapacity);
            lifecycleScratch = new List<Actor>(runtimeLimits.InitialActorCapacity);
            actorCollector = new WorldActorCollector(this);
            updateTickActors = new List<Actor>(runtimeLimits.InitialUpdateTickCapacity);
            fixedUpdateTickActors = new List<Actor>(runtimeLimits.InitialFixedUpdateTickCapacity);
            lateUpdateTickActors = new List<Actor>(runtimeLimits.InitialLateUpdateTickCapacity);
            tickScratch = new List<Actor>(Math.Max(
                runtimeLimits.InitialUpdateTickCapacity,
                Math.Max(
                    runtimeLimits.InitialFixedUpdateTickCapacity,
                    runtimeLimits.InitialLateUpdateTickCapacity)));
            actorIndices = new Dictionary<int, int>(runtimeLimits.InitialActorCapacity);
            NetMode = netMode;
        }

        public GameInstance GameInstance
        {
            get
            {
                EnsureOwnerThread();
                return gameInstance;
            }
        }

        public WorldRuntimeLimits RuntimeLimits => runtimeLimits;
        public IMatchClock MatchClock
        {
            get
            {
                EnsureOwnerThread();
                return matchClock;
            }
        }

        public IWorldDefinition Definition
        {
            get
            {
                EnsureOwnerThread();
                return definition;
            }
        }
        public WorldNetMode NetMode { get; }
        public WorldLifecycleState LifecycleState
        {
            get
            {
                EnsureOwnerThread();
                return lifecycleState;
            }
        }

        internal bool HasPendingGameplayCleanup
        {
            get
            {
                EnsureOwnerThread();
                return pendingGameplayCleanup;
            }
        }

        internal bool HasPendingActorCleanup
        {
            get
            {
                EnsureOwnerThread();
                return pendingActorCleanup;
            }
        }

        internal bool HasPendingCameraOutputCleanup
        {
            get
            {
                EnsureOwnerThread();
                return pendingCameraOutputCleanup;
            }
        }

        internal int PendingWorldSettingsLeaseCount
        {
            get
            {
                EnsureOwnerThread();
                return definition.PendingLeaseCount;
            }
        }

        internal bool HasPendingLifetimeTokenCleanup
        {
            get
            {
                EnsureOwnerThread();
                return pendingLifetimeTokenCleanup;
            }
        }

        public bool IsAuthority =>
            NetMode == WorldNetMode.Standalone ||
            NetMode == WorldNetMode.ListenServer ||
            NetMode == WorldNetMode.DedicatedServer;
        public bool IsDedicatedServer => NetMode == WorldNetMode.DedicatedServer;
        public GameMode GameMode
        {
            get
            {
                EnsureOwnerThread();
                return gameMode;
            }
        }

        public GameState GameState
        {
            get
            {
                EnsureOwnerThread();
                return gameState;
            }
        }

        public OwnerThreadReadOnlyList<PlayerController> PlayerControllers
        {
            get
            {
                EnsureOwnerThread();
                return playerControllerView ??= new OwnerThreadReadOnlyList<PlayerController>(
                    EnsureOwnerThread,
                    playerControllers);
            }
        }

        public OwnerThreadReadOnlyList<PlayerStart> PlayerStarts
        {
            get
            {
                EnsureOwnerThread();
                return playerStartView ??= new OwnerThreadReadOnlyList<PlayerStart>(
                    EnsureOwnerThread,
                    playerStarts);
            }
        }

        public int ActorCount
        {
            get
            {
                EnsureOwnerThread();
                return actors.Count;
            }
        }

        public int PeakActorCount
        {
            get
            {
                EnsureOwnerThread();
                return peakActorCount;
            }
        }

        public int OwnedActorCount
        {
            get
            {
                EnsureOwnerThread();
                return ownedActorCount;
            }
        }

        public int PlayerControllerCount
        {
            get
            {
                EnsureOwnerThread();
                return playerControllers.Count;
            }
        }

        public int PlayerStartCount
        {
            get
            {
                EnsureOwnerThread();
                return playerStarts.Count;
            }
        }

        public long RejectedActorAdmissionCount
        {
            get
            {
                EnsureOwnerThread();
                return rejectedActorAdmissionCount;
            }
        }

        public CancellationToken LifetimeToken
        {
            get
            {
                EnsureOwnerThread();
                return lifetimeCancellation.Token;
            }
        }
        public ISceneTransitionHandler SceneTransitionHandler
        {
            get
            {
                EnsureOwnerThread();
                return sceneTransitionHandler;
            }
        }
        public bool IsDispatchingActorTick
        {
            get
            {
                EnsureOwnerThread();
                return isDispatchingActorTick;
            }
        }

        public ActorTickPhase ActiveTickPhase
        {
            get
            {
                EnsureOwnerThread();
                return activeTickPhase;
            }
        }

        public GameInstance GetGameInstance()
        {
            EnsureOwnerThread();
            return gameInstance;
        }
        public GameMode GetAuthGameMode()
        {
            EnsureOwnerThread();
            return IsAuthority ? gameMode : null;
        }

        public T GetAuthGameMode<T>() where T : GameMode => GetAuthGameMode() as T;
        public GameState GetGameState()
        {
            EnsureOwnerThread();
            return gameState;
        }

        public T GetGameState<T>() where T : GameState => GetGameState() as T;
        public PlayerController GetFirstPlayerController() => GetPlayerController(0);

        public PlayerController GetPlayerController(int index)
        {
            EnsureOwnerThread();
            return (uint)index < (uint)playerControllers.Count
                ? playerControllers[index]
                : null;
        }

        public void AssertOwnerThread()
        {
            EnsureOwnerThread();
        }

        /// <summary>
        /// Dispatches one primary Actor Tick phase. Actors added, enabled, or moved into a phase
        /// during dispatch become eligible on the next dispatch of the target phase.
        /// </summary>
        public void Tick(ActorTickPhase phase, float deltaSeconds)
        {
            EnsureOwnerThread();
            ValidateTickRequest(phase, deltaSeconds);
            if (lifecycleState == WorldLifecycleState.Disposed)
            {
                throw new ObjectDisposedException(nameof(World));
            }

            if (isDispatchingActorTick)
            {
                throw new InvalidOperationException(
                    $"Actor Tick re-entry is not allowed while dispatching '{activeTickPhase}'.");
            }

            if (!tickDispatchReady || lifecycleState != WorldLifecycleState.Playing)
            {
                return;
            }

            List<Actor> tickActors = GetTickActorList(phase);
            if (tickActors.Count == 0)
            {
                return;
            }

            isDispatchingActorTick = true;
            activeTickPhase = phase;
            tickScratch.AddRange(tickActors);
            try
            {
                for (int i = 0; i < tickScratch.Count; i++)
                {
                    if (!tickDispatchReady || lifecycleState != WorldLifecycleState.Playing)
                    {
                        break;
                    }

                    Actor actor = tickScratch[i];
                    if (!CanDispatchActorTick(actor, phase))
                    {
                        continue;
                    }

                    try
                    {
                        actor.DispatchTick(deltaSeconds);
                    }
                    catch (Exception exception)
                    {
                        HandleActorCallbackFailure(actor, phase, exception);
                    }
                }
            }
            finally
            {
                tickScratch.Clear();
                activeTickPhase = ActorTickPhase.None;
                isDispatchingActorTick = false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HandleActorCallbackFailure(
            Actor actor,
            ActorTickPhase phase,
            Exception exception)
        {
            OutOfMemoryException outOfMemory = FindOutOfMemory(exception);
            if (outOfMemory != null)
            {
                throw outOfMemory;
            }

            try
            {
                string actorName = actor != null ? actor.name : "<destroyed>";
                Log.Error(
                    exception,
                    $"Actor '{actorName}' Tick failed during '{phase}'; dispatch will continue with the remaining actors.");
            }
            catch (Exception loggingException)
            {
                outOfMemory = FindOutOfMemory(loggingException);
                if (outOfMemory != null)
                {
                    throw outOfMemory;
                }
            }
        }

        public int GetTickActorCount(ActorTickPhase phase)
        {
            EnsureOwnerThread();
            return GetTickActorList(phase).Count;
        }

        public bool ContainsPlayerController(PlayerController playerController)
        {
            EnsureOwnerThread();
            return IndexOfPlayerControllerReference(playerController) >= 0;
        }

        internal async UniTask InitializeAsync(
            IReadOnlyList<LocalPlayer> localPlayers,
            CancellationToken cancellationToken)
        {
            EnsureOwnerThread();
            TransitionTo(WorldLifecycleState.Initializing);
            using var initializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            CancellationToken initializationToken = initializationCancellation.Token;
            initializationToken.ThrowIfCancellationRequested();

            DiscoverActors();

            if (IsAuthority)
            {
                gameMode = SpawnActor(definition.GameModeClass);
                terminalGameModeOwner = gameMode;
                gameMode.Initialize(this, configuredGameSession);
                await gameMode.StartPlayAsync(localPlayers, initializationToken);
                await UniTask.SwitchToMainThread();
                EnsureOwnerThread();
            }

            initializationToken.ThrowIfCancellationRequested();
            if (lifecycleState != WorldLifecycleState.Initializing)
            {
                throw new InvalidOperationException(
                    "World initialization cannot continue after shutdown has started.");
            }

            TransitionTo(WorldLifecycleState.Playing);
            BeginPlayForRegisteredActors();
            gameMode?.NotifyWorldStarted();
            tickDispatchReady = true;
        }

        /// <summary>
        /// Spawns and world-owns an Actor. This is a cold-path operation and must run on the
        /// GameInstance owner thread.
        /// </summary>
        public T SpawnActor<T>(T prefab) where T : Actor
        {
            if (!TrySpawnActor(prefab, out T actor))
            {
                throw CreateActorCapacityException();
            }

            return actor;
        }

        /// <summary>
        /// Attempts to spawn and world-own an Actor. Returns false only when the World actor
        /// implementation ceiling rejects a new Actor.
        /// </summary>
        public bool TrySpawnActor<T>(T prefab, out T actor) where T : Actor
        {
            return TrySpawnActorInternal(prefab, beginIfPlaying: true, out actor);
        }

        /// <summary>
        /// Spawns and registers an Actor without publishing BeginPlay in an already-running
        /// World. Configure dependencies, then call <see cref="FinishSpawningActor"/>.
        /// </summary>
        public T SpawnActorDeferred<T>(T prefab) where T : Actor
        {
            if (!TrySpawnActorDeferred(prefab, out T actor))
            {
                throw CreateActorCapacityException();
            }

            return actor;
        }

        /// <summary>
        /// Attempts a deferred spawn. Returns false only when the World actor implementation
        /// ceiling rejects a new Actor.
        /// </summary>
        public bool TrySpawnActorDeferred<T>(T prefab, out T actor) where T : Actor
        {
            return TrySpawnActorInternal(prefab, beginIfPlaying: false, out actor);
        }

        public void FinishSpawningActor(Actor actor)
        {
            EnsureOwnerThread();
            if (actor == null || !actorIndices.TryGetValue(actor.GetStableInstanceId(), out int index))
            {
                throw new InvalidOperationException("Cannot complete an unregistered Actor spawn.");
            }

            ActorEntry entry = actors[index];
            if (entry.Deferred)
            {
                entry.Deferred = false;
                actors[index] = entry;
                if (entry.ActivateOnFinish && !actor.gameObject.activeSelf)
                {
                    actor.gameObject.SetActive(true);
                }
            }

            if (lifecycleState == WorldLifecycleState.Playing && actor.isActiveAndEnabled)
            {
                actor.NotifyWorldBeginPlay();
            }
        }

        private bool TrySpawnActorInternal<T>(T prefab, bool beginIfPlaying, out T actor) where T : Actor
        {
            EnsureOwnerThread();
            EnsureAcceptingActors();

            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (actors.Count >= runtimeLimits.MaximumActorCount)
            {
                IncrementRejectedActorAdmissionCount();
                actor = null;
                return false;
            }

            T instance = actorLifetime.Create(prefab);
            if (instance == null)
            {
                throw new InvalidOperationException($"The Actor lifetime returned null for '{prefab.name}'.");
            }

            bool pendingLifetimeRelease = true;
            bool registrationAdded = false;
            try
            {
                bool deferred = !beginIfPlaying;
                bool activateOnFinish = deferred && instance.gameObject.activeSelf;
                if (activateOnFinish)
                {
                    instance.gameObject.SetActive(false);
                }

                if (!TryRegisterActorCore(
                        instance,
                        owned: true,
                        deferred,
                        activateOnFinish,
                        out registrationAdded))
                {
                    ReleasePendingSpawn(instance, ref pendingLifetimeRelease);
                    actor = null;
                    return false;
                }

                // Registry ownership commits before any Actor callback can run.
                pendingLifetimeRelease = false;
                BeginPlayAfterRegistration(instance, beginIfPlaying);
                if (!IsActorRegistered(instance))
                {
                    throw new InvalidOperationException(
                        $"Actor '{prefab.name}' ended its lifetime while BeginPlay was being published.");
                }

                actor = instance;
                return true;
            }
            catch (Exception spawnException)
            {
                Exception cleanupException = null;
                bool releaseRegisteredInstance = registrationAdded && IsActorRegistered(instance);
                bool registrationRemoved = false;
                try
                {
                    if (releaseRegisteredInstance)
                    {
                        RollbackActorRegistration(
                            instance,
                            EndPlayReason.InitializationFailure);
                        registrationRemoved = true;
                    }
                }
                catch (Exception exception)
                {
                    cleanupException = exception;
                }

                if (releaseRegisteredInstance && registrationRemoved)
                {
                    try
                    {
                        actorLifetime.Release(instance);
                    }
                    catch (Exception exception)
                    {
                        cleanupException = cleanupException == null
                            ? exception
                            : new AggregateException(cleanupException, exception);
                    }
                }

                if (IsActorRegistered(instance) || ReferenceEquals(instance.World, this))
                {
                    // Registry ownership remains reachable for a later terminal retry.
                    pendingLifetimeRelease = false;
                }

                try
                {
                    ReleasePendingSpawn(instance, ref pendingLifetimeRelease);
                }
                catch (Exception exception)
                {
                    cleanupException = cleanupException == null
                        ? exception
                        : new AggregateException(cleanupException, exception);
                }

                if (cleanupException != null)
                {
                    throw new AggregateException(
                        "Actor spawn failed and ownership rollback also reported an error.",
                        spawnException,
                        cleanupException);
                }

                throw;
            }
        }

        /// <summary>
        /// Registers a scene- or externally-created Actor without transferring destruction
        /// ownership. The Actor still receives world BeginPlay/EndPlay notifications.
        /// </summary>
        public void RegisterActor(Actor actor)
        {
            if (!TryRegisterActor(actor))
            {
                throw CreateActorCapacityException();
            }
        }

        /// <summary>
        /// Attempts to register a scene- or externally-created Actor. Returns false only when
        /// the World actor implementation ceiling rejects a new Actor.
        /// </summary>
        public bool TryRegisterActor(Actor actor)
        {
            EnsureOwnerThread();
            EnsureAcceptingActors();
            bool registered = TryRegisterActorCore(
                actor,
                owned: false,
                deferred: false,
                activateOnFinish: false,
                out bool registrationAdded);
            if (!registered)
            {
                return false;
            }

            try
            {
                BeginPlayAfterRegistration(actor, beginIfPlaying: true);
                if (!IsActorRegistered(actor))
                {
                    throw new InvalidOperationException(
                        $"Actor '{actor.name}' ended its lifetime while BeginPlay was being published.");
                }

                return true;
            }
            catch
            {
                if (registrationAdded && IsActorRegistered(actor))
                {
                    RollbackActorRegistration(actor, EndPlayReason.InitializationFailure);
                }

                throw;
            }
        }

        /// <summary>
        /// Removes a live, externally owned Actor from this World without destroying it or
        /// invoking the configured Actor lifetime. World-owned Actors must use DestroyActor.
        /// </summary>
        public void UnregisterActor(
            Actor actor,
            EndPlayReason reason = EndPlayReason.RemovedFromWorld)
        {
            EnsureOwnerThread();
            EnsureAcceptingActors();
            if (actor == null ||
                !actorIndices.TryGetValue(actor.GetStableInstanceId(), out int index))
            {
                throw new InvalidOperationException("Cannot unregister an Actor that is not registered.");
            }

            if (actors[index].Owned)
            {
                throw new InvalidOperationException(
                    "World-owned Actors cannot be unregistered; use DestroyActor to end their lifetime.");
            }

            UnregisterExternalActorAt(index, reason);
        }

        /// <summary>
        /// Attempts to remove a live, externally owned Actor without destroying or releasing it.
        /// Returns false for null, unregistered, or World-owned Actors.
        /// </summary>
        public bool TryUnregisterActor(
            Actor actor,
            EndPlayReason reason = EndPlayReason.RemovedFromWorld)
        {
            EnsureOwnerThread();
            EnsureAcceptingActors();
            if (actor == null ||
                !actorIndices.TryGetValue(actor.GetStableInstanceId(), out int index) ||
                actors[index].Owned)
            {
                return false;
            }

            UnregisterExternalActorAt(index, reason);
            return true;
        }

        /// <summary>Returns an allocation-free O(1) actor admission snapshot.</summary>
        public ActorAdmissionSnapshot GetActorAdmissionSnapshot()
        {
            EnsureOwnerThread();
            return new ActorAdmissionSnapshot(
                actors.Count,
                runtimeLimits.MaximumActorCount,
                actors.Capacity,
                peakActorCount,
                rejectedActorAdmissionCount);
        }

        public bool IsActorRegistered(Actor actor)
        {
            EnsureOwnerThread();
            return !ReferenceEquals(actor, null) &&
                   actorIndices.ContainsKey(actor.GetStableInstanceId());
        }

        public bool TryGetActor(int instanceId, out Actor actor)
        {
            EnsureOwnerThread();
            if (actorIndices.TryGetValue(instanceId, out int index))
            {
                actor = actors[index].Actor;
                return actor != null;
            }

            actor = null;
            return false;
        }

        /// <summary>
        /// Reads one registration by its current dense index. Indices are not stable across
        /// actor removal and must not be persisted between calls.
        /// </summary>
        public bool TryGetActorRegistration(int index, out WorldActorRegistration registration)
        {
            EnsureOwnerThread();
            if ((uint)index < (uint)actors.Count)
            {
                ActorEntry entry = actors[index];
                registration = new WorldActorRegistration(entry.Actor, entry.Owned, entry.Deferred);
                return entry.Actor != null;
            }

            registration = default;
            return false;
        }

        public bool TryGetActor<T>(out T actor) where T : Actor
        {
            EnsureOwnerThread();
            for (int i = 0; i < actors.Count; i++)
            {
                if (actors[i].Actor is T candidate)
                {
                    actor = candidate;
                    return true;
                }
            }

            actor = null;
            return false;
        }

        /// <summary>
        /// Ends, unregisters, and destroys an Actor immediately in Edit Mode or at the normal
        /// Unity destruction boundary in Play Mode.
        /// </summary>
        public bool DestroyActor(Actor actor, EndPlayReason reason = EndPlayReason.Destroyed)
        {
            EnsureOwnerThread();
            if (actor == null || !actorIndices.TryGetValue(actor.GetStableInstanceId(), out int index))
            {
                return false;
            }

            if (actor is PlayerController playerController &&
                IndexOfPlayerControllerReference(playerController) >= 0 &&
                gameMode != null)
            {
                return gameMode.Logout(playerController);
            }
            if (actor is PlayerState playerState &&
                gameMode != null &&
                TryGetPlayerControllerForState(playerState, out PlayerController stateOwner))
            {
                return gameMode.Logout(stateOwner);
            }

            if (ReferenceEquals(actor, gameMode) &&
                (lifecycleState == WorldLifecycleState.Initializing ||
                 lifecycleState == WorldLifecycleState.Playing))
            {
                ShutdownImmediate(reason);
                return true;
            }

            bool removed = TryTeardownActorAt(
                index,
                reason,
                ActorReleasePolicy.DestroyRegisteredActor,
                preparePlayerCameraContext: true,
                out Exception teardownFailure);
            if (teardownFailure != null)
            {
                throw teardownFailure;
            }

            return removed;
        }

        /// <summary>
        /// Ends gameplay and releases all World-owned resources. Shutdown is deliberately
        /// non-cancellable after entry so every ownership boundary reaches a terminal state.
        /// </summary>
        public async UniTask ShutdownAsync(
            EndPlayReason reason = EndPlayReason.WorldShutdown)
        {
            EnsureOwnerThread();
            if (lifecycleState == WorldLifecycleState.Disposed ||
                lifecycleState == WorldLifecycleState.Stopped)
            {
                return;
            }

            if (lifecycleState == WorldLifecycleState.Stopping)
            {
                RetryTerminalCleanup();
                return;
            }

            OutOfMemoryException terminalOutOfMemory = BeginStopping(reason);
            isTerminalCleanupInProgress = true;
            isGameplayShutdownInProgress = true;

            try
            {
                GameMode cleanupOwner = terminalGameModeOwner;
                if (CanInvokeGameModeCleanupOwner(cleanupOwner))
                {
                    await cleanupOwner.ShutdownAsync(reason);
                }
            }
            catch (Exception exception)
            {
                if (!TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception))
                {
                    throw;
                }
            }
            finally
            {
                try
                {
                    await UniTask.SwitchToMainThread();
                    EnsureOwnerThread();
                    isGameplayShutdownInProgress = false;
                    gameplayShutdownCompleted = IsGameModeShutdownComplete();
                    CompleteShutdown(reason, terminalOutOfMemory);
                }
                finally
                {
                    isGameplayShutdownInProgress = false;
                    isTerminalCleanupInProgress = false;
                    activeCameraOutputTerminalReleasePass = default;
                }
            }
        }

        internal void AbortInitialization()
        {
            EnsureOwnerThread();
            if (lifecycleState == WorldLifecycleState.Disposed)
            {
                return;
            }

            if (lifecycleState == WorldLifecycleState.Stopping)
            {
                RetryTerminalCleanup();
                return;
            }

            OutOfMemoryException terminalOutOfMemory = BeginStopping(
                EndPlayReason.InitializationFailure);
            isTerminalCleanupInProgress = true;
            isGameplayShutdownInProgress = true;
            try
            {
                GameMode cleanupOwner = terminalGameModeOwner;
                if (CanInvokeGameModeCleanupOwner(cleanupOwner))
                {
                    cleanupOwner.ShutdownImmediate(EndPlayReason.InitializationFailure);
                }
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameMode shutdown after World initialization failure failed; World cleanup will continue.",
                    ref terminalOutOfMemory);
            }
            finally
            {
                try
                {
                    isGameplayShutdownInProgress = false;
                    gameplayShutdownCompleted = IsGameModeShutdownComplete();
                    CompleteShutdown(EndPlayReason.InitializationFailure, terminalOutOfMemory);
                }
                finally
                {
                    isGameplayShutdownInProgress = false;
                    isTerminalCleanupInProgress = false;
                    activeCameraOutputTerminalReleasePass = default;
                }
            }
        }

        internal void ShutdownImmediate(EndPlayReason reason)
        {
            EnsureOwnerThread();
            if (lifecycleState == WorldLifecycleState.Disposed)
            {
                return;
            }

            if (lifecycleState == WorldLifecycleState.Stopping)
            {
                RetryTerminalCleanup();
                return;
            }

            OutOfMemoryException terminalOutOfMemory = BeginStopping(reason);
            isTerminalCleanupInProgress = true;
            isGameplayShutdownInProgress = true;
            try
            {
                GameMode cleanupOwner = terminalGameModeOwner;
                if (CanInvokeGameModeCleanupOwner(cleanupOwner))
                {
                    cleanupOwner.ShutdownImmediate(reason);
                }
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameMode immediate shutdown failed; World cleanup will continue.",
                    ref terminalOutOfMemory);
            }
            finally
            {
                try
                {
                    isGameplayShutdownInProgress = false;
                    gameplayShutdownCompleted = IsGameModeShutdownComplete();
                    CompleteShutdown(reason, terminalOutOfMemory);
                }
                finally
                {
                    isGameplayShutdownInProgress = false;
                    isTerminalCleanupInProgress = false;
                    activeCameraOutputTerminalReleasePass = default;
                }
            }
        }

        internal void SetGameState(GameState value)
        {
            EnsureOwnerThread();
            if (value == null)
            {
                gameState = null;
                return;
            }

            if (!ReferenceEquals(value.World, this))
            {
                throw new InvalidOperationException("GameState must be registered with this World.");
            }

            value.ConfigureMatchClock(matchClock);
            gameState = value;
        }

        /// <summary>
        /// Binds the GameState representation received by a client integration. The Actor must
        /// already be registered with this client World. Authoritative Worlds initialize their
        /// GameState through GameMode and reject this operation.
        /// </summary>
        public void SetReplicatedGameState(GameState value)
        {
            EnsureOwnerThread();
            if (IsAuthority)
            {
                throw new InvalidOperationException(
                    "Replicated GameState can only be bound in a client World.");
            }

            if (value == null)
            {
                gameState = null;
                return;
            }

            if (!ReferenceEquals(value.World, this))
            {
                throw new InvalidOperationException(
                    "Replicated GameState must be registered with this World before binding.");
            }

            if (gameState != null && !ReferenceEquals(gameState, value))
            {
                throw new InvalidOperationException(
                    "Destroy or clear the current replicated GameState before binding another instance.");
            }

            value.ConfigureMatchClock(matchClock);
            gameState = value;
        }

        /// <summary>
        /// Publishes an initialized PlayerController received by a client integration. The
        /// Controller and its PlayerState must already be registered with this World. When a
        /// LocalPlayer is supplied, it must be the exact slot passed to InitializePlayer.
        /// </summary>
        public void CommitReplicatedPlayerController(
            PlayerController playerController,
            LocalPlayer localPlayer = null)
        {
            EnsureOwnerThread();
            if (IsAuthority)
            {
                throw new InvalidOperationException(
                    "Replicated PlayerController can only be committed in a client World.");
            }

            if (playerController == null || !ReferenceEquals(playerController.World, this))
            {
                throw new InvalidOperationException(
                    "Replicated PlayerController must be registered with this World before commit.");
            }

            if (!playerController.RuntimeComponentsInitialized)
            {
                throw new InvalidOperationException(
                    "Replicated PlayerController must be initialized before commit.");
            }

            if (!ReferenceEquals(playerController.LocalPlayer, localPlayer))
            {
                throw new InvalidOperationException(
                    "The committed LocalPlayer must match the slot used to initialize the PlayerController.");
            }

            if (localPlayer != null &&
                ((uint)localPlayer.Index >= (uint)gameInstance.LocalPlayers.Count ||
                 !ReferenceEquals(gameInstance.LocalPlayers[localPlayer.Index], localPlayer)))
            {
                throw new InvalidOperationException(
                    "LocalPlayer must belong to this World's GameInstance.");
            }

            CommitPlayerController(playerController, localPlayer);
        }

        internal void CommitPlayerController(PlayerController playerController, LocalPlayer localPlayer)
        {
            PreparePlayerControllerCommit(playerController, localPlayer);

            int controllerIndex = IndexOfPlayerControllerReference(playerController);
            PlayerState playerState = playerController.GetPlayerState();
            if (controllerIndex < 0)
            {
                playerControllers.Add(playerController);
                participantPlayerStates.Add(playerState);
            }
            else if (!ReferenceEquals(participantPlayerStates[controllerIndex], playerState))
            {
                throw new InvalidOperationException(
                    "A committed PlayerController cannot replace its managed PlayerState identity.");
            }

            if (localPlayer != null)
            {
                localPlayer.PlayerController = playerController;
            }
        }

        internal void PreparePlayerControllerCommit(
            PlayerController playerController,
            LocalPlayer localPlayer)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(playerController, null))
            {
                throw new ArgumentNullException(nameof(playerController));
            }

            if (!ReferenceEquals(playerController.World, this))
            {
                throw new InvalidOperationException("PlayerController belongs to a different World.");
            }

            if (localPlayer != null &&
                localPlayer.PlayerController != null &&
                !ReferenceEquals(localPlayer.PlayerController, playerController))
            {
                throw new InvalidOperationException(
                    $"LocalPlayer {localPlayer.Index} already has a PlayerController.");
            }

            if (IndexOfPlayerControllerReference(playerController) < 0)
            {
                int requiredCapacity = checked(playerControllers.Count + 1);
                // Login reserves both sides of the managed participant ledger before the
                // GameSession acquires ownership. Commit cannot allocate after that boundary.
                if (playerControllers.Capacity < requiredCapacity)
                {
                    playerControllers.Capacity = requiredCapacity;
                }
                if (participantPlayerStates.Capacity < requiredCapacity)
                {
                    participantPlayerStates.Capacity = requiredCapacity;
                }
            }
        }

        internal void RemovePlayerController(PlayerController playerController)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(playerController, null))
            {
                return;
            }

            int index = IndexOfPlayerControllerReference(playerController);
            if (index >= 0)
            {
                playerControllers.RemoveAt(index);
                participantPlayerStates.RemoveAt(index);
            }
            LocalPlayer localPlayer = playerController.LocalPlayer;
            if (localPlayer != null && ReferenceEquals(localPlayer.PlayerController, playerController))
            {
                localPlayer.PlayerController = null;
            }
        }

        internal void BindTerminalGameSession(GameMode owner, IGameSession session)
        {
            EnsureOwnerThread();
            if (!ReferenceEquals(owner, terminalGameModeOwner))
            {
                throw new InvalidOperationException(
                    "Only the authoritative GameMode can bind the terminal GameSession owner.");
            }
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (terminalGameSession != null && !ReferenceEquals(terminalGameSession, session))
            {
                throw new InvalidOperationException(
                    "A different terminal GameSession owner is already bound to this World.");
            }

            terminalGameSession = session;
        }

        internal bool TryReleaseParticipantOwnership(
            PlayerController playerController,
            PlayerState playerState,
            IGameSession session)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(playerController, null))
            {
                return false;
            }
            int controllerIndex = IndexOfPlayerControllerReference(playerController);
            if (controllerIndex < 0)
            {
                return true;
            }
            PlayerState retainedPlayerState = participantPlayerStates[controllerIndex];
            if (!ReferenceEquals(retainedPlayerState, playerState))
            {
                throw new InvalidOperationException(
                    "Participant cleanup must use the PlayerState identity committed to this World.");
            }
            if (session != null &&
                terminalGameSession != null &&
                !ReferenceEquals(session, terminalGameSession))
            {
                throw new InvalidOperationException(
                    "Participant cleanup must use the GameSession bound to this World.");
            }

            if (session != null)
            {
                if (session.ContainsPlayer(playerController))
                {
                    session.UnregisterPlayer(playerController);
                }
                if (session.ContainsPlayer(playerController))
                {
                    return false;
                }
            }

            GameState retainedGameState = gameState;
            if (!ReferenceEquals(retainedGameState, null))
            {
                retainedGameState.RemovePlayerState(retainedPlayerState);
            }

            RemovePlayerController(playerController);
            return IndexOfPlayerControllerReference(playerController) < 0;
        }

        internal void EnterGameModeDestructionStage(GameMode owner)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(owner, null) ||
                !ReferenceEquals(owner, terminalGameModeOwner))
            {
                throw new InvalidOperationException(
                    "Only the retained GameMode cleanup owner can enter its destruction stage.");
            }

            if (!ReferenceEquals(gameModeDestructionStageOwner, null) &&
                !ReferenceEquals(gameModeDestructionStageOwner, owner))
            {
                throw new InvalidOperationException(
                    "Another GameMode destruction stage is already active.");
            }

            gameModeDestructionStageOwner = owner;
        }

        internal void ExitGameModeDestructionStage(GameMode owner)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(gameModeDestructionStageOwner, owner))
            {
                gameModeDestructionStageOwner = null;
            }
        }

        internal void NotifyActorDestroyed(Actor actor)
        {
            if (ReferenceEquals(actor, null) || lifecycleState == WorldLifecycleState.Disposed)
            {
                return;
            }

            EnsureOwnerThread();
            GameMode participantCleanupOwner = terminalGameModeOwner;
            bool authoritativeGameModeDestroyed =
                ReferenceEquals(actor, participantCleanupOwner);
            bool activeGameModeDestroyed = authoritativeGameModeDestroyed &&
                                           (lifecycleState == WorldLifecycleState.Initializing ||
                                            lifecycleState == WorldLifecycleState.Playing);
            if (authoritativeGameModeDestroyed)
            {
                // The live Unity authority slot ends at destruction notification. The separate
                // terminal fields retain only managed ownership metadata for deterministic retry.
                gameMode = null;
            }

            PlayerState destroyedPlayerState = actor as PlayerState;
            PlayerController destroyedStateOwner = null;
            if (!ReferenceEquals(destroyedPlayerState, null) &&
                !ReferenceEquals(participantCleanupOwner, null))
            {
                TryGetPlayerControllerForState(destroyedPlayerState, out destroyedStateOwner);
            }

            if (actorIndices.TryGetValue(actor.GetStableInstanceId(), out int index))
            {
                bool removed = TryTeardownActorAt(
                    index,
                    EndPlayReason.Destroyed,
                    ActorReleasePolicy.ObserveDestroyedActor,
                    preparePlayerCameraContext: false,
                    out Exception teardownFailure);
                if (teardownFailure != null)
                {
                    throw teardownFailure;
                }
                if (!removed)
                {
                    throw new InvalidOperationException(
                        "Destroyed Actor bookkeeping retained registry ownership for retry.");
                }
            }

            if (!ReferenceEquals(destroyedStateOwner, null) &&
                CanInvokeGameModeCleanupOwner(participantCleanupOwner))
            {
                participantCleanupOwner.HandleExternallyDestroyedPlayerState(
                    destroyedStateOwner,
                    destroyedPlayerState);
            }

            if (activeGameModeDestroyed)
            {
                ShutdownImmediate(EndPlayReason.Destroyed);
            }
        }

        internal void NotifyActorEnabled(Actor actor)
        {
            EnsureOwnerThread();
            if (lifecycleState != WorldLifecycleState.Playing ||
                actor == null ||
                !actorIndices.TryGetValue(actor.GetStableInstanceId(), out int index))
            {
                return;
            }

            ActorEntry entry = actors[index];
            if (!entry.Deferred &&
                ReferenceEquals(entry.Actor, actor) &&
                actor.isActiveAndEnabled)
            {
                actor.NotifyWorldBeginPlay();
            }
        }

        internal void NotifyActorTickConfigurationChanged(
            Actor actor,
            ActorTickPhase previousPhase,
            bool previousEnabled,
            ActorTickPhase nextPhase,
            bool nextEnabled)
        {
            EnsureOwnerThread();
            if (actor == null || !actorIndices.TryGetValue(actor.GetStableInstanceId(), out int actorIndex))
            {
                return;
            }

            ActorEntry entry = actors[actorIndex];
            bool wasRegisteredForTick = entry.TickListIndex >= 0;
            if (!ReferenceEquals(entry.Actor, actor) ||
                entry.TickPhase != previousPhase ||
                wasRegisteredForTick != previousEnabled)
            {
                throw new InvalidOperationException("Actor Tick registry state is inconsistent.");
            }

            RemoveActorFromTickRegistry(ref entry);
            entry.TickPhase = nextPhase;
            if (nextEnabled)
            {
                AddActorToTickRegistry(ref entry);
            }
            actors[actorIndex] = entry;
        }

        internal bool TryAcquireCameraOutput(
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputResourceSet resources,
            out CameraOutputLease lease,
            out string error)
        {
            EnsureOwnerThread();
            if (lifecycleState != WorldLifecycleState.Initializing &&
                lifecycleState != WorldLifecycleState.Playing)
            {
                lease = default;
                error = $"World cannot acquire a camera output while in state '{lifecycleState}'.";
                return false;
            }

            if (owner == null || !ReferenceEquals(owner.World, this))
            {
                lease = default;
                error = "CameraManager must belong to this World before acquiring an output.";
                return false;
            }

            if (!cameraOutputLeaseArbiter.TryAcquire(
                    this,
                    owner,
                    output,
                    in resources,
                    out lease,
                    out error))
            {
                return false;
            }

            if ((lifecycleState == WorldLifecycleState.Initializing ||
                 lifecycleState == WorldLifecycleState.Playing) &&
                ReferenceEquals(owner.World, this))
            {
                return true;
            }

            cameraOutputLeaseArbiter.Release(this, owner, output, in lease);
            lease = default;
            error = "Camera output acquisition was interrupted by World or owner teardown.";
            return false;
        }

        internal void ReleaseCameraOutput(
            CameraManager owner,
            ICameraOutput output,
            CameraOutputLease lease)
        {
            EnsureOwnerThread();
            cameraOutputLeaseArbiter.Release(this, owner, output, in lease);
        }

        internal bool TryBeginCameraOutputTerminalReleaseAttempt(
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputLease lease)
        {
            EnsureOwnerThread();
            if (lifecycleState != WorldLifecycleState.Stopping)
            {
                return false;
            }

            return cameraOutputLeaseArbiter.TryBeginTerminalReleaseAttempt(
                this,
                owner,
                output,
                in lease,
                in activeCameraOutputTerminalReleasePass);
        }

        public void Dispose()
        {
            ShutdownImmediate(EndPlayReason.WorldShutdown);
        }

        private void DiscoverActors()
        {
            EnsureOwnerThread();
            if (actorSource == null)
            {
                return;
            }

            lifecycleScratch.Clear();
            bool collectionActive = false;
            try
            {
                EnsureActorDiscoveryTransactionActive();
                actorCollector.Begin();
                collectionActive = true;
                actorSource.CollectActors(actorCollector);
                bool capacityExceeded = actorCollector.End();
                collectionActive = false;
                EnsureActorDiscoveryTransactionActive();
                if (capacityExceeded)
                {
                    throw CreateActorCapacityException();
                }

                for (int i = 0; i < lifecycleScratch.Count; i++)
                {
                    EnsureActorDiscoveryTransactionActive();
                    Actor actor = lifecycleScratch[i];
                    if (actor == null || IsActorRegistered(actor))
                    {
                        continue;
                    }

                    RegisterActorInternal(
                        actor,
                        owned: false,
                        beginIfPlaying: true,
                        deferred: false,
                        activateOnFinish: false);
                }
            }
            finally
            {
                if (collectionActive)
                {
                    actorCollector.End();
                }

                lifecycleScratch.Clear();
            }
        }

        private void EnsureActorDiscoveryTransactionActive()
        {
            if (lifecycleState != WorldLifecycleState.Initializing)
            {
                throw new InvalidOperationException(
                    "World Actor discovery cannot continue after initialization has been interrupted.");
            }
        }

        private void UnregisterExternalActorAt(int index, EndPlayReason reason)
        {
            bool removed = TryTeardownActorAt(
                index,
                reason,
                ActorReleasePolicy.None,
                preparePlayerCameraContext: true,
                out Exception failure);
            if (failure != null)
            {
                throw failure;
            }
            if (!removed)
            {
                throw new InvalidOperationException(
                    "External Actor cleanup retained ownership for retry.");
            }
        }

        private void RegisterActorInternal(
            Actor actor,
            bool owned,
            bool beginIfPlaying,
            bool deferred,
            bool activateOnFinish)
        {
            if (!TryRegisterActorCore(
                    actor,
                    owned,
                    deferred,
                    activateOnFinish,
                    out bool registrationAdded))
            {
                throw CreateActorCapacityException();
            }

            try
            {
                BeginPlayAfterRegistration(actor, beginIfPlaying);
                if (!IsActorRegistered(actor))
                {
                    throw new InvalidOperationException(
                        $"Actor '{actor.name}' ended its lifetime while BeginPlay was being published.");
                }
            }
            catch
            {
                if (registrationAdded && IsActorRegistered(actor))
                {
                    RollbackActorRegistration(actor, EndPlayReason.InitializationFailure);
                }

                throw;
            }
        }

        private bool TryRegisterActorCore(
            Actor actor,
            bool owned,
            bool deferred,
            bool activateOnFinish,
            out bool registrationAdded)
        {
            registrationAdded = false;
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            actor.PrepareForWorldRegistration(this, allowReentry: !owned);
            int instanceId = actor.GetStableInstanceId();
            if (actorIndices.TryGetValue(instanceId, out int existingIndex))
            {
                if (owned)
                {
                    throw new InvalidOperationException(
                        "An Actor lifetime must return an independent Actor that is not already registered.");
                }

                return true;
            }

            if (actors.Count >= runtimeLimits.MaximumActorCount)
            {
                IncrementRejectedActorAdmissionCount();
                return false;
            }

            bool actorBound = false;
            bool actorTracked = false;
            int index = actors.Count;
            try
            {
                actor.BindToWorld(this, allowReentry: !owned);
                actorBound = true;
                if (actor is GameState registeringGameState)
                {
                    registeringGameState.ConfigureMatchClock(matchClock);
                }

                actors.Add(new ActorEntry
                {
                    Actor = actor,
                    Owned = owned,
                    Deferred = deferred,
                    ActivateOnFinish = activateOnFinish,
                    TickPhase = ActorTickPhase.None,
                    TickListIndex = -1,
                });
                actorTracked = true;
                if (owned)
                {
                    ownedActorCount++;
                }
                if (actors.Count > peakActorCount)
                {
                    peakActorCount = actors.Count;
                }

                actorIndices.Add(instanceId, index);

                ActorEntry registeredEntry = actors[index];
                registeredEntry.TickPhase = actor.TickPhase;
                actors[index] = registeredEntry;
                if (actor.IsActorTickEnabled())
                {
                    AddActorToTickRegistry(ref registeredEntry);
                    actors[index] = registeredEntry;
                }

                if (actor is PlayerStart playerStart)
                {
                    playerStarts.Add(playerStart);
                }

                registrationAdded = true;
                return true;
            }
            catch (Exception registrationException)
            {
                Exception rollbackException = null;
                try
                {
                    if (actorTracked &&
                        (actorIndices.ContainsKey(instanceId) ||
                         (uint)index < (uint)actors.Count &&
                         ReferenceEquals(actors[index].Actor, actor)))
                    {
                        RollbackActorRegistration(actor, EndPlayReason.InitializationFailure);
                    }
                    else if (actorBound && ReferenceEquals(actor.World, this))
                    {
                        actor.UnbindFromWorld(this, EndPlayReason.InitializationFailure);
                    }
                }
                catch (Exception exception)
                {
                    rollbackException = exception;
                }

                if (rollbackException != null)
                {
                    throw new AggregateException(
                        "Actor registration failed and registry rollback also reported an error.",
                        registrationException,
                        rollbackException);
                }

                throw;
            }
        }

        private InvalidOperationException CreateActorCapacityException()
        {
            return new InvalidOperationException(
                $"World actor capacity reached the configured limit of {runtimeLimits.MaximumActorCount}.");
        }

        private void ReleasePendingSpawn(Actor actor, ref bool pendingLifetimeRelease)
        {
            if (!pendingLifetimeRelease)
            {
                return;
            }

            // Transfer the flag before invoking external code so a throwing Release cannot be
            // attempted twice by the surrounding rollback path.
            pendingLifetimeRelease = false;
            actorLifetime.Release(actor);
        }

        private void RollbackActorRegistration(Actor actor, EndPlayReason reason)
        {
            if (ReferenceEquals(actor, null))
            {
                return;
            }

            int instanceId = actor.GetStableInstanceId();
            int index;
            if (!actorIndices.TryGetValue(instanceId, out index))
            {
                index = -1;
                for (int i = actors.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(actors[i].Actor, actor))
                    {
                        index = i;
                        break;
                    }
                }
            }

            if ((uint)index >= (uint)actors.Count ||
                !ReferenceEquals(actors[index].Actor, actor))
            {
                if (ReferenceEquals(actor.World, this))
                {
                    actor.UnbindFromWorld(this, reason);
                }
                return;
            }

            bool removed = TryTeardownActorAt(
                index,
                reason,
                ActorReleasePolicy.None,
                preparePlayerCameraContext: true,
                out Exception failure);
            if (failure != null)
            {
                throw failure;
            }
            if (!removed)
            {
                throw new InvalidOperationException(
                    "Actor registration rollback retained cleanup ownership for retry.");
            }
        }

        private void BeginPlayAfterRegistration(Actor actor, bool beginIfPlaying)
        {
            if (beginIfPlaying &&
                lifecycleState == WorldLifecycleState.Playing &&
                actor.isActiveAndEnabled)
            {
                actor.NotifyWorldBeginPlay();
            }
        }

        private void IncrementRejectedActorAdmissionCount()
        {
            if (rejectedActorAdmissionCount < long.MaxValue)
            {
                rejectedActorAdmissionCount++;
            }
        }

        private bool TryTeardownActorAt(
            int index,
            EndPlayReason reason,
            ActorReleasePolicy releasePolicy,
            bool preparePlayerCameraContext,
            out Exception failure)
        {
            failure = null;
            ActorEntry entry = actors[index];
            Actor actor = entry.Actor;
            int stableInstanceId = actor.GetStableInstanceId();

            if (!entry.TeardownDetached &&
                preparePlayerCameraContext &&
                actor is PlayerController playerController &&
                actor != null)
            {
                try
                {
                    if (!playerController.TryReleaseCameraContextForWorldTeardown())
                    {
                        return !TryResolveActorEntry(
                            actor,
                            stableInstanceId,
                            out index,
                            out entry);
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                    return !TryResolveActorEntry(
                        actor,
                        stableInstanceId,
                        out index,
                        out entry);
                }

                if (!TryResolveActorEntry(
                        actor,
                        stableInstanceId,
                        out index,
                        out entry))
                {
                    return true;
                }
            }

            if (!entry.TeardownDetached)
            {
                try
                {
                    DetachActorBookkeeping(actor);
                }
                catch (Exception exception)
                {
                    failure = exception;
                    return !TryResolveActorEntry(
                        actor,
                        stableInstanceId,
                        out index,
                        out entry);
                }

                if (!TryResolveActorEntry(
                        actor,
                        stableInstanceId,
                        out index,
                        out entry))
                {
                    return true;
                }

                entry.TeardownDetached = true;
                actors[index] = entry;
            }

            if (!entry.TeardownUnbound)
            {
                // Actor.UnbindFromWorld commits its one-shot callback boundary even when an
                // observer throws. Commit the teardown stage before invoking it so reentrant
                // registry updates cannot be overwritten by this method's earlier snapshot.
                entry.TeardownUnbound = true;
                actors[index] = entry;
                if (actor != null)
                {
                    try
                    {
                        actor.UnbindFromWorld(this, reason);
                    }
                    catch (Exception exception)
                    {
                        // Actor commits its one-shot World-unbound boundary in a finally block.
                        // Preserve the observer failure without invoking the callback twice.
                        failure = exception;
                    }
                }

                if (!TryResolveActorEntry(
                        actor,
                        stableInstanceId,
                        out index,
                        out entry))
                {
                    return true;
                }
            }

            if (!entry.TeardownLifetimeReleased)
            {
                // IActorLifetime transfers or terminates ownership before Release can throw.
                // Record that irreversible boundary before invoking external lifetime code.
                entry.TeardownLifetimeReleased = true;
                actors[index] = entry;
                try
                {
                    if (releasePolicy == ActorReleasePolicy.DestroyRegisteredActor)
                    {
                        if (entry.Owned)
                        {
                            actorLifetime.Release(actor);
                        }
                        else
                        {
                            UnityActorLifetime.ReleaseUnityActor(actor);
                        }
                    }
                    else if (releasePolicy == ActorReleasePolicy.ReleaseWorldOwnedActor &&
                             entry.Owned)
                    {
                        actorLifetime.Release(actor);
                    }
                    else if (releasePolicy == ActorReleasePolicy.ObserveDestroyedActor &&
                             entry.Owned)
                    {
                        actorLifetime.Release(actor);
                    }
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }

                if (!TryResolveActorEntry(
                        actor,
                        stableInstanceId,
                        out index,
                        out entry))
                {
                    return true;
                }
            }

            try
            {
                RemoveActorAt(index);
                return true;
            }
            catch (Exception exception)
            {
                failure ??= exception;
                return false;
            }
        }

        private bool TryResolveActorEntry(
            Actor actor,
            int stableInstanceId,
            out int index,
            out ActorEntry entry)
        {
            if (actorIndices.TryGetValue(stableInstanceId, out index) &&
                (uint)index < (uint)actors.Count)
            {
                entry = actors[index];
                if (ReferenceEquals(entry.Actor, actor))
                {
                    return true;
                }
            }

            for (int candidateIndex = actors.Count - 1; candidateIndex >= 0; candidateIndex--)
            {
                ActorEntry candidate = actors[candidateIndex];
                if (!ReferenceEquals(candidate.Actor, actor))
                {
                    continue;
                }

                index = candidateIndex;
                entry = candidate;
                return true;
            }

            index = -1;
            entry = default;
            return false;
        }

        private ActorEntry RemoveActorAt(int index)
        {
            int lastIndex = actors.Count - 1;
            ActorEntry removed = actors[index];
            RemoveActorFromTickRegistry(ref removed);
            actorIndices.Remove(removed.Actor.GetStableInstanceId());
            if (removed.Owned)
            {
                ownedActorCount--;
            }

            if (index != lastIndex)
            {
                ActorEntry moved = actors[lastIndex];
                actors[index] = moved;
                actorIndices[moved.Actor.GetStableInstanceId()] = index;
            }

            actors.RemoveAt(lastIndex);
            if (removed.Actor is PlayerStart playerStart)
            {
                playerStarts.Remove(playerStart);
            }

            return removed;
        }

        private void BeginPlayForRegisteredActors()
        {
            lifecycleScratch.Clear();
            for (int i = 0; i < actors.Count; i++)
            {
                ActorEntry entry = actors[i];
                if (!entry.Deferred && entry.Actor != null)
                {
                    lifecycleScratch.Add(entry.Actor);
                }
            }

            try
            {
                for (int i = 0; i < lifecycleScratch.Count; i++)
                {
                    Actor actor = lifecycleScratch[i];
                    if (actor != null &&
                        IsActorRegistered(actor) &&
                        actor.isActiveAndEnabled)
                    {
                        actor.NotifyWorldBeginPlay();
                    }
                }
            }
            finally
            {
                lifecycleScratch.Clear();
            }
        }

        private OutOfMemoryException BeginStopping(EndPlayReason reason)
        {
            if (lifecycleState == WorldLifecycleState.Stopping ||
                lifecycleState == WorldLifecycleState.Stopped ||
                lifecycleState == WorldLifecycleState.Disposed)
            {
                return null;
            }

            tickDispatchReady = false;
            lifecycleState = WorldLifecycleState.Stopping;
            shutdownReason = reason;
            activeCameraOutputTerminalReleasePass =
                cameraOutputLeaseArbiter.BeginTerminalReleasePass(this);
            OutOfMemoryException terminalOutOfMemory = null;
            try
            {
                lifetimeCancellation.Cancel();
            }
            catch (Exception exception)
            {
                // Cancellation observers are not allowed to interrupt ownership cleanup.
                LogTerminalException(
                    exception,
                    "A World lifetime cancellation observer failed; ownership cleanup will continue.",
                    ref terminalOutOfMemory);
            }

            return terminalOutOfMemory;
        }

        private void RetryTerminalCleanup()
        {
            if (isTerminalCleanupInProgress)
            {
                throw new InvalidOperationException(
                    "World terminal cleanup is already in progress.");
            }

            activeCameraOutputTerminalReleasePass =
                cameraOutputLeaseArbiter.BeginTerminalReleasePass(this);
            isTerminalCleanupInProgress = true;
            try
            {
                OutOfMemoryException retryOutOfMemory = null;
                RetryGameplayShutdownImmediate(ref retryOutOfMemory);
                CompleteShutdown(shutdownReason, retryOutOfMemory);
            }
            finally
            {
                isGameplayShutdownInProgress = false;
                isTerminalCleanupInProgress = false;
                activeCameraOutputTerminalReleasePass = default;
            }
        }

        private void RetryGameplayShutdownImmediate(
            ref OutOfMemoryException terminalOutOfMemory)
        {
            if (IsGameModeShutdownComplete())
            {
                gameplayShutdownCompleted = true;
                return;
            }

            isGameplayShutdownInProgress = true;
            try
            {
                GameMode cleanupOwner = terminalGameModeOwner;
                if (CanInvokeGameModeCleanupOwner(cleanupOwner))
                {
                    cleanupOwner.ShutdownImmediate(shutdownReason);
                }
                else if (!ReferenceEquals(cleanupOwner, null) &&
                         gameMode == null &&
                         ReferenceEquals(gameModeDestructionStageOwner, null))
                {
                    RetryParticipantCleanupWithoutGameMode(ref terminalOutOfMemory);
                }
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameMode terminal cleanup retry failed; participant ownership remains available.",
                    ref terminalOutOfMemory);
            }
            finally
            {
                isGameplayShutdownInProgress = false;
                gameplayShutdownCompleted = IsGameModeShutdownComplete();
            }
        }

        private bool IsGameModeShutdownComplete()
        {
            GameMode cleanupOwner = terminalGameModeOwner;
            if (ReferenceEquals(cleanupOwner, null))
            {
                return true;
            }

            // Once Unity destroys the authoritative GameMode, its retained managed identity
            // is ownership metadata only. Never dispatch properties or callbacks through it.
            if (gameMode == null)
            {
                return playerControllers.Count == 0;
            }

            return cleanupOwner.ModeState == GameModeLifecycleState.Stopped ||
                   cleanupOwner.ModeState == GameModeLifecycleState.Uninitialized;
        }

        private bool CanInvokeGameModeCleanupOwner(GameMode cleanupOwner)
        {
            return !ReferenceEquals(cleanupOwner, null) &&
                   cleanupOwner != null &&
                   ReferenceEquals(cleanupOwner, gameMode) &&
                   !ReferenceEquals(cleanupOwner, gameModeDestructionStageOwner);
        }

        private void RetryParticipantCleanupWithoutGameMode(
            ref OutOfMemoryException terminalOutOfMemory)
        {
            while (playerControllers.Count > 0)
            {
                int controllerCount = playerControllers.Count;
                PlayerController playerController = playerControllers[controllerCount - 1];
                PlayerState playerState = participantPlayerStates[controllerCount - 1];

                try
                {
                    playerController.UnPossess();
                }
                catch (Exception exception)
                {
                    LogTerminalException(
                        exception,
                        "PlayerController failed to release possession during retained participant cleanup.",
                        ref terminalOutOfMemory);
                }

                if (playerController.GetPawn() != null)
                {
                    break;
                }

                try
                {
                    if (!TryReleaseParticipantOwnership(
                            playerController,
                            playerState,
                            terminalGameSession))
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    LogTerminalException(
                        exception,
                        "Retained participant ownership cleanup failed; the World will keep it for retry.",
                        ref terminalOutOfMemory);
                    break;
                }

                if (playerControllers.Count >= controllerCount)
                {
                    break;
                }
            }
        }

        private void CompleteShutdown(
            EndPlayReason reason,
            OutOfMemoryException terminalOutOfMemory)
        {
            pendingGameplayCleanup =
                !gameplayShutdownCompleted || isGameplayShutdownInProgress;
            if (pendingGameplayCleanup)
            {
                if (terminalOutOfMemory != null)
                {
                    throw terminalOutOfMemory;
                }

                throw shutdownIncompleteException;
            }

            for (int index = actors.Count - 1; index >= 0; index--)
            {
                bool removed = TryTeardownActorAt(
                    index,
                    reason,
                    ActorReleasePolicy.ReleaseWorldOwnedActor,
                    preparePlayerCameraContext: true,
                    out Exception teardownFailure);
                if (teardownFailure != null)
                {
                    LogTerminalException(
                        teardownFailure,
                        removed
                            ? "Actor teardown completed after reporting an observer or lifetime failure."
                            : "Actor teardown retained registry ownership for retry.",
                        ref terminalOutOfMemory);
                }
            }

            pendingActorCleanup = actors.Count != 0;
            if (!pendingActorCleanup)
            {
                playerControllers.Clear();
                participantPlayerStates.Clear();
                playerStarts.Clear();
                updateTickActors.Clear();
                fixedUpdateTickActors.Clear();
                lateUpdateTickActors.Clear();
                gameMode = null;
                terminalGameModeOwner = null;
                gameModeDestructionStageOwner = null;
                terminalGameSession = null;
                gameState = null;
            }

            bool cameraOutputsReleased = false;
            try
            {
                cameraOutputsReleased = cameraOutputLeaseArbiter.TryReleaseAll(
                    this,
                    in activeCameraOutputTerminalReleasePass);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "Camera output lease release failed during World shutdown; terminal cleanup will continue.",
                    ref terminalOutOfMemory);
            }

            pendingCameraOutputCleanup = !cameraOutputsReleased;

            if (!pendingActorCleanup &&
                !pendingCameraOutputCleanup &&
                !definition.IsDisposed)
            {
                try
                {
                    definition.Dispose();
                }
                catch (Exception exception)
                {
                    LogTerminalException(
                        exception,
                        "World definition disposal failed; the retained leases remain available for retry.",
                        ref terminalOutOfMemory);
                }
            }

            if (!pendingActorCleanup &&
                !pendingCameraOutputCleanup &&
                definition.IsDisposed &&
                !lifetimeCancellationDisposed)
            {
                try
                {
                    lifetimeCancellation.Dispose();
                    lifetimeCancellationDisposed = true;
                }
                catch (Exception exception)
                {
                    LogTerminalException(
                        exception,
                        "World lifetime-token disposal failed; the token owner remains available for retry.",
                        ref terminalOutOfMemory);
                }
            }

            pendingLifetimeTokenCleanup = !lifetimeCancellationDisposed;

            if (pendingActorCleanup ||
                pendingCameraOutputCleanup ||
                !definition.IsDisposed ||
                !lifetimeCancellationDisposed)
            {
                if (terminalOutOfMemory != null)
                {
                    throw terminalOutOfMemory;
                }

                throw shutdownIncompleteException;
            }

            pendingGameplayCleanup = false;
            pendingActorCleanup = false;
            pendingCameraOutputCleanup = false;
            pendingLifetimeTokenCleanup = false;
            lifecycleState = WorldLifecycleState.Disposed;
            try
            {
                gameInstance.NotifyWorldDisposed(this);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameInstance notification failed after World disposal.",
                    ref terminalOutOfMemory);
            }

            if (terminalOutOfMemory != null)
            {
                throw terminalOutOfMemory;
            }
        }

        private static void LogTerminalException(
            Exception exception,
            string message,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            if (TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception))
            {
                return;
            }

            try
            {
                Log.Error(exception, message);
            }
            catch (Exception loggingException)
            {
                TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, loggingException);
            }
        }

        private static bool TryCaptureTerminalOutOfMemory(
            ref OutOfMemoryException terminalOutOfMemory,
            Exception exception)
        {
            OutOfMemoryException captured = FindOutOfMemory(exception);
            if (captured == null)
            {
                return false;
            }

            CaptureTerminalOutOfMemory(ref terminalOutOfMemory, captured);
            return true;
        }

        private static void CaptureTerminalOutOfMemory(
            ref OutOfMemoryException terminalOutOfMemory,
            OutOfMemoryException exception)
        {
            if (terminalOutOfMemory == null)
            {
                terminalOutOfMemory = exception;
            }
        }

        private static OutOfMemoryException FindOutOfMemory(Exception exception)
        {
            if (exception is OutOfMemoryException outOfMemoryException)
            {
                return outOfMemoryException;
            }

            if (exception is AggregateException aggregateException)
            {
                for (int index = 0; index < aggregateException.InnerExceptions.Count; index++)
                {
                    OutOfMemoryException nested = FindOutOfMemory(
                        aggregateException.InnerExceptions[index]);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private void DetachActorBookkeeping(Actor actor)
        {
            if (actor is PlayerController playerController)
            {
                if (IndexOfPlayerControllerReference(playerController) >= 0)
                {
                    GameMode cleanupOwner = terminalGameModeOwner;
                    if (!ReferenceEquals(cleanupOwner, null))
                    {
                        cleanupOwner.HandleExternallyDestroyedPlayerController(playerController);
                    }
                }

                RemovePlayerController(playerController);
            }

            if (ReferenceEquals(actor, gameMode))
            {
                gameMode = null;
            }

            if (ReferenceEquals(actor, gameState))
            {
                gameState = null;
            }
        }

        private int IndexOfPlayerControllerReference(PlayerController playerController)
        {
            if (ReferenceEquals(playerController, null))
            {
                return -1;
            }

            for (int index = 0; index < playerControllers.Count; index++)
            {
                if (ReferenceEquals(playerControllers[index], playerController))
                {
                    return index;
                }
            }

            return -1;
        }

        private bool TryGetPlayerControllerForState(
            PlayerState playerState,
            out PlayerController playerController)
        {
            for (int i = 0; i < playerControllers.Count; i++)
            {
                PlayerController candidate = playerControllers[i];
                if (!ReferenceEquals(candidate, null) &&
                    ReferenceEquals(participantPlayerStates[i], playerState))
                {
                    playerController = candidate;
                    return true;
                }
            }

            playerController = null;
            return false;
        }

        private void EnsureAcceptingActors()
        {
            if (lifecycleState != WorldLifecycleState.Initializing &&
                lifecycleState != WorldLifecycleState.Playing)
            {
                throw new InvalidOperationException(
                    $"World does not accept actors while in state '{lifecycleState}'.");
            }
        }

        private bool CanDispatchActorTick(Actor actor, ActorTickPhase phase)
        {
            if (actor == null ||
                !actor.HasBegunPlay ||
                !actor.isActiveAndEnabled ||
                !actor.IsActorTickEnabled() ||
                actor.TickPhase != phase ||
                !actorIndices.TryGetValue(actor.GetStableInstanceId(), out int actorIndex))
            {
                return false;
            }

            ActorEntry entry = actors[actorIndex];
            return !entry.Deferred &&
                   entry.TickPhase == phase &&
                   entry.TickListIndex >= 0 &&
                   ReferenceEquals(entry.Actor, actor);
        }

        private void AddActorToTickRegistry(ref ActorEntry entry)
        {
            if (entry.TickPhase == ActorTickPhase.None || entry.TickListIndex >= 0)
            {
                throw new InvalidOperationException("Actor Tick registration requires an unregistered dispatchable phase.");
            }

            List<Actor> tickActors = GetTickActorList(entry.TickPhase);
            // Capacity growth is paid on the registration cold path rather than the first
            // PlayerLoop dispatch after a population increase.
            int requiredCount = tickActors.Count + 1;
            if (tickScratch.Capacity < requiredCount)
            {
                tickScratch.Capacity = Math.Max(requiredCount, tickActors.Capacity);
            }

            entry.TickListIndex = tickActors.Count;
            tickActors.Add(entry.Actor);
        }

        private void RemoveActorFromTickRegistry(ref ActorEntry entry)
        {
            if (entry.TickListIndex < 0)
            {
                return;
            }

            if (entry.TickPhase == ActorTickPhase.None)
            {
                throw new InvalidOperationException("Actor Tick registry contains an entry without a dispatchable phase.");
            }

            List<Actor> tickActors = GetTickActorList(entry.TickPhase);
            int removeIndex = entry.TickListIndex;
            int lastIndex = tickActors.Count - 1;
            if ((uint)removeIndex >= (uint)tickActors.Count ||
                !ReferenceEquals(tickActors[removeIndex], entry.Actor))
            {
                throw new InvalidOperationException("Actor Tick registry index is inconsistent.");
            }

            if (removeIndex != lastIndex)
            {
                Actor movedActor = tickActors[lastIndex];
                tickActors[removeIndex] = movedActor;
                if (ReferenceEquals(movedActor, null) ||
                    !actorIndices.TryGetValue(movedActor.GetStableInstanceId(), out int movedActorIndex))
                {
                    throw new InvalidOperationException("Actor Tick registry contains an unregistered Actor.");
                }

                ActorEntry movedEntry = actors[movedActorIndex];
                movedEntry.TickListIndex = removeIndex;
                actors[movedActorIndex] = movedEntry;
            }

            tickActors.RemoveAt(lastIndex);
            entry.TickListIndex = -1;
        }

        private List<Actor> GetTickActorList(ActorTickPhase phase)
        {
            switch (phase)
            {
                case ActorTickPhase.Update:
                    return updateTickActors;
                case ActorTickPhase.FixedUpdate:
                    return fixedUpdateTickActors;
                case ActorTickPhase.LateUpdate:
                    return lateUpdateTickActors;
                default:
                    throw new ArgumentOutOfRangeException(nameof(phase), phase, "A dispatchable Actor Tick phase is required.");
            }
        }

        internal static void ValidateNetMode(WorldNetMode netMode)
        {
            if (netMode != WorldNetMode.Standalone &&
                netMode != WorldNetMode.Client &&
                netMode != WorldNetMode.ListenServer &&
                netMode != WorldNetMode.DedicatedServer)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(netMode),
                    netMode,
                    "World network mode is not defined.");
            }
        }

        internal static void ValidateTickRequest(ActorTickPhase phase, float deltaSeconds)
        {
            if (phase == ActorTickPhase.None || phase > ActorTickPhase.LateUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "A dispatchable Actor Tick phase is required.");
            }

            if (deltaSeconds < 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Tick delta must be finite and non-negative.");
            }
        }

        private void TransitionTo(WorldLifecycleState next)
        {
            bool valid = lifecycleState == WorldLifecycleState.Created && next == WorldLifecycleState.Initializing ||
                         lifecycleState == WorldLifecycleState.Initializing && next == WorldLifecycleState.Playing;

            if (!valid)
            {
                throw new InvalidOperationException(
                    $"Illegal World lifecycle transition: {lifecycleState} -> {next}.");
            }

            lifecycleState = next;
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "World live-state access must run on the GameInstance owner thread.");
            }
        }

    }
}
