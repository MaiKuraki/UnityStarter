using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VitalRouter;

namespace CycloneGames.Cheat.Sample
{
    // --- Context that owns the private routers ---
    public class MultiRouterContext
    {
        public Router UIRouter { get; } = new();
        public Router GameplayRouter { get; } = new();
    }

    // --- A system that ONLY listens to the UI Router ---
    [Routes]
    public partial class SampleUISystem
    {
        public SampleUISystem(Router uiRouter)
        {
            MapTo(uiRouter);
            Debug.Log("<b>[SampleUISystem]</b> Mapped to UIRouter instance.");
        }

        [Route]
        void OnUICommand(CheatCommand cmd)
        {
            Debug.Log($"<color=aqua><b>[SampleUISystem]</b> Received UI Command: {cmd.CommandID}</color>");
        }
    }

    // --- A system that ONLY listens to the Gameplay Router ---
    [Routes]
    public partial class SampleGameplaySystem
    {
        public SampleGameplaySystem(Router gameplayRouter)
        {
            MapTo(gameplayRouter);
            Debug.Log("<b>[SampleGameplaySystem]</b> Mapped to GameplayRouter instance.");
        }

        [Route]
        void OnGameplayCommand(CheatCommand<GameData> cmd)
        {
            Debug.Log($"<color=orange><b>[SampleGameplaySystem]</b> Received Gameplay Command: {cmd.CommandID} with data {cmd.Arg.position}</color>");
        }
    }

    // --- The main MonoBehaviour that runs the entire sample ---
    [Routes]
    public partial class MultiRouterSampleRunner : MonoBehaviour
    {
        [SerializeField] CheatSampleBenchmark benchmarker;
        [SerializeField] Button Btn_Benchmark;
        private MultiRouterContext routerContext;
        private SampleUISystem uiSystem;
        private SampleGameplaySystem gameplaySystem;

        void Awake()
        {
            Debug.Log("<color=lime><b>[MultiRouterSampleRunner]</b> Initializing multi-router sample...</color>");
            routerContext = new MultiRouterContext();
            uiSystem = new SampleUISystem(routerContext.UIRouter);
            gameplaySystem = new SampleGameplaySystem(routerContext.GameplayRouter);
            Btn_Benchmark.onClick.AddListener(() => benchmarker.RunBenchmark().Forget());

            // Default is GlobalRouter            
            MapTo(Router.Default);
        }

        void OnDestroy()
        {
            UnmapRoutes();
        }

        private void Update()
        {
            // --- F1-F3: Publish to the GLOBAL Router (Router.Default) ---
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Protocol_Simple' to <b>Router.Default</b>");
                CheatCommandUtility.PublishCheatCommand("Protocol_Simple").Forget();
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Protocol_LongRunningTask' to <b>Router.Default</b>");
                CheatCommandUtility.PublishCheatCommand("Protocol_LongRunningTask").Forget();
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                Debug.Log("<b>[Input]</b> Cancelling 'Protocol_LongRunningTask' on <b>Router.Default</b>");
                CheatCommandUtility.CancelCheatCommand("Protocol_LongRunningTask");
            }

            // --- F5-F6: Publish to the UI Router ---
            if (Input.GetKeyDown(KeyCode.F5))
            {
                Debug.Log("<b>[Input]</b> Publishing 'UI_ShowPopup' to <b>UIRouter</b>");
                CheatCommandUtility.PublishCheatCommand("UI_ShowPopup", routerContext.UIRouter).Forget();
            }
            if (Input.GetKeyDown(KeyCode.F6))
            {
                Debug.Log("<b>[Input]</b> Publishing 'UI_HidePopup' to <b>UIRouter</b>");
                CheatCommandUtility.PublishCheatCommand("UI_HidePopup", routerContext.UIRouter).Forget();
            }

            // --- F7-F8: Publish to the Gameplay Router ---
            if (Input.GetKeyDown(KeyCode.F7))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Player_Jump' to <b>GameplayRouter</b>");
                var jumpData = new GameData(Vector3.up * 5, Vector3.zero);
                CheatCommandUtility.PublishCheatCommand("Player_Jump", jumpData, routerContext.GameplayRouter).Forget();
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Enemy_Spawn' to <b>GameplayRouter</b>");
                var spawnData = new GameData(new Vector3(10, 0, 10), Vector3.forward);
                CheatCommandUtility.PublishCheatCommand("Enemy_Spawn", spawnData, routerContext.GameplayRouter).Forget();
            }

            // --- F9: Publish a class type command ---
            if (Input.GetKeyDown(KeyCode.F9))
            {
                Debug.Log("<b>[Input]</b> Publishing class-type command 'Log_Message' to <b>Router.Default</b>");
                CheatCommandUtility.PublishCheatCommandWithClass("Log_Message", "Hello from a class-type command!").Forget();
            }          
            if (Input.GetKeyDown(KeyCode.F10))
            {
                Debug.Log("<b>[Input]</b> Publishing 'Global_GameData' to <b>Router.Default</b>");
                var data = new GameData(Vector3.one, Vector3.forward);
                CheatCommandUtility.PublishCheatCommand("Global_GameData", data).Forget();
            }
        }

        // --- Routes that were previously in CheatSampleGameLogic ---

        [Route]
        async UniTask OnGlobalCommand(CheatCommand cmd, CancellationToken ct)
        {
            switch (cmd.CommandID)
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
            if (cmd.CommandID == CheatSampleBenchmark.BENCHMARK_COMMAND)
            {
                //  No logging to avoid GC allocation during benchmark
                return;
            }
            Debug.Log($"<color=green>[MultiRouterSampleRunner:Global] Received GameData command '{cmd.CommandID}'.</color>");
        }

        [Route]
        void OnGlobalStringCommand(CheatCommandClass<string> cmd)
        {
            Debug.Log($"<color=yellow>[MultiRouterSampleRunner:Global] Received string command '{cmd.CommandID}'. Message: '{cmd.Arg}'</color>");
        }
    }
}
