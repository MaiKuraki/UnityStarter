using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using CycloneGames.Factory.Runtime;
using Cysharp.Threading.Tasks;

namespace CycloneGames.GameplayFramework.Runtime
{
    public enum WorldNetMode : byte
    {
        Standalone = 0,
        Client = 1,
        ListenServer = 2,
        DedicatedServer = 3,
    }

    public enum WorldLifecycleState : byte
    {
        Created = 0,
        Initializing = 1,
        Playing = 2,
        Stopping = 3,
        Stopped = 4,
        Disposed = 5,
    }

    public enum EndPlayReason : byte
    {
        Destroyed = 0,
        SceneUnload = 1,
        WorldShutdown = 2,
        Travel = 3,
        InitializationFailure = 4,
        ApplicationShutdown = 5,
    }

    /// <summary>
    /// A local user slot that survives world replacement. Input and viewport integrations bind
    /// to this object; PlayerController and Pawn remain world-scoped.
    /// </summary>
    public sealed class LocalPlayer
    {
        internal LocalPlayer(int index)
        {
            Index = index;
        }

        public int Index { get; }
        public PlayerController PlayerController { get; internal set; }
    }

    /// <summary>
    /// Application-scoped composition and lifetime owner. A GameInstance may own one active
    /// World at a time and never uses global state or reflection-based discovery.
    /// </summary>
    public sealed class GameInstance : IDisposable
    {
        public const int MaxLocalPlayers = 8;

        private readonly IUnityObjectSpawner objectSpawner;
        private readonly IWorldSettingsReferenceResolver referenceResolver;
        private readonly ISceneTransitionHandler sceneTransitionHandler;
        private readonly List<LocalPlayer> localPlayers;
        private readonly ReadOnlyCollection<LocalPlayer> localPlayerView;
        private readonly int ownerThreadId;
        private CancellationTokenSource lifetimeCancellation;
        private World currentWorld;
        private bool isStartingWorld;
        private bool isDisposed;

        public GameInstance(
            IUnityObjectSpawner objectSpawner,
            int localPlayerCount = 1,
            IWorldSettingsReferenceResolver referenceResolver = null,
            ISceneTransitionHandler sceneTransitionHandler = null)
        {
            this.objectSpawner = objectSpawner ?? throw new ArgumentNullException(nameof(objectSpawner));
            if (localPlayerCount < 0 || localPlayerCount > MaxLocalPlayers)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localPlayerCount),
                    localPlayerCount,
                    $"Local player count must be between 0 and {MaxLocalPlayers}.");
            }

            this.referenceResolver = referenceResolver;
            this.sceneTransitionHandler = sceneTransitionHandler;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            lifetimeCancellation = new CancellationTokenSource();
            localPlayers = new List<LocalPlayer>(localPlayerCount);

            for (int i = 0; i < localPlayerCount; i++)
            {
                localPlayers.Add(new LocalPlayer(i));
            }

            localPlayerView = localPlayers.AsReadOnly();
        }

        public IReadOnlyList<LocalPlayer> LocalPlayers => localPlayerView;
        public World CurrentWorld => currentWorld;
        public bool IsDisposed => isDisposed;

        /// <summary>
        /// Forwards one PlayerLoop phase to the active World. Composition roots that do not use
        /// GameplayWorldHost must call this method explicitly from their loop owner.
        /// </summary>
        public void Tick(ActorTickPhase phase, float deltaSeconds)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            World.ValidateTickRequest(phase, deltaSeconds);
            currentWorld?.Tick(phase, deltaSeconds);
        }

        /// <summary>
        /// Starts a world transaction. Configuration, asset resolution, spawn, login, and
        /// possession failures are rolled back before the exception is rethrown.
        /// </summary>
        public async UniTask<World> StartWorldAsync(
            WorldSettings settings,
            WorldNetMode netMode = WorldNetMode.Standalone,
            IGameSession gameSession = null,
            CancellationToken cancellationToken = default)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (currentWorld != null)
            {
                throw new InvalidOperationException("A world is already active. Stop it before starting another world.");
            }

            if (isStartingWorld)
            {
                throw new InvalidOperationException("A world start operation is already in progress.");
            }

            isStartingWorld = true;
            WorldDefinition pendingDefinition = null;
            try
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token,
                    cancellationToken);

                pendingDefinition = await settings.ResolveDefinitionAsync(
                    referenceResolver,
                    linkedCancellation.Token);

                await UniTask.SwitchToMainThread(linkedCancellation.Token);
                EnsureOwnerThread();
                ThrowIfDisposed();

                var world = new World(
                    this,
                    objectSpawner,
                    pendingDefinition,
                    netMode,
                    gameSession,
                    sceneTransitionHandler,
                    ownerThreadId);

                // Ownership transfers to World only after construction succeeds.
                pendingDefinition = null;
                currentWorld = world;
                try
                {
                    await world.InitializeAsync(localPlayers, linkedCancellation.Token);
                    await UniTask.SwitchToMainThread();
                    EnsureOwnerThread();
                    if (world.LifecycleState != WorldLifecycleState.Playing ||
                        !ReferenceEquals(currentWorld, world))
                    {
                        throw new InvalidOperationException(
                            "World initialization was interrupted by a shutdown request.");
                    }

                    return world;
                }
                catch
                {
                    await UniTask.SwitchToMainThread();
                    EnsureOwnerThread();
                    try
                    {
                        world.AbortInitialization();
                    }
                    finally
                    {
                        if (ReferenceEquals(currentWorld, world))
                        {
                            currentWorld = null;
                        }
                    }

                    throw;
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                EnsureOwnerThread();
                pendingDefinition?.Dispose();
                isStartingWorld = false;
            }
        }

        public async UniTask StopWorldAsync(
            EndPlayReason reason = EndPlayReason.WorldShutdown,
            CancellationToken cancellationToken = default)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();

            if (isStartingWorld)
            {
                throw new InvalidOperationException(
                    "Cannot stop a World while its start operation is still resolving configuration.");
            }

            World world = currentWorld;
            if (world == null)
            {
                return;
            }

            try
            {
                await world.ShutdownAsync(reason, cancellationToken);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                EnsureOwnerThread();
                if (ReferenceEquals(currentWorld, world) &&
                    world.LifecycleState == WorldLifecycleState.Disposed)
                {
                    currentWorld = null;
                }
            }
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            EnsureOwnerThread();
            isDisposed = true;

            try
            {
                lifetimeCancellation.Cancel();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }

            currentWorld?.ShutdownImmediate(EndPlayReason.ApplicationShutdown);
            currentWorld = null;

            for (int i = 0; i < localPlayers.Count; i++)
            {
                localPlayers[i].PlayerController = null;
            }

            lifetimeCancellation.Dispose();
            lifetimeCancellation = null;
        }

        internal void NotifyWorldDisposed(World world)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(currentWorld, world))
            {
                currentWorld = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(GameInstance));
            }
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "GameplayFramework mutation must run on the GameInstance owner thread. " +
                    "Marshal work back to the Unity main thread before calling this API.");
            }
        }
    }
}
