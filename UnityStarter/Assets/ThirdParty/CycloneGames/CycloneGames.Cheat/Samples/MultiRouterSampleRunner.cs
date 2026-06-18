using System;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.Cheat.Core;
using CycloneGames.Cheat.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VitalRouter;

namespace CycloneGames.Cheat.Sample
{
    /// <summary>
    /// Context that owns private routers for domain-specific command routing.
    /// </summary>
    public class MultiRouterContext
    {
        public Router UIRouter { get; } = new();
        public Router GameplayRouter { get; } = new();
    }

    /// <summary>
    /// UI system that only listens to UI-specific commands via dedicated router.
    /// </summary>
    [Routes]
    public partial class SampleUISystem : IDisposable
    {
        public SampleUISystem(Router uiRouter)
        {
            MapTo(uiRouter);
            Debug.Log("<b>[SampleUISystem]</b> Mapped to UIRouter instance.");
        }

        [Route]
        void OnUICommand(CheatCommand cmd)
        {
            Debug.Log($"<color=aqua><b>[SampleUISystem]</b> Received UI Command: {cmd.CommandId}</color>");
        }

        public void Dispose()
        {
            UnmapRoutes();
        }
    }

    /// <summary>
    /// Gameplay system that only listens to gameplay-specific commands via dedicated router.
    /// </summary>
    [Routes]
    public partial class SampleGameplaySystem : IDisposable
    {
        public SampleGameplaySystem(Router gameplayRouter)
        {
            MapTo(gameplayRouter);
            Debug.Log("<b>[SampleGameplaySystem]</b> Mapped to GameplayRouter instance.");
        }

        [Route]
        void OnGameplayCommand(CheatCommand<GameData> cmd)
        {
            Debug.Log($"<color=orange><b>[SampleGameplaySystem]</b> Received Gameplay Command: {cmd.CommandId} with data {cmd.Arg.position}</color>");
        }

        public void Dispose()
        {
            UnmapRoutes();
        }
    }

    /// <summary>
    /// Main sample runner demonstrating multi-router cheat command system.
    /// Demonstrates flexible routing, cancellation, and different command types.
    /// </summary>
    [Routes]
    public partial class MultiRouterSampleRunner : MonoBehaviour
    {
        [SerializeField] private CheatSampleBenchmark benchmarker;
        [SerializeField] private Button Btn_Benchmark;
        
        private MultiRouterContext routerContext;
        private SampleUISystem uiSystem;
        private SampleGameplaySystem gameplaySystem;
        private ICheatCommandRuntime cheatRuntime;
        private UnityAction _benchmarkClickAction;

        // Cache command IDs to avoid string allocations
        private static readonly string CMD_PROTOCOL_SIMPLE = "Protocol_Simple";
        private static readonly string CMD_PROTOCOL_LONG_RUNNING = "Protocol_LongRunningTask";
        private static readonly string CMD_UI_SHOW_POPUP = "UI_ShowPopup";
        private static readonly string CMD_UI_HIDE_POPUP = "UI_HidePopup";
        private static readonly string CMD_PLAYER_JUMP = "Player_Jump";
        private static readonly string CMD_ENEMY_SPAWN = "Enemy_Spawn";
        private static readonly string CMD_LOG_MESSAGE = "Log_Message";
        private static readonly string CMD_GLOBAL_GAMEDATA = "Global_GameData";

        void Awake()
        {
            Debug.Log("<color=lime><b>[MultiRouterSampleRunner]</b> Initializing multi-router sample...</color>");
            
            routerContext = new MultiRouterContext();
            uiSystem = new SampleUISystem(routerContext.UIRouter);
            gameplaySystem = new SampleGameplaySystem(routerContext.GameplayRouter);
            cheatRuntime = new CheatCommandRuntime(new UnityDebugCheatLogger());
            
            if (Btn_Benchmark != null)
            {
                _benchmarkClickAction = OnBenchmarkClicked;
                Btn_Benchmark.onClick.AddListener(_benchmarkClickAction);
            }

            // Map to global router for default command handling
            MapTo(Router.Default);
        }

        void OnDestroy()
        {
            if (Btn_Benchmark != null && _benchmarkClickAction != null)
            {
                Btn_Benchmark.onClick.RemoveListener(_benchmarkClickAction);
                _benchmarkClickAction = null;
            }

            UnmapRoutes();
            uiSystem?.Dispose();
            gameplaySystem?.Dispose();
            cheatRuntime?.Dispose();
        }

