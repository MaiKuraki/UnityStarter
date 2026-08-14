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
        [SerializeField] private WorldNetMode netMode = WorldNetMode.Standalone;
        [SerializeField] private bool autoStart = true;
        [SerializeField, Range(0, GameInstance.MaxLocalPlayers)] private int localPlayerCount = 1;

        private GameInstance gameInstance;
        private CancellationTokenSource lifetimeCancellation;
        private CancellationTokenSource startCancellation;
        private GameplayWorldComposition composition;
        private SceneWorldActorSource defaultActorSource;
        private GameplayWorldTickDriver tickDriver;
        private GameplayWorldLateTickDriver lateTickDriver;
        private GameplayWorldHostState state = GameplayWorldHostState.Idle;
        private string lastError;

        public WorldSettings WorldSettings => worldSettings;
        public WorldNetMode NetMode => netMode;
        public bool AutoStart => autoStart;
        public int ConfiguredLocalPlayerCount => localPlayerCount;
        public int EffectiveLocalPlayerCount => netMode == WorldNetMode.DedicatedServer ? 0 : localPlayerCount;
        public GameplayWorldHostState State =>
            state == GameplayWorldHostState.Running && CurrentWorld == null
                ? GameplayWorldHostState.Stopped
                : state;
        public string LastError => lastError;
        public GameInstance GameInstance => gameInstance;
        public World CurrentWorld => gameInstance?.CurrentWorld;
        public bool IsRunning => state == GameplayWorldHostState.Running && CurrentWorld != null;
        public GameplayWorldComposition Composition => composition;
        public bool HasExplicitComposition => composition != null;

        private void Awake()
        {
            EnsureLifetime();
            EnsureTickDrivers();
        }

        private void Start()
        {
            if (autoStart)
            {
                StartAutomaticallyAsync().Forget();
            }
        }

        public async UniTask<World> StartWorldAsync(CancellationToken cancellationToken = default)
        {
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

            DisposeGameInstance();
            lastError = null;
            state = GameplayWorldHostState.Starting;
            startCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation.Token,
                cancellationToken);

            try
            {
                GameplayWorldComposition activeComposition =
                    composition ?? GameplayWorldComposition.CreateDefault(
                        actorSource: GetDefaultActorSource());
                gameInstance = new GameInstance(
                    activeComposition.ActorLifetime,
                    EffectiveLocalPlayerCount,
                    activeComposition.ReferenceResolver,
                    activeComposition.SceneTransitionHandler,
                    activeComposition.RuntimeLimits,
                    activeComposition.ActorSource,
                    activeComposition.MatchClock,
                    activeComposition.CameraOutputLeaseArbiter);

                World world = await gameInstance.StartWorldAsync(
                    worldSettings,
                    netMode,
                    activeComposition.GameSession,
                    startCancellation.Token);
                await UniTask.SwitchToMainThread();
                ThrowIfDisposed();
                state = GameplayWorldHostState.Running;
                return world;
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                DisposeGameInstance();
                if (state != GameplayWorldHostState.Disposed)
                {
                    state = GameplayWorldHostState.Stopped;
                }

                throw;
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                DisposeGameInstance();
                if (state != GameplayWorldHostState.Disposed)
                {
                    lastError = exception.Message;
                    state = GameplayWorldHostState.Faulted;
                }

                throw;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                startCancellation?.Dispose();
                startCancellation = null;
            }
        }

        public async UniTask StopWorldAsync(
            EndPlayReason reason = EndPlayReason.WorldShutdown,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (state == GameplayWorldHostState.Starting)
            {
                CancelWithoutInterruptingCleanup(startCancellation);
                while (state == GameplayWorldHostState.Starting)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
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
                await stoppingInstance.StopWorldAsync(reason, cancellationToken);
                await UniTask.SwitchToMainThread();
                if (state == GameplayWorldHostState.Disposed)
                {
                    return;
                }

                DisposeGameInstance();
                state = GameplayWorldHostState.Stopped;
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                if (state == GameplayWorldHostState.Disposed)
                {
                    return;
                }

                DisposeGameInstance();
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

        private async UniTaskVoid StartAutomaticallyAsync()
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

        private void OnValidate()
        {
            localPlayerCount = Mathf.Clamp(localPlayerCount, 0, GameInstance.MaxLocalPlayers);
        }

        private void OnDestroy()
        {
            if (state == GameplayWorldHostState.Disposed)
            {
                return;
            }

            state = GameplayWorldHostState.Disposed;
            tickDriver?.Unbind(this);
            tickDriver = null;
            lateTickDriver?.Unbind(this);
            lateTickDriver = null;
            CancelWithoutInterruptingCleanup(lifetimeCancellation);
            CancelWithoutInterruptingCleanup(startCancellation);

            try
            {
                DisposeGameInstance();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "GameplayWorldHost GameInstance disposal failed during destruction.");
            }
            finally
            {
                // An active startup transaction owns and disposes startCancellation in its
                // finally block after every cancellation observer has resumed.
                lifetimeCancellation?.Dispose();
                lifetimeCancellation = null;
                defaultActorSource = null;
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

        private void DisposeGameInstance()
        {
            gameInstance?.Dispose();
            gameInstance = null;
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
    }
}
