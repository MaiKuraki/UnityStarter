using System;
using System.Threading;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public enum GameplayWorldHostState : byte
    {
        Idle = 0,
        Starting = 1,
        Running = 2,
        Stopping = 3,
        Stopped = 4,
        Faulted = 5,
        Disposed = 6,
    }

    /// <summary>
    /// Unity composition root for one GameInstance and its active World. Manual bootstrap and
    /// DI containers provide the same explicit GameplayWorldComposition before startup.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class GameplayWorldHost : MonoBehaviour
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        [SerializeField] private WorldSettings worldSettings;
        [SerializeField] private GameplayWorldTerminalCleanupOwner terminalCleanupOwner;
        [SerializeField] private WorldNetMode netMode = WorldNetMode.Standalone;
        [SerializeField] private bool autoStart = true;
        [SerializeField, Range(0, GameInstance.MaxLocalPlayers)] private int localPlayerCount = 1;

        private GameInstance gameInstance;
        private CancellationTokenSource lifetimeCancellation;
        private CancellationTokenSource startCancellation;
        private GameplayWorldComposition composition;
        private IGameplayWorldTerminalCleanupOwner configuredTerminalCleanupOwner;
        private IGameplayWorldTerminalCleanupOwner activeTerminalCleanupOwner;
        private SceneWorldActorSource defaultActorSource;
        private GameplayWorldTickDriver tickDriver;
        private GameplayWorldLateTickDriver lateTickDriver;
        private int ownerThreadId;
        private GameplayWorldHostState state = GameplayWorldHostState.Idle;
        private string lastError;

        public WorldSettings WorldSettings
        {
            get { AssertOwnerThread(); return worldSettings; }
        }
        public WorldNetMode NetMode
        {
            get { AssertOwnerThread(); return netMode; }
        }
        public bool AutoStart
        {
            get { AssertOwnerThread(); return autoStart; }
        }
        public int ConfiguredLocalPlayerCount
        {
            get { AssertOwnerThread(); return localPlayerCount; }
        }
        public int EffectiveLocalPlayerCount
        {
            get
            {
                AssertOwnerThread();
                return netMode == WorldNetMode.DedicatedServer ? 0 : localPlayerCount;
            }
        }
        public GameplayWorldHostState State
        {
            get
            {
                AssertOwnerThread();
                return state == GameplayWorldHostState.Running && CurrentWorld == null
                    ? GameplayWorldHostState.Stopped
                    : state;
            }
        }
        public string LastError
        {
            get { AssertOwnerThread(); return lastError; }
        }
        public GameInstance GameInstance
        {
            get { AssertOwnerThread(); return gameInstance; }
        }
        public World CurrentWorld
        {
            get { AssertOwnerThread(); return gameInstance?.CurrentWorld; }
        }
        public bool IsRunning
        {
            get
            {
                AssertOwnerThread();
                return state == GameplayWorldHostState.Running && CurrentWorld != null;
            }
        }
        public GameplayWorldComposition Composition
        {
            get { AssertOwnerThread(); return composition; }
        }
        public IGameplayWorldTerminalCleanupOwner TerminalCleanupOwner
        {
            get
            {
                AssertOwnerThread();
                return composition?.TerminalCleanupOwner ??
                       configuredTerminalCleanupOwner ??
                       terminalCleanupOwner;
            }
        }
        public bool HasExplicitComposition
        {
            get { AssertOwnerThread(); return composition != null; }
        }

        private void Awake()
        {
            BindOwnerThread();
            EnsureLifetime();
            EnsureTickDrivers();
        }

        private void OnEnable()
        {
            BindOwnerThread();
        }

        private void Start()
        {
            if (autoStart)
            {
                StartAutomaticallyAsync().Forget(HandleAutoStartFailure);
            }
        }

        public async UniTask<World> StartWorldAsync(CancellationToken cancellationToken = default)
        {
            AssertOwnerThread();
            EnsureLifetime();
            ThrowIfDisposed();
            EnsureTickDrivers();

            if (state == GameplayWorldHostState.Running && CurrentWorld != null)
            {
                return CurrentWorld;
            }

            if (state == GameplayWorldHostState.Starting || state == GameplayWorldHostState.Stopping)
            {
                throw new InvalidOperationException($"Cannot start a World while the host is {state}.");
            }

            if (worldSettings == null)
            {
                throw new InvalidOperationException("GameplayWorldHost requires WorldSettings.");
            }

            GameplayWorldComposition activeComposition = composition;
            if (activeComposition == null)
            {
                IGameplayWorldTerminalCleanupOwner defaultCleanupOwner =
                    configuredTerminalCleanupOwner ?? terminalCleanupOwner;
                if (defaultCleanupOwner == null)
                {
                    throw new InvalidOperationException(
                        "GameplayWorldHost requires an application-lifetime terminal cleanup owner.");
                }

                ValidateTerminalCleanupOwnerHierarchy(defaultCleanupOwner);
                activeComposition = GameplayWorldComposition.CreateDefault(
                    defaultCleanupOwner,
                    actorSource: GetDefaultActorSource());
            }

            IGameplayWorldTerminalCleanupOwner cleanupOwner =
                activeComposition.TerminalCleanupOwner;
            ValidateTerminalCleanupOwnerHierarchy(cleanupOwner);
            DisposeGameInstance();
            if (!cleanupOwner.HasCapacity)
            {
                throw new InvalidOperationException(
                    "Gameplay World terminal cleanup ownership has no capacity for another GameInstance.");
            }

            lastError = null;
            state = GameplayWorldHostState.Starting;
            startCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation.Token,
                cancellationToken);

            try
            {
                var createdInstance = new GameInstance(
                    activeComposition.ActorLifetime,
                    EffectiveLocalPlayerCount,
                    activeComposition.ReferenceResolver,
                    activeComposition.SceneTransitionHandler,
                    activeComposition.RuntimeLimits,
                    activeComposition.ActorSource,
                    activeComposition.MatchClock,
                    activeComposition.CameraOutputLeaseArbiter);

                if (!cleanupOwner.TryRegister(createdInstance))
                {
                    createdInstance.Dispose();
                    throw new InvalidOperationException(
                        "Gameplay World terminal cleanup ownership could not register the new GameInstance.");
                }

                activeTerminalCleanupOwner = cleanupOwner;
                gameInstance = createdInstance;

                World world = await gameInstance.StartWorldAsync(
                    worldSettings,
                    netMode,
                    activeComposition.GameSession,
                    startCancellation.Token);
                await UniTask.SwitchToMainThread();
                AssertOwnerThread();
                ThrowIfDisposed();
                state = GameplayWorldHostState.Running;
                return world;
            }
            catch (OperationCanceledException exception)
            {
                await UniTask.SwitchToMainThread();
                AssertOwnerThread();
                Exception replacementFailure = CompleteFailedStart(
                    exception,
                    wasCancellation: true);
                if (replacementFailure != null)
                {
                    if (ReferenceEquals(replacementFailure, exception))
                    {
                        throw;
                    }

                    throw replacementFailure;
                }

                throw;
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                AssertOwnerThread();
                Exception replacementFailure = CompleteFailedStart(
                    exception,
                    wasCancellation: false);
                if (replacementFailure != null)
                {
                    if (ReferenceEquals(replacementFailure, exception))
                    {
                        throw;
                    }

                    throw replacementFailure;
                }

                throw;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                AssertOwnerThread();
                startCancellation?.Dispose();
                startCancellation = null;
            }
        }

        /// <summary>
        /// Cancels a pending start or fully stops the active World. Once World shutdown begins,
        /// cleanup is non-cancellable and this operation completes only after ownership ends.
        /// </summary>
        public async UniTask StopWorldAsync(
            EndPlayReason reason = EndPlayReason.WorldShutdown)
        {
            AssertOwnerThread();
            ThrowIfDisposed();

            if (state == GameplayWorldHostState.Starting)
            {
                CancelWithoutInterruptingCleanup(startCancellation);
                while (state == GameplayWorldHostState.Starting)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    AssertOwnerThread();
                }
            }

            ThrowIfDisposed();
            if (state == GameplayWorldHostState.Stopping)
            {
                throw new InvalidOperationException("A World stop operation is already in progress.");
            }

            if (gameInstance?.CurrentWorld == null)
            {
                DisposeGameInstance();
                state = GameplayWorldHostState.Stopped;
                return;
            }

            GameInstance stoppingInstance = gameInstance;
            state = GameplayWorldHostState.Stopping;
            lastError = null;
            try
            {
                if (stoppingInstance.IsDisposed)
                {
                    // A failed startup may already have entered GameInstance disposal while its
                    // World still owns retryable terminal state. Dispose is the retry operation;
                    // StopWorldAsync deliberately rejects every disposed GameInstance.
                    DisposeGameInstance();
                }
                else
                {
                    await stoppingInstance.StopWorldAsync(reason);
                    await UniTask.SwitchToMainThread();
                    AssertOwnerThread();
                    if (state == GameplayWorldHostState.Disposed)
                    {
                        return;
                    }

                    DisposeGameInstance();
                }

                state = GameplayWorldHostState.Stopped;
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                AssertOwnerThread();
                if (state == GameplayWorldHostState.Disposed)
                {
                    return;
                }

                lastError = exception.Message;
                state = GameplayWorldHostState.Faulted;
                throw;
            }
        }

        /// <summary>
        /// Supplies explicit World dependencies. Call before startup; the caller retains
        /// ownership of the supplied services and disposes them after this host stops.
        /// </summary>
        public void Configure(GameplayWorldComposition value)
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            if (state == GameplayWorldHostState.Starting ||
                state == GameplayWorldHostState.Running ||
                state == GameplayWorldHostState.Stopping)
            {
                throw new InvalidOperationException(
                    $"GameplayWorldHost composition cannot change while the host is {state}.");
            }

            composition = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Supplies the application-lifetime terminal owner while retaining default World
        /// composition. Call before startup.
        /// </summary>
        public void ConfigureTerminalCleanupOwner(
            IGameplayWorldTerminalCleanupOwner value)
        {
            AssertOwnerThread();
            ThrowIfDisposed();
            if (state == GameplayWorldHostState.Starting ||
                state == GameplayWorldHostState.Running ||
                state == GameplayWorldHostState.Stopping)
            {
                throw new InvalidOperationException(
                    $"GameplayWorldHost terminal cleanup ownership cannot change while the host is {state}.");
            }

            configuredTerminalCleanupOwner = value ??
                throw new ArgumentNullException(nameof(value));
        }

        private async UniTask StartAutomaticallyAsync()
        {
            try
            {
                await StartWorldAsync();
            }
            catch (OperationCanceledException)
            {
                // Destroy and explicit stop are normal cancellation paths.
            }
            catch (Exception exception)
            {
                Log.Error(exception, "GameplayWorldHost automatic World startup failed.");
            }
        }

        private static void HandleAutoStartFailure(Exception exception)
        {
            try
            {
                Log.Error(
                    exception,
                    "GameplayWorldHost automatic World startup failed unexpectedly.");
            }
            catch (Exception)
            {
                // Terminal safety net: a logging failure must never escape into the scheduler.
            }
        }

        private void OnValidate()
        {
            localPlayerCount = Mathf.Clamp(localPlayerCount, 0, GameInstance.MaxLocalPlayers);
        }

        private void OnDestroy()
        {
            BindOwnerThread();
            if (state == GameplayWorldHostState.Disposed)
            {
                return;
            }

            state = GameplayWorldHostState.Disposed;
            OutOfMemoryException terminalOutOfMemory = null;

            try
            {
                tickDriver?.Unbind(this);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameplayWorldHost tick driver unbind failed during destruction.",
                    ref terminalOutOfMemory);
            }
            finally
            {
                tickDriver = null;
            }

            try
            {
                lateTickDriver?.Unbind(this);
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameplayWorldHost late tick driver unbind failed during destruction.",
                    ref terminalOutOfMemory);
            }
            finally
            {
                lateTickDriver = null;
            }

            CancelForTerminalCleanup(
                lifetimeCancellation,
                "GameplayWorldHost lifetime cancellation failed during destruction.",
                ref terminalOutOfMemory);
            CancelForTerminalCleanup(
                startCancellation,
                "GameplayWorldHost startup cancellation failed during destruction.",
                ref terminalOutOfMemory);

            try
            {
                DisposeGameInstance();
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    "GameplayWorldHost GameInstance disposal failed during destruction.",
                    ref terminalOutOfMemory);
            }
            finally
            {
                // The application-lifetime registry remains the sole retry owner after this
                // component becomes unreachable.
                if (gameInstance != null && activeTerminalCleanupOwner != null)
                {
                    gameInstance = null;
                    activeTerminalCleanupOwner = null;
                }

                // An active startup transaction owns and disposes startCancellation in its
                // finally block after every cancellation observer has resumed.
                try
                {
                    lifetimeCancellation?.Dispose();
                }
                catch (Exception exception)
                {
                    LogTerminalException(
                        exception,
                        "GameplayWorldHost lifetime token disposal failed during destruction.",
                        ref terminalOutOfMemory);
                }
                finally
                {
                    lifetimeCancellation = null;
                }

                defaultActorSource = null;
            }

            if (terminalOutOfMemory != null)
            {
                throw terminalOutOfMemory;
            }
        }

        private SceneWorldActorSource GetDefaultActorSource()
        {
            if (defaultActorSource == null || defaultActorSource.Scene != gameObject.scene)
            {
                defaultActorSource = new SceneWorldActorSource(gameObject.scene);
            }

            return defaultActorSource;
        }

        private void ValidateTerminalCleanupOwnerHierarchy(
            IGameplayWorldTerminalCleanupOwner cleanupOwner)
        {
            if (!(cleanupOwner is GameplayWorldTerminalCleanupOwner unityOwner))
            {
                return;
            }

            if (unityOwner.transform.parent != null)
            {
                throw new InvalidOperationException(
                    "GameplayWorldTerminalCleanupOwner must be placed on a root GameObject.");
            }
            if (ReferenceEquals(unityOwner.transform.root, transform.root))
            {
                throw new InvalidOperationException(
                    "GameplayWorldHost and its terminal cleanup owner must use independent root GameObjects.");
            }
        }

        private void DisposeGameInstance()
        {
            GameInstance disposingInstance = gameInstance;
            if (disposingInstance == null)
            {
                return;
            }

            try
            {
                disposingInstance.Dispose();
            }
            finally
            {
                if (disposingInstance.IsDisposalComplete &&
                    ReferenceEquals(gameInstance, disposingInstance))
                {
                    IGameplayWorldTerminalCleanupOwner cleanupOwner =
                        activeTerminalCleanupOwner ??
                        throw new InvalidOperationException(
                            "GameInstance terminal cleanup ownership is unavailable.");
                    cleanupOwner.ReleaseCompleted(disposingInstance);
                    gameInstance = null;
                    activeTerminalCleanupOwner = null;
                }
            }
        }

        private Exception CompleteFailedStart(Exception startupFailure, bool wasCancellation)
        {
            Exception cleanupFailure = null;
            try
            {
                DisposeGameInstance();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            finally
            {
                if (state != GameplayWorldHostState.Disposed)
                {
                    bool cleanupCompleted = gameInstance == null;
                    state = wasCancellation && cleanupCompleted && cleanupFailure == null
                        ? GameplayWorldHostState.Stopped
                        : GameplayWorldHostState.Faulted;
                    lastError = state == GameplayWorldHostState.Faulted
                        ? (cleanupFailure ?? startupFailure).Message
                        : null;
                }
            }

            OutOfMemoryException startupOutOfMemory = FindOutOfMemory(startupFailure);
            if (startupOutOfMemory != null)
            {
                return startupOutOfMemory;
            }

            OutOfMemoryException cleanupOutOfMemory = FindOutOfMemory(cleanupFailure);
            return cleanupOutOfMemory ?? cleanupFailure;
        }

        private void ThrowIfDisposed()
        {
            if (state == GameplayWorldHostState.Disposed)
            {
                throw new ObjectDisposedException(nameof(GameplayWorldHost));
            }
        }

        private void CancelWithoutInterruptingCleanup(CancellationTokenSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "A GameplayWorldHost cancellation observer failed; lifecycle cleanup will continue.");
            }
        }

        private static void CancelForTerminalCleanup(
            CancellationTokenSource source,
            string failureMessage,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            catch (Exception exception)
            {
                LogTerminalException(
                    exception,
                    failureMessage,
                    ref terminalOutOfMemory);
            }
        }

        private static void LogTerminalException(
            Exception exception,
            string message,
            ref OutOfMemoryException terminalOutOfMemory)
        {
            OutOfMemoryException captured = FindOutOfMemory(exception);
            if (captured != null)
            {
                if (terminalOutOfMemory == null)
                {
                    terminalOutOfMemory = captured;
                }

                return;
            }

            try
            {
                Log.Error(exception, message);
            }
            catch (Exception loggingException)
            {
                captured = FindOutOfMemory(loggingException);
                if (terminalOutOfMemory == null && captured != null)
                {
                    terminalOutOfMemory = captured;
                }
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

        private void EnsureLifetime()
        {
            if (state == GameplayWorldHostState.Disposed)
            {
                throw new ObjectDisposedException(nameof(GameplayWorldHost));
            }

            lifetimeCancellation ??= new CancellationTokenSource();
        }

        internal void DispatchWorldTick(ActorTickPhase phase, float deltaSeconds)
        {
            AssertOwnerThread();
            gameInstance?.Tick(phase, deltaSeconds);
        }

        private void EnsureTickDrivers()
        {
            tickDriver = GetComponent<GameplayWorldTickDriver>();
            if (tickDriver == null)
            {
                tickDriver = gameObject.AddComponent<GameplayWorldTickDriver>();
                tickDriver.hideFlags = HideFlags.HideInInspector;
            }

            tickDriver.Bind(this);

            lateTickDriver = GetComponent<GameplayWorldLateTickDriver>();
            if (lateTickDriver == null)
            {
                lateTickDriver = gameObject.AddComponent<GameplayWorldLateTickDriver>();
                lateTickDriver.hideFlags = HideFlags.HideInInspector;
            }

            lateTickDriver.Bind(this);
        }

        private void AssertOwnerThread()
        {
            int expectedThreadId = ownerThreadId;
            if (expectedThreadId == 0)
            {
                throw new InvalidOperationException(
                    "GameplayWorldHost lifecycle ownership has not been initialized.");
            }
            if (Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "GameplayWorldHost live state must be accessed on its Unity lifecycle owner thread.");
            }
        }

        private void BindOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ownerThreadId != 0 && ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "GameplayWorldHost lifecycle ownership cannot move between threads.");
            }

            ownerThreadId = currentThreadId;
        }
    }
}
