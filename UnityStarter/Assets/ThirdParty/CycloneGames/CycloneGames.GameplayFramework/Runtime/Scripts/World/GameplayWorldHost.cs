using System;
using System.Threading;
using CycloneGames.Factory.Runtime;
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
    /// Unity composition root for one GameInstance and its active World. Projects that need
    /// external asset loading, scene navigation, or a custom session override the narrow
    /// creation methods; projects with a DI composition root may use GameInstance directly.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public class GameplayWorldHost : MonoBehaviour
    {
        [SerializeField] private WorldSettings worldSettings;
        [SerializeField] private WorldNetMode netMode = WorldNetMode.Standalone;
        [SerializeField] private bool autoStart = true;
        [SerializeField, Range(0, GameInstance.MaxLocalPlayers)] private int localPlayerCount = 1;

        private GameInstance gameInstance;
        private CancellationTokenSource lifetimeCancellation;
        private CancellationTokenSource startCancellation;
        private GameplayWorldTickDriver tickDriver;
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

        private void Awake()
        {
            EnsureLifetime();
            EnsureTickDriver();
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
            EnsureTickDriver();

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
                gameInstance = new GameInstance(
                    CreateObjectSpawner(),
                    EffectiveLocalPlayerCount,
                    CreateReferenceResolver(),
                    CreateSceneTransitionHandler());

                World world = await gameInstance.StartWorldAsync(
                    worldSettings,
                    netMode,
                    CreateGameSession(),
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

            state = GameplayWorldHostState.Stopping;
            lastError = null;
            try
            {
                await gameInstance.StopWorldAsync(reason, cancellationToken);
                await UniTask.SwitchToMainThread();
                DisposeGameInstance();
                state = GameplayWorldHostState.Stopped;
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                DisposeGameInstance();
                lastError = exception.Message;
                state = GameplayWorldHostState.Faulted;
                throw;
            }
        }

        protected virtual IUnityObjectSpawner CreateObjectSpawner()
        {
            return new DefaultUnityObjectSpawner();
        }

        protected virtual IWorldSettingsReferenceResolver CreateReferenceResolver()
        {
            return null;
        }

        protected virtual ISceneTransitionHandler CreateSceneTransitionHandler()
        {
            return null;
        }

        protected virtual IGameSession CreateGameSession()
        {
            return null;
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
                Debug.LogException(exception, this);
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
            CancelWithoutInterruptingCleanup(lifetimeCancellation);
            CancelWithoutInterruptingCleanup(startCancellation);

            DisposeGameInstance();
            startCancellation?.Dispose();
            startCancellation = null;
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
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
                Debug.LogException(exception, this);
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

        private void EnsureTickDriver()
        {
            tickDriver = GetComponent<GameplayWorldTickDriver>();
            if (tickDriver == null)
            {
                tickDriver = gameObject.AddComponent<GameplayWorldTickDriver>();
                tickDriver.hideFlags = HideFlags.HideInInspector;
            }

            tickDriver.Bind(this);
        }
    }
}
