using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Logger;
using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Sample.PureUnity
{
    public class UnitySampleBoot : MonoBehaviour
    {
        private const string DEBUG_FLAG = "<color=cyan>[UnitySampleBoot]</color>";
        private IUnityObjectSpawner objectSpawner;
        private bool isBootstrapped;

        async void Start()
        {
            if (isBootstrapped)
            {
                return;
            }

            isBootstrapped = true;

            try
            {
                if (!TryInitializeWorld(out World world))
                {
                    return;
                }

                objectSpawner = new UnitySampleObjectSpawner();

                // This WorldSettings ScriptableObject is located at /Samples/Sample.PureUnity/Resources/UnitySampleWorldSettings.asset
                if (!TryLoadWorldSettings(out WorldSettings worldSettings))
                {
                    return;
                }

                if (!await TryResolveWorldSettingsAsync(worldSettings, this.GetCancellationTokenOnDestroy()))
                {
                    return;
                }

                if (!TryCreateGameMode(worldSettings, out GameMode gameMode, out IGameMode gameModeLifecycle))
                {
                    return;
                }

                gameMode.Initialize(objectSpawner, worldSettings);
                world.SetGameMode(gameMode);
                await gameModeLifecycle.LaunchGameModeAsync(this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Boot was cancelled because the object was destroyed.");
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Unexpected boot failure: {ex}");
            }
        }

        private bool TryInitializeWorld(out World world)
        {
            if (UnitySampleGameInstance.Instance.World == null)
            {
                UnitySampleGameInstance.Instance.InitializeWorld();
            }

            world = UnitySampleGameInstance.Instance.World;
            if (world == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to initialize World.");
                return false;
            }

            return true;
        }

        private bool TryLoadWorldSettings(out WorldSettings worldSettings)
        {
            worldSettings = Resources.Load<WorldSettings>("UnitySampleWorldSettings");
            if (worldSettings == null)
            {
                CLogger.LogError(
                    $"{DEBUG_FLAG} Missing WorldSettings asset 'UnitySampleWorldSettings' in Resources. " +
                    "Expected path: Samples/Sample.PureUnity/Resources/UnitySampleWorldSettings.asset");
                return false;
            }

            return true;
        }

        private async UniTask<bool> TryResolveWorldSettingsAsync(WorldSettings worldSettings, CancellationToken cancellationToken)
        {
            if (!await worldSettings.ResolveReferencesAsync(cancellationToken))
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to resolve WorldSettings references. Check Direct Ref / Asset Ref / Path configuration, AssetManagementLocator.DefaultPackage, or your custom IWorldSettingsReferenceResolver registration.");
                return false;
            }

            if (worldSettings.GameModeClass == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} WorldSettings.GameModeClass is null after resolution. Please assign a GameMode prefab, asset location, or custom path entry.");
                return false;
            }

            return true;
        }

        private bool TryCreateGameMode(WorldSettings worldSettings, out GameMode gameMode, out IGameMode gameModeLifecycle)
        {
            gameMode = objectSpawner.Create(worldSettings.GameModeClass) as GameMode;
            if (gameMode == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to spawn GameMode from WorldSettings.GameModeClass.");
                gameModeLifecycle = null;
                return false;
            }

            gameModeLifecycle = gameMode as IGameMode;
            if (gameModeLifecycle == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Spawned GameMode does not implement IGameMode.");
                return false;
            }

            return true;
        }
    }
}
