using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using CycloneGames.Factory.Runtime;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
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
        private struct ActorEntry
        {
            public Actor Actor;
            public bool Owned;
            public bool Deferred;
            public bool ActivateOnFinish;
            public ActorTickPhase TickPhase;
            public int TickListIndex;
        }

        private struct CameraBrainOwnership
        {
            public CameraManager Owner;
            public CinemachineBrain Brain;
            public CinemachineBrain.UpdateMethods PreviousUpdateMethod;
        }

        private readonly GameInstance gameInstance;
        private readonly IUnityObjectSpawner objectSpawner;
        private readonly WorldDefinition definition;
        private readonly IGameSession configuredGameSession;
        private readonly ISceneTransitionHandler sceneTransitionHandler;
        private readonly int ownerThreadId;
        private readonly List<ActorEntry> actors = new List<ActorEntry>(128);
        private readonly List<Actor> lifecycleScratch = new List<Actor>(128);
        private readonly List<Actor> updateTickActors = new List<Actor>(128);
        private readonly List<Actor> fixedUpdateTickActors = new List<Actor>(32);
        private readonly List<Actor> lateUpdateTickActors = new List<Actor>(32);
        private readonly List<Actor> tickScratch = new List<Actor>(128);
        private readonly Dictionary<int, int> actorIndices = new Dictionary<int, int>(128);
        private readonly List<PlayerController> playerControllers = new List<PlayerController>(8);
        private readonly List<PlayerStart> playerStarts = new List<PlayerStart>(16);
        private readonly Dictionary<int, CameraBrainOwnership> cameraBrainOwners =
            new Dictionary<int, CameraBrainOwnership>(4);
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        private WorldLifecycleState lifecycleState = WorldLifecycleState.Created;
        private GameMode gameMode;
        private GameState gameState;
        private int ownedActorCount;
        private ReadOnlyCollection<PlayerController> playerControllerView;
        private ReadOnlyCollection<PlayerStart> playerStartView;
        private bool tickDispatchReady;
        private bool isDispatchingActorTick;
        private ActorTickPhase activeTickPhase;

        internal World(
            GameInstance gameInstance,
            IUnityObjectSpawner objectSpawner,
            WorldDefinition definition,
            WorldNetMode netMode,
            IGameSession gameSession,
            ISceneTransitionHandler sceneTransitionHandler,
            int ownerThreadId)
        {
            this.gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            this.objectSpawner = objectSpawner ?? throw new ArgumentNullException(nameof(objectSpawner));
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            configuredGameSession = gameSession;
            this.sceneTransitionHandler = sceneTransitionHandler;
            this.ownerThreadId = ownerThreadId;
            NetMode = netMode;
        }

        public GameInstance GameInstance => gameInstance;
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
        public int OwnedActorCount => ownedActorCount;
        public CancellationToken LifetimeToken => lifetimeCancellation.Token;
        public ISceneTransitionHandler SceneTransitionHandler => sceneTransitionHandler;
        public bool IsDispatchingActorTick => isDispatchingActorTick;
        public ActorTickPhase ActiveTickPhase => activeTickPhase;

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
                        // observable with the failing Actor as the Unity log context.
                        Debug.LogException(exception, actor);
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

            DiscoverSceneActors();

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
            return SpawnActorInternal(prefab, beginIfPlaying: true);
        }

        /// <summary>
        /// Spawns and registers an Actor without publishing BeginPlay in an already-running
        /// World. Configure dependencies, then call <see cref="FinishSpawningActor"/>.
        /// </summary>
        public T SpawnActorDeferred<T>(T prefab) where T : Actor
        {
            return SpawnActorInternal(prefab, beginIfPlaying: false);
        }

        public void FinishSpawningActor(Actor actor)
        {
            EnsureOwnerThread();
            if (actor == null || !actorIndices.TryGetValue(actor.GetInstanceID(), out int index))
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

        private T SpawnActorInternal<T>(T prefab, bool beginIfPlaying) where T : Actor
        {
            EnsureOwnerThread();
            EnsureAcceptingActors();

            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            T instance = objectSpawner.Create(prefab);
            if (instance == null)
            {
                throw new InvalidOperationException($"The object spawner returned null for '{prefab.name}'.");
            }

            try
            {
                bool deferred = !beginIfPlaying;
                bool activateOnFinish = deferred && instance.gameObject.activeSelf;
                if (activateOnFinish)
                {
                    instance.gameObject.SetActive(false);
                }

                RegisterActorInternal(
                    instance,
                    owned: true,
                    beginIfPlaying,
                    deferred,
                    activateOnFinish);
                return instance;
            }
            catch
            {
                DestroyUnityObject(instance.gameObject);
                throw;
            }
        }

        /// <summary>
        /// Registers a scene- or externally-created Actor without transferring destruction
        /// ownership. The Actor still receives world BeginPlay/EndPlay notifications.
        /// </summary>
        public void RegisterActor(Actor actor)
        {
            EnsureOwnerThread();
            EnsureAcceptingActors();
            RegisterActorInternal(actor, owned: false, beginIfPlaying: true, deferred: false, activateOnFinish: false);
        }

        public bool IsActorRegistered(Actor actor)
        {
            return actor != null && actorIndices.ContainsKey(actor.GetInstanceID());
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
            if (actor == null || !actorIndices.TryGetValue(actor.GetInstanceID(), out int index))
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
            GameObject actorObject = entry.Actor.gameObject;
            DetachActorBookkeeping(entry.Actor);
            try
            {
                entry.Actor.UnbindFromWorld(this, reason);
            }
            finally
            {
                DestroyUnityObject(actorObject);
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
                Debug.LogException(exception);
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
                Debug.LogException(exception);
            }
            finally
            {
                CompleteShutdown(reason);
            }
        }

        internal void SetGameState(GameState value)
        {
            EnsureOwnerThread();
            if (value != null && !ReferenceEquals(value.World, this))
            {
                throw new InvalidOperationException("GameState must be registered with this World.");
            }

            gameState = value;
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

            if (!playerControllers.Contains(playerController))
            {
                playerControllers.Add(playerController);
            }

            if (localPlayer != null)
            {
                if (localPlayer.PlayerController != null &&
                    !ReferenceEquals(localPlayer.PlayerController, playerController))
                {
                    throw new InvalidOperationException(
                        $"LocalPlayer {localPlayer.Index} already has a PlayerController.");
                }

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
            if (!actorIndices.TryGetValue(actor.GetInstanceID(), out int index))
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
                !actorIndices.TryGetValue(actor.GetInstanceID(), out int index))
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
            if (actor == null || !actorIndices.TryGetValue(actor.GetInstanceID(), out int actorIndex))
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

        internal bool TryAcquireCameraBrain(
            CameraManager owner,
            CinemachineBrain brain,
            out int ownershipId,
            out string error)
        {
            EnsureOwnerThread();
            ownershipId = 0;
            if (owner == null || brain == null)
            {
                error = "CameraManager and CinemachineBrain are required.";
                return false;
            }

            int id = brain.GetInstanceID();
            if (cameraBrainOwners.TryGetValue(id, out CameraBrainOwnership existing))
            {
                if (ReferenceEquals(existing.Owner, owner))
                {
                    ownershipId = id;
                    error = null;
                    return true;
                }

                error = $"CinemachineBrain '{brain.name}' is already owned by '{existing.Owner?.name}'.";
                return false;
            }

            cameraBrainOwners.Add(id, new CameraBrainOwnership
            {
                Owner = owner,
                Brain = brain,
                PreviousUpdateMethod = brain.UpdateMethod,
            });
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            ownershipId = id;
            error = null;
            return true;
        }

        internal void ReleaseCameraBrain(CameraManager owner, int ownershipId)
        {
            EnsureOwnerThread();
            if (ownershipId == 0 || !cameraBrainOwners.TryGetValue(ownershipId, out CameraBrainOwnership entry))
            {
                return;
            }

            if (!ReferenceEquals(entry.Owner, owner))
            {
                return;
            }

            if (entry.Brain != null)
            {
                entry.Brain.UpdateMethod = entry.PreviousUpdateMethod;
            }

            cameraBrainOwners.Remove(ownershipId);
        }

        public void Dispose()
        {
            ShutdownImmediate(EndPlayReason.WorldShutdown);
        }

        private void DiscoverSceneActors()
        {
            Actor[] sceneActors = UnityEngine.Object.FindObjectsByType<Actor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < sceneActors.Length; i++)
            {
                Actor actor = sceneActors[i];
                if (actor == null || actor.gameObject.scene.IsValid() == false)
                {
                    continue;
                }

                RegisterActorInternal(actor, owned: false, beginIfPlaying: true, deferred: false, activateOnFinish: false);
            }
        }

        private void RegisterActorInternal(
            Actor actor,
            bool owned,
            bool beginIfPlaying,
            bool deferred,
            bool activateOnFinish)
        {
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            int instanceId = actor.GetInstanceID();
            if (actorIndices.TryGetValue(instanceId, out int existingIndex))
            {
                if (owned && !actors[existingIndex].Owned)
                {
                    ActorEntry upgradedEntry = actors[existingIndex];
                    upgradedEntry.Owned = true;
                    actors[existingIndex] = upgradedEntry;
                    ownedActorCount++;
                }

                return;
            }

            if (actor.World != null && !ReferenceEquals(actor.World, this))
            {
                throw new InvalidOperationException($"Actor '{actor.name}' already belongs to another World.");
            }

            int index = actors.Count;
            actors.Add(new ActorEntry
            {
                Actor = actor,
                Owned = owned,
                Deferred = deferred,
                ActivateOnFinish = activateOnFinish,
                TickPhase = ActorTickPhase.None,
                TickListIndex = -1,
            });
            if (owned)
            {
                ownedActorCount++;
            }

            actorIndices.Add(instanceId, index);
            actor.BindToWorld(this, allowReentry: !owned);

            ActorEntry registeredEntry = actors[index];
            registeredEntry.TickPhase = actor.TickPhase;
            if (actor.IsActorTickEnabled())
            {
                AddActorToTickRegistry(ref registeredEntry);
            }
            actors[index] = registeredEntry;

            if (actor is PlayerStart playerStart)
            {
                playerStarts.Add(playerStart);
            }

            if (beginIfPlaying && lifecycleState == WorldLifecycleState.Playing && actor.isActiveAndEnabled)
            {
                actor.NotifyWorldBeginPlay();
            }
        }

        private ActorEntry RemoveActorAt(int index)
        {
            int lastIndex = actors.Count - 1;
            ActorEntry removed = actors[index];
            RemoveActorFromTickRegistry(ref removed);
            actorIndices.Remove(removed.Actor.GetInstanceID());
            if (removed.Owned)
            {
                ownedActorCount--;
            }

            if (index != lastIndex)
            {
                ActorEntry moved = actors[lastIndex];
                actors[index] = moved;
                actorIndices[moved.Actor.GetInstanceID()] = index;
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
                Debug.LogException(exception);
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
                GameObject actorObject = entry.Owned ? actor.gameObject : null;
                try
                {
                    actor.UnbindFromWorld(this, reason);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, actor);
                }
                finally
                {
                    if (entry.Owned)
                    {
                        DestroyUnityObject(actorObject);
                    }
                }
            }

            playerControllers.Clear();
            playerStarts.Clear();
            updateTickActors.Clear();
            fixedUpdateTickActors.Clear();
            lateUpdateTickActors.Clear();
            ReleaseAllCameraBrains();
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

        private void ReleaseAllCameraBrains()
        {
            foreach (KeyValuePair<int, CameraBrainOwnership> pair in cameraBrainOwners)
            {
                CameraBrainOwnership entry = pair.Value;
                if (entry.Brain != null)
                {
                    entry.Brain.UpdateMethod = entry.PreviousUpdateMethod;
                }
            }

            cameraBrainOwners.Clear();
        }

        private bool CanDispatchActorTick(Actor actor, ActorTickPhase phase)
        {
            if (actor == null ||
                !actor.HasBegunPlay ||
                !actor.isActiveAndEnabled ||
                !actor.IsActorTickEnabled() ||
                actor.TickPhase != phase ||
                !actorIndices.TryGetValue(actor.GetInstanceID(), out int actorIndex))
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
            entry.TickListIndex = tickActors.Count;
            tickActors.Add(entry.Actor);

            // Capacity growth is paid on the registration cold path rather than the first
            // PlayerLoop dispatch after a population increase.
            if (tickScratch.Capacity < tickActors.Count)
            {
                tickScratch.Capacity = tickActors.Capacity;
            }
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
                    !actorIndices.TryGetValue(movedActor.GetInstanceID(), out int movedActorIndex))
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

        private static void DestroyUnityObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
