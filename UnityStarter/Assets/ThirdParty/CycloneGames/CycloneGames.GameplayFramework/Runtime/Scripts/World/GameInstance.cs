using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.Logging;
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
        RemovedFromWorld = 6,
    }

    /// <summary>
    /// Reports a retryable World terminal pass that retained one or more cleanup owners.
    /// The exception exposes diagnostics through the retained World without copying resource
    /// handles. Calling shutdown again retries only the incomplete terminal work.
    /// </summary>
    public sealed class WorldShutdownIncompleteException : InvalidOperationException
    {
        private readonly World world;

        internal WorldShutdownIncompleteException(World world)
            : base(
                "World shutdown retained incomplete cleanup ownership. Retry shutdown on the same owner thread.")
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public World World
        {
            get
            {
                world.AssertOwnerThread();
                return world;
            }
        }
        public bool HasPendingGameplayCleanup => World.HasPendingGameplayCleanup;
        public bool HasPendingActorCleanup => World.HasPendingActorCleanup;
        public bool HasPendingCameraOutputCleanup => World.HasPendingCameraOutputCleanup;
        public int PendingWorldSettingsLeaseCount =>
            World.PendingWorldSettingsLeaseCount;
        public bool HasPendingLifetimeTokenCleanup =>
            World.HasPendingLifetimeTokenCleanup;
    }

    /// <summary>
    /// A local user slot that survives world replacement. Input and viewport integrations bind
    /// to this object; PlayerController and Pawn remain world-scoped.
    /// </summary>
    public sealed class LocalPlayer
    {
        private readonly GameInstance owner;
        private PlayerController playerController;

        internal LocalPlayer(GameInstance owner, int index)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Index = index;
        }

        public int Index { get; }
        public PlayerController PlayerController
        {
            get
            {
                owner.AssertOwnerThread();
                return playerController;
            }
            internal set
            {
                owner.AssertOwnerThread();
                playerController = value;
            }
        }
    }

    /// <summary>
    /// Application-scoped composition and lifetime owner. A GameInstance may own one active
    /// World at a time and never uses global state or reflection-based discovery.
    /// </summary>
    public sealed class GameInstance : IDisposable
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        public const int MaxLocalPlayers = 8;

        private readonly IActorLifetime actorLifetime;
        private readonly IWorldSettingsReferenceResolver referenceResolver;
        private readonly ISceneTransitionHandler sceneTransitionHandler;
        private readonly WorldRuntimeLimits runtimeLimits;
        private readonly IWorldActorSource actorSource;
        private readonly IMatchClock matchClock;
        private readonly ICameraOutputLeaseArbiter cameraOutputLeaseArbiter;
        private readonly List<LocalPlayer> localPlayers;
        private readonly OwnerThreadReadOnlyList<LocalPlayer> localPlayerView;
        private readonly int ownerThreadId;
        private CancellationTokenSource lifetimeCancellation;
        private World currentWorld;
        private WorldDefinition retainedDefinition;
        private WorldSettingsLeaseQuarantine retainedLeaseQuarantine;
        private bool isStartingWorld;
        private bool isDisposed;
        private bool localPlayersCleared;

        public GameInstance(
            IActorLifetime actorLifetime,
            int localPlayerCount = 1,
            IWorldSettingsReferenceResolver referenceResolver = null,
            ISceneTransitionHandler sceneTransitionHandler = null,
            WorldRuntimeLimits runtimeLimits = null,
            IWorldActorSource actorSource = null,
            IMatchClock matchClock = null,
            ICameraOutputLeaseArbiter cameraOutputLeaseArbiter = null)
        {
            this.actorLifetime = actorLifetime ?? throw new ArgumentNullException(nameof(actorLifetime));
            if (localPlayerCount < 0 || localPlayerCount > MaxLocalPlayers)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localPlayerCount),
                    localPlayerCount,
                    $"Local player count must be between 0 and {MaxLocalPlayers}.");
            }

            this.referenceResolver = referenceResolver;
            this.sceneTransitionHandler = sceneTransitionHandler;
            this.runtimeLimits = runtimeLimits ?? WorldRuntimeLimits.Default;
            this.actorSource = actorSource;
            this.matchClock = matchClock ?? UnityMatchClock.Scaled;
            this.cameraOutputLeaseArbiter =
                cameraOutputLeaseArbiter ?? new CameraOutputLeaseArbiter();
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            lifetimeCancellation = new CancellationTokenSource();
            localPlayers = new List<LocalPlayer>(localPlayerCount);

            for (int i = 0; i < localPlayerCount; i++)
            {
                localPlayers.Add(new LocalPlayer(this, i));
            }

            localPlayerView = new OwnerThreadReadOnlyList<LocalPlayer>(
                EnsureOwnerThread,
                localPlayers);
        }

        public OwnerThreadReadOnlyList<LocalPlayer> LocalPlayers
        {
            get
            {
                EnsureOwnerThread();
                return localPlayerView;
            }
        }

        public World CurrentWorld
        {
            get
            {
                EnsureOwnerThread();
                return currentWorld;
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

        public ICameraOutputLeaseArbiter CameraOutputLeaseArbiter
        {
            get
            {
                EnsureOwnerThread();
                return cameraOutputLeaseArbiter;
            }
        }
        public bool IsDisposed
        {
            get
            {
                EnsureOwnerThread();
                return isDisposed;
            }
        }

        public bool IsDisposalComplete
        {
            get
            {
                EnsureOwnerThread();
                return isDisposed &&
                       currentWorld == null &&
                       retainedDefinition == null &&
                       retainedLeaseQuarantine == null &&
                       localPlayersCleared &&
                       lifetimeCancellation == null;
            }
        }

        public World GetWorld()
        {
            EnsureOwnerThread();
            return currentWorld;
        }

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

            World.ValidateNetMode(netMode);

            if (currentWorld != null)
            {
                throw new InvalidOperationException("A world is already active. Stop it before starting another world.");
            }

            if (retainedDefinition != null)
            {
                retainedDefinition.Dispose();
                if (!retainedDefinition.IsDisposed)
                {
                    throw new InvalidOperationException(
                        "A prior World definition still owns external leases that could not be released.");
                }

                retainedDefinition = null;
            }

            if (retainedLeaseQuarantine != null)
            {
                ReleaseRetainedLeaseQuarantine();
                if (retainedLeaseQuarantine != null)
                {
                    throw new InvalidOperationException(
                        "A prior WorldSettings resolution still owns external leases that could not be released.");
                }
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

                retainedDefinition = pendingDefinition;
                var world = new World(
                    this,
                    actorLifetime,
                    pendingDefinition,
                    netMode,
                    gameSession,
                    sceneTransitionHandler,
                    ownerThreadId,
                    runtimeLimits,
                    actorSource,
                    matchClock,
                    cameraOutputLeaseArbiter);

                // Ownership transfers to World only after construction succeeds.
                pendingDefinition = null;
                retainedDefinition = null;
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
                        if (ReferenceEquals(currentWorld, world) &&
                            world.LifecycleState == WorldLifecycleState.Disposed)
                        {
                            currentWorld = null;
                        }
                    }

                    throw;
                }
            }
            catch (WorldSettingsLeaseCleanupException exception)
            {
                // The carrier can originate from a failed main-thread switch. Adopt ownership
                // before any second scheduling attempt so another scheduler failure cannot make
                // the registered GameInstance lose the only retry handle.
                retainedLeaseQuarantine = exception.TakeLeaseQuarantine();
                await UniTask.SwitchToMainThread();
                EnsureOwnerThread();
                throw;
            }
            catch (WorldSettingsLeaseCleanupOutOfMemoryException exception)
            {
                retainedLeaseQuarantine = exception.TakeLeaseQuarantine();
                await UniTask.SwitchToMainThread();
                EnsureOwnerThread();
                throw;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                EnsureOwnerThread();
                try
                {
                    if (pendingDefinition != null)
                    {
                        retainedDefinition = pendingDefinition;
                        try
                        {
                            pendingDefinition.Dispose();
                        }
                        finally
                        {
                            if (pendingDefinition.IsDisposed)
                            {
                                if (ReferenceEquals(retainedDefinition, pendingDefinition))
                                {
                                    retainedDefinition = null;
                                }

                                pendingDefinition = null;
                            }
                        }
                    }
                }
                finally
                {
                    isStartingWorld = false;
                }
            }
        }

        /// <summary>
        /// Stops the active World and completes all ownership cleanup. Shutdown is deliberately
        /// non-cancellable once requested so the World cannot be left partially released.
        /// </summary>
        public async UniTask StopWorldAsync(
            EndPlayReason reason = EndPlayReason.WorldShutdown)
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
                await world.ShutdownAsync(reason);
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
            EnsureOwnerThread();
            bool firstDisposalPass = !isDisposed;
            if (!firstDisposalPass &&
                currentWorld == null &&
                retainedDefinition == null &&
                retainedLeaseQuarantine == null &&
                localPlayersCleared &&
                lifetimeCancellation == null)
            {
                return;
            }

            isDisposed = true;
            OutOfMemoryException terminalOutOfMemory = null;
            ExceptionDispatchInfo deferredFailure = null;

            if (firstDisposalPass && lifetimeCancellation != null)
            {
                try
                {
                    lifetimeCancellation.Cancel();
                }
                catch (Exception exception)
                {
                    LogTerminalException(
                        exception,
                        "A GameInstance lifetime cancellation observer failed; disposal will continue.",
                        ref terminalOutOfMemory);
                }
            }

            try
            {
                currentWorld?.ShutdownImmediate(EndPlayReason.ApplicationShutdown);
            }
            catch (Exception exception)
            {
                if (!TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception))
                {
                    deferredFailure = ExceptionDispatchInfo.Capture(exception);
                }
            }
            finally
            {
                if (currentWorld != null &&
                    currentWorld.LifecycleState == WorldLifecycleState.Disposed)
                {
                    currentWorld = null;
                }

                if (retainedDefinition != null)
                {
                    try
                    {
                        retainedDefinition.Dispose();
                        if (retainedDefinition.IsDisposed)
                        {
                            retainedDefinition = null;
                        }
                    }
                    catch (Exception exception)
                    {
                        if (!TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception) &&
                            deferredFailure == null)
                        {
                            deferredFailure = ExceptionDispatchInfo.Capture(exception);
                        }
                    }
                }

                if (retainedLeaseQuarantine != null)
                {
                    try
                    {
                        ReleaseRetainedLeaseQuarantine();
                    }
                    catch (Exception exception)
                    {
                        if (!TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception) &&
                            deferredFailure == null)
                        {
                            deferredFailure = ExceptionDispatchInfo.Capture(exception);
                        }
                    }
                }

                if (!localPlayersCleared)
                {
                    bool cleared = true;
                    for (int i = 0; i < localPlayers.Count; i++)
                    {
                        try
                        {
                            localPlayers[i].PlayerController = null;
                        }
                        catch (Exception exception)
                        {
                            cleared = false;
                            if (!TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception) &&
                                deferredFailure == null)
                            {
                                deferredFailure = ExceptionDispatchInfo.Capture(exception);
                            }
                        }
                    }

                    localPlayersCleared = cleared;
                }

                if (lifetimeCancellation != null)
                {
                    try
                    {
                        lifetimeCancellation.Dispose();
                        lifetimeCancellation = null;
                    }
                    catch (Exception exception)
                    {
                        if (!TryCaptureTerminalOutOfMemory(ref terminalOutOfMemory, exception) &&
                            deferredFailure == null)
                        {
                            deferredFailure = ExceptionDispatchInfo.Capture(exception);
                        }
                    }
                }
            }

            if (terminalOutOfMemory != null)
            {
                throw terminalOutOfMemory;
            }

            deferredFailure?.Throw();
        }

        private void ReleaseRetainedLeaseQuarantine()
        {
            WorldSettingsLeaseQuarantine quarantine = retainedLeaseQuarantine;
            if (quarantine == null)
            {
                return;
            }

            try
            {
                quarantine.Dispose();
            }
            finally
            {
                if (quarantine.IsDisposed &&
                    ReferenceEquals(retainedLeaseQuarantine, quarantine))
                {
                    retainedLeaseQuarantine = null;
                }
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

            if (terminalOutOfMemory == null)
            {
                terminalOutOfMemory = captured;
            }

            return true;
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

        internal void NotifyWorldDisposed(World world)
        {
            EnsureOwnerThread();
            if (ReferenceEquals(currentWorld, world))
            {
                currentWorld = null;
            }
        }

        internal void AssertOwnerThread()
        {
            EnsureOwnerThread();
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
                    "GameplayFramework live-state access must run on the GameInstance owner thread. " +
                    "Marshal work back to the Unity main thread before calling this API.");
            }
        }
    }
}
