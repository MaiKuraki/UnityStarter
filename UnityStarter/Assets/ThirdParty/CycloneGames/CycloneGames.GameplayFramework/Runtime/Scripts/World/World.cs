using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly List<ActorEntry> actors;
        private readonly List<Actor> lifecycleScratch;
        private readonly List<Actor> updateTickActors;
        private readonly List<Actor> fixedUpdateTickActors;
        private readonly List<Actor> lateUpdateTickActors;
        private readonly List<Actor> tickScratch;
        private readonly Dictionary<int, int> actorIndices;
        private readonly List<PlayerController> playerControllers = new List<PlayerController>(8);
        private readonly List<PlayerStart> playerStarts = new List<PlayerStart>(16);
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        private WorldLifecycleState lifecycleState = WorldLifecycleState.Created;
        private GameMode gameMode;
        private GameState gameState;
        private int ownedActorCount;
        private int peakActorCount;
        private long rejectedActorAdmissionCount;
        private ReadOnlyCollection<PlayerController> playerControllerView;
        private ReadOnlyCollection<PlayerStart> playerStartView;
        private bool tickDispatchReady;
        private bool isDispatchingActorTick;
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

        public GameInstance GameInstance => gameInstance;
        public WorldRuntimeLimits RuntimeLimits => runtimeLimits;
        public IMatchClock MatchClock => matchClock;
        public WorldDefinition Definition => definition;
        public WorldNetMode NetMode { get; }
        public WorldLifecycleState LifecycleState => lifecycleState;
        public bool IsAuthority => NetMode != WorldNetMode.Client;
        public bool IsDedicatedServer => NetMode == WorldNetMode.DedicatedServer;
        public GameMode GameMode => gameMode;
        public GameState GameState => gameState;
        public IReadOnlyList<PlayerController> PlayerControllers =>
            playerControllerView ??= playerControllers.AsReadOnly();
        public IReadOnlyList<PlayerStart> PlayerStarts =>
            playerStartView ??= playerStarts.AsReadOnly();
        public int ActorCount => actors.Count;
        public int PeakActorCount => peakActorCount;
        public int OwnedActorCount => ownedActorCount;
        public int PlayerControllerCount => playerControllers.Count;
        public int PlayerStartCount => playerStarts.Count;
        public long RejectedActorAdmissionCount => rejectedActorAdmissionCount;
        public CancellationToken LifetimeToken => lifetimeCancellation.Token;
        public ISceneTransitionHandler SceneTransitionHandler => sceneTransitionHandler;
        public bool IsDispatchingActorTick => isDispatchingActorTick;
        public ActorTickPhase ActiveTickPhase => activeTickPhase;

        public GameInstance GetGameInstance() => gameInstance;
        public GameMode GetAuthGameMode() => IsAuthority ? gameMode : null;
        public T GetAuthGameMode<T>() where T : GameMode => GetAuthGameMode() as T;
        public GameState GetGameState() => gameState;
        public T GetGameState<T>() where T : GameState => gameState as T;
        public PlayerController GetFirstPlayerController() => GetPlayerController(0);

        public PlayerController GetPlayerController(int index)
        {
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
                        // One Actor cannot starve the rest of the phase. Exceptions remain
                        // observable through the framework logging pipeline.
                        Log.Error(
                            exception,
                            $"Actor '{actor.name}' Tick failed during '{phase}'; dispatch will continue with the remaining actors.");
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

        public int GetTickActorCount(ActorTickPhase phase)
        {
            return GetTickActorList(phase).Count;
        }

        public bool ContainsPlayerController(PlayerController playerController)
        {
            return !ReferenceEquals(playerController, null) && playerControllers.Contains(playerController);
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
                try
                {
                    if (releaseRegisteredInstance)
                    {
                        RollbackActorRegistration(
                            instance,
                            EndPlayReason.InitializationFailure);
                    }
                }
                catch (Exception exception)
                {
                    cleanupException = exception;
                }

                if (releaseRegisteredInstance)
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
            return actor != null && actorIndices.ContainsKey(actor.GetStableInstanceId());
        }

        public bool TryGetActor(int instanceId, out Actor actor)
        {
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
                playerControllers.Contains(playerController) &&
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

            ActorEntry entry = RemoveActorAt(index);
            Actor actorToRelease = entry.Actor;
            DetachActorBookkeeping(entry.Actor);
            try
            {
                entry.Actor.UnbindFromWorld(this, reason);
            }
            finally
            {
                if (entry.Owned)
                {
                    actorLifetime.Release(actorToRelease);
                }
                else
                {
                    UnityActorLifetime.ReleaseUnityActor(actorToRelease);
                }
            }

            return true;
        }

        public async UniTask ShutdownAsync(
            EndPlayReason reason = EndPlayReason.WorldShutdown,
            CancellationToken cancellationToken = default)
        {
            EnsureOwnerThread();
            if (lifecycleState == WorldLifecycleState.Disposed ||
                lifecycleState == WorldLifecycleState.Stopped ||
                lifecycleState == WorldLifecycleState.Stopping)
            {
                return;
            }

            // Shutdown is non-cancellable once requested so ownership cleanup cannot be left
            // half-complete. The token is reserved for future bounded adapter waits.
            _ = cancellationToken;
            BeginStopping();

            try
            {
                if (gameMode != null)
                {
                    await gameMode.ShutdownAsync(reason);
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                EnsureOwnerThread();
                CompleteShutdown(reason);
            }
        }

        internal void AbortInitialization()
        {
            EnsureOwnerThread();
            if (lifecycleState == WorldLifecycleState.Disposed ||
                lifecycleState == WorldLifecycleState.Stopping)
            {
                return;
            }

            BeginStopping();
            try
            {
                gameMode?.ShutdownImmediate(EndPlayReason.InitializationFailure);
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "GameMode shutdown after World initialization failure failed; World cleanup will continue.");
            }
            finally
            {
                CompleteShutdown(EndPlayReason.InitializationFailure);
            }
        }

        internal void ShutdownImmediate(EndPlayReason reason)
        {
            EnsureOwnerThread();
            if (lifecycleState == WorldLifecycleState.Disposed ||
                lifecycleState == WorldLifecycleState.Stopping)
            {
                return;
            }

            BeginStopping();
            try
            {
                gameMode?.ShutdownImmediate(reason);
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    $"GameMode immediate shutdown failed for reason '{reason}'; World cleanup will continue.");
            }
            finally
            {
                CompleteShutdown(reason);
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
            EnsureOwnerThread();
            if (playerController == null)
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

            // Validate every relationship before publishing the Controller to the World roster.
            // Once the list mutation commits, the remaining LocalPlayer assignment cannot fail.
            if (!playerControllers.Contains(playerController))
            {
                playerControllers.Add(playerController);
            }

            if (localPlayer != null)
            {
                localPlayer.PlayerController = playerController;
            }
        }

        internal void RemovePlayerController(PlayerController playerController)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(playerController, null))
            {
                return;
            }

            playerControllers.Remove(playerController);
            LocalPlayer localPlayer = playerController.LocalPlayer;
            if (localPlayer != null && ReferenceEquals(localPlayer.PlayerController, playerController))
            {
                localPlayer.PlayerController = null;
            }
        }

        internal void NotifyActorDestroyed(Actor actor)
        {
            if (ReferenceEquals(actor, null) || lifecycleState == WorldLifecycleState.Disposed)
            {
                return;
            }

            EnsureOwnerThread();
            if (!actorIndices.TryGetValue(actor.GetStableInstanceId(), out int index))
            {
                return;
            }

            bool activeGameModeDestroyed = ReferenceEquals(actor, gameMode) &&
                                           (lifecycleState == WorldLifecycleState.Initializing ||
                                            lifecycleState == WorldLifecycleState.Playing);
            PlayerController destroyedStateOwner = null;
            if (actor is PlayerState destroyedPlayerState && gameMode != null)
            {
                TryGetPlayerControllerForState(destroyedPlayerState, out destroyedStateOwner);
            }

            ActorEntry entry = RemoveActorAt(index);
            DetachActorBookkeeping(entry.Actor);
            if (entry.Owned)
            {
                try
                {
                    actorLifetime.Release(entry.Actor);
                }
                catch (Exception exception)
                {
                    Log.Error(
                        exception,
                        "The Actor lifetime failed to observe an externally destroyed World-owned Actor; cleanup will continue.");
                }
            }

            if (destroyedStateOwner != null)
            {
                gameMode?.Logout(destroyedStateOwner);
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
            ActorEntry entry = RemoveActorAt(index);
            DetachActorBookkeeping(entry.Actor);
            entry.Actor.UnbindFromWorld(this, reason);
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

            if (actor.World != null && !ReferenceEquals(actor.World, this))
            {
                throw new InvalidOperationException($"Actor '{actor.name}' already belongs to another World.");
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

            ActorEntry entry = RemoveActorAt(index);
            DetachActorBookkeeping(entry.Actor);
            entry.Actor.UnbindFromWorld(this, reason);
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

        private void BeginStopping()
        {
            if (lifecycleState == WorldLifecycleState.Stopping ||
                lifecycleState == WorldLifecycleState.Stopped ||
                lifecycleState == WorldLifecycleState.Disposed)
            {
                return;
            }

            tickDispatchReady = false;
            lifecycleState = WorldLifecycleState.Stopping;
            try
            {
                lifetimeCancellation.Cancel();
            }
            catch (Exception exception)
            {
                // Cancellation observers are not allowed to interrupt ownership cleanup.
                Log.Error(
                    exception,
                    "A World lifetime cancellation observer failed; ownership cleanup will continue.");
            }
        }

        private void CompleteShutdown(EndPlayReason reason)
        {
            while (actors.Count > 0)
            {
                ActorEntry entry = RemoveActorAt(actors.Count - 1);
                Actor actor = entry.Actor;
                if (actor == null)
                {
                    continue;
                }

                DetachActorBookkeeping(actor);
                string actorName = actor.name;
                Actor actorToRelease = entry.Owned ? actor : null;
                try
                {
                    actor.UnbindFromWorld(this, reason);
                }
                catch (Exception exception)
                {
                    Log.Error(
                        exception,
                        $"Actor '{actorName}' failed to unbind during World shutdown for reason '{reason}'.");
                }
                finally
                {
                    if (entry.Owned)
                    {
                        try
                        {
                            actorLifetime.Release(actorToRelease);
                        }
                        catch (Exception exception)
                        {
                            Log.Error(
                                exception,
                                $"Actor '{actorName}' lifetime release failed during World shutdown; cleanup will continue.");
                        }
                    }
                }
            }

            playerControllers.Clear();
            playerStarts.Clear();
            updateTickActors.Clear();
            fixedUpdateTickActors.Clear();
            lateUpdateTickActors.Clear();
            try
            {
                cameraOutputLeaseArbiter.ReleaseAll(this);
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "Camera output lease release failed during World shutdown; terminal cleanup will continue.");
            }

            gameMode = null;
            gameState = null;
            lifecycleState = WorldLifecycleState.Stopped;

            definition.Dispose();
            lifetimeCancellation.Dispose();
            lifecycleState = WorldLifecycleState.Disposed;
            gameInstance.NotifyWorldDisposed(this);
        }

        private void DetachActorBookkeeping(Actor actor)
        {
            if (actor is PlayerController playerController)
            {
                if (playerControllers.Contains(playerController))
                {
                    gameMode?.HandleExternallyDestroyedPlayerController(playerController);
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

        private bool TryGetPlayerControllerForState(
            PlayerState playerState,
            out PlayerController playerController)
        {
            for (int i = 0; i < playerControllers.Count; i++)
            {
                PlayerController candidate = playerControllers[i];
                if (!ReferenceEquals(candidate, null) &&
                    ReferenceEquals(candidate.GetPlayerState(), playerState))
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
                    "World mutation must run on the GameInstance owner thread.");
            }
        }

    }
}