        private void OnBenchmarkClicked()
        {
            if (benchmarker != null)
            {
                benchmarker.RunBenchmark().Forget();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Update()
        {
            // F1-F3: Global Router commands
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Protocol_Simple' to <b>Router.Default</b>");
                cheatRuntime.PublishAsync(CMD_PROTOCOL_SIMPLE).Forget();
            }
            else if (Input.GetKeyDown(KeyCode.F2))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Protocol_LongRunningTask' to <b>Router.Default</b>");
                cheatRuntime.PublishAsync(CMD_PROTOCOL_LONG_RUNNING).Forget();
            }
            else if (Input.GetKeyDown(KeyCode.F3))
            {
                Debug.Log("<b>[Input]</b> Cancelling 'Protocol_LongRunningTask' on <b>Router.Default</b>");
                cheatRuntime.CancelCommand(CMD_PROTOCOL_LONG_RUNNING);
            }
            // F5-F6: UI Router commands
            else if (Input.GetKeyDown(KeyCode.F5))
            {
                Debug.Log("<b>[Input]</b> Publishing 'UI_ShowPopup' to <b>UIRouter</b>");
                cheatRuntime.PublishAsync(CMD_UI_SHOW_POPUP, routerContext.UIRouter).Forget();
            }
            else if (Input.GetKeyDown(KeyCode.F6))
            {
                Debug.Log("<b>[Input]</b> Publishing 'UI_HidePopup' to <b>UIRouter</b>");
                cheatRuntime.PublishAsync(CMD_UI_HIDE_POPUP, routerContext.UIRouter).Forget();
            }
            // F7-F8: Gameplay Router commands
            else if (Input.GetKeyDown(KeyCode.F7))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Player_Jump' to <b>GameplayRouter</b>");
                var jumpData = new GameData(Vector3.up * 5, Vector3.zero);
                cheatRuntime.PublishAsync(CMD_PLAYER_JUMP, jumpData, routerContext.GameplayRouter).Forget();
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Enemy_Spawn' to <b>GameplayRouter</b>");
                var spawnData = new GameData(new Vector3(10, 0, 10), Vector3.forward);
                cheatRuntime.PublishAsync(CMD_ENEMY_SPAWN, spawnData, routerContext.GameplayRouter).Forget();
            }
            // F9-F10: Additional examples
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                Debug.Log("<b>[Input]</b> Publishing class-type command 'Log_Message' to <b>Router.Default</b>");
                cheatRuntime.PublishClassAsync(CMD_LOG_MESSAGE, "Hello from a class-type command!").Forget();
            }
            else if (Input.GetKeyDown(KeyCode.F10))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Global_GameData' to <b>Router.Default</b>");
                var data = new GameData(Vector3.one, Vector3.forward);
                cheatRuntime.PublishAsync(CMD_GLOBAL_GAMEDATA, data).Forget();
            }
        }

        [Route]
        async UniTask OnGlobalCommand(CheatCommand cmd, CancellationToken ct)
        {
            // Use switch for better performance than if-else chain
            switch (cmd.CommandId)
            {
                case "Protocol_Simple":
                    Debug.Log("<color=cyan>[MultiRouterSampleRunner:Global] Received simple command.</color>");
                    break;
                case "Protocol_LongRunningTask":
                    Debug.Log("<color=magenta>[MultiRouterSampleRunner:Global] Starting long-running task... (5 seconds). Press F3 to cancel.</color>");
                    try
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: ct);
                        Debug.Log("<color=magenta>[MultiRouterSampleRunner:Global] Long-running task finished successfully.</color>");
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.LogWarning("<color=magenta>[MultiRouterSampleRunner:Global] Long-running task was canceled.</color>");
                    }
                    break;
            }
        }

        [Route]
        void OnGlobalDataCommand(CheatCommand<GameData> cmd)
        {
            // Skip logging for benchmark commands to avoid GC allocation
            if (cmd.CommandId == CheatSampleBenchmark.BENCHMARK_COMMAND ||
                cmd.CommandId == CheatSampleBenchmark.WARMUP_COMMAND)
            {
                return;
            }
            Debug.Log($"<color=green>[MultiRouterSampleRunner:Global] Received GameData command '{cmd.CommandId}'.</color>");
        }

        [Route]
        void OnGlobalStringCommand(CheatCommandClass<string> cmd)
        {
            Debug.Log($"<color=yellow>[MultiRouterSampleRunner:Global] Received string command '{cmd.CommandId}'. Message: '{cmd.Arg}'</color>");
        }
    }
}
