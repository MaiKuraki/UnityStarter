using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public enum WorldSettingsReferenceSource
    {
        DirectReference = 0,
        AssetReference = 1,
        PathLocation = 2,
    }

    public interface IWorldSettings
    {
        GameMode GameModeClass { get; }
        PlayerController PlayerControllerClass { get; }
        Pawn PawnClass { get; }
        PlayerState PlayerStateClass { get; }
        CameraManager CameraManagerClass { get; }
        SpectatorPawn SpectatorPawnClass { get; }
    }

    public interface IResolvableWorldSettings
    {
        bool UsesExternalReferences { get; }
        UniTask<bool> ResolveReferencesAsync(CancellationToken cancellationToken = default, bool logWarnings = true);
        void ClearResolvedReferences();
    }

    public interface IWorldSettingsReferenceResolver
    {
        bool Supports(WorldSettingsReferenceSource source);
        UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(string location, CancellationToken cancellationToken) where T : UnityEngine.Object;
    }

    public static class WorldSettingsExtensions
    {
        public static UniTask<bool> ResolveReferencesAsync(this IWorldSettings worldSettings, CancellationToken cancellationToken = default, bool logWarnings = true)
        {
            if (worldSettings is IResolvableWorldSettings resolvableWorldSettings)
            {
                return resolvableWorldSettings.ResolveReferencesAsync(cancellationToken, logWarnings);
            }

            return UniTask.FromResult(true);
        }
    }

    [CreateAssetMenu(fileName = "WorldSettings", menuName = "CycloneGames/GameplayFramework/WorldSettings")]
    public class WorldSettings : ScriptableObject, IWorldSettings, IResolvableWorldSettings
    {
        [Header("Game Mode")]
        [SerializeField] private GameMode gameModeClass;
        [SerializeField] private WorldSettingsReferenceSource gameModeSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string gameModeAssetLocation;
        [SerializeField] private string gameModeAssetGuid;

        [Header("Player")]
        [SerializeField] private PlayerController playerControllerClass;
        [SerializeField] private WorldSettingsReferenceSource playerControllerSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string playerControllerAssetLocation;
        [SerializeField] private string playerControllerAssetGuid;

        [SerializeField] private Pawn pawnClass;
        [SerializeField] private WorldSettingsReferenceSource pawnSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string pawnAssetLocation;
        [SerializeField] private string pawnAssetGuid;

        [SerializeField] private PlayerState playerStateClass;
        [SerializeField] private WorldSettingsReferenceSource playerStateSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string playerStateAssetLocation;
        [SerializeField] private string playerStateAssetGuid;

        [Header("Camera")]
        [SerializeField] private CameraManager cameraManagerClass;
        [SerializeField] private WorldSettingsReferenceSource cameraManagerSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string cameraManagerAssetLocation;
        [SerializeField] private string cameraManagerAssetGuid;

        [Header("Spectator")]
        [SerializeField] private SpectatorPawn spectatorPawnClass;
        [SerializeField] private WorldSettingsReferenceSource spectatorPawnSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string spectatorPawnAssetLocation;
        [SerializeField] private string spectatorPawnAssetGuid;

        [NonSerialized] private GameMode resolvedGameModeClass;
        [NonSerialized] private PlayerController resolvedPlayerControllerClass;
        [NonSerialized] private Pawn resolvedPawnClass;
        [NonSerialized] private PlayerState resolvedPlayerStateClass;
        [NonSerialized] private CameraManager resolvedCameraManagerClass;
        [NonSerialized] private SpectatorPawn resolvedSpectatorPawnClass;

        public GameMode GameModeClass => GetResolvedReference(gameModeSource, gameModeClass, resolvedGameModeClass);
        public PlayerController PlayerControllerClass => GetResolvedReference(playerControllerSource, playerControllerClass, resolvedPlayerControllerClass);
        public Pawn PawnClass => GetResolvedReference(pawnSource, pawnClass, resolvedPawnClass);
        public PlayerState PlayerStateClass => GetResolvedReference(playerStateSource, playerStateClass, resolvedPlayerStateClass);
        public CameraManager CameraManagerClass => GetResolvedReference(cameraManagerSource, cameraManagerClass, resolvedCameraManagerClass);
        public SpectatorPawn SpectatorPawnClass => GetResolvedReference(spectatorPawnSource, spectatorPawnClass, resolvedSpectatorPawnClass);

        public WorldSettingsReferenceSource GameModeSource => gameModeSource;
        public WorldSettingsReferenceSource PlayerControllerSource => playerControllerSource;
        public WorldSettingsReferenceSource PawnSource => pawnSource;
        public WorldSettingsReferenceSource PlayerStateSource => playerStateSource;
        public WorldSettingsReferenceSource CameraManagerSource => cameraManagerSource;
        public WorldSettingsReferenceSource SpectatorPawnSource => spectatorPawnSource;

        public string GameModeAssetLocation => gameModeAssetLocation;
        public string PlayerControllerAssetLocation => playerControllerAssetLocation;
        public string PawnAssetLocation => pawnAssetLocation;
        public string PlayerStateAssetLocation => playerStateAssetLocation;
        public string CameraManagerAssetLocation => cameraManagerAssetLocation;
        public string SpectatorPawnAssetLocation => spectatorPawnAssetLocation;

        public bool UsesExternalReferences =>
            UsesExternalReference(gameModeSource) ||
            UsesExternalReference(playerControllerSource) ||
            UsesExternalReference(pawnSource) ||
            UsesExternalReference(playerStateSource) ||
            UsesExternalReference(cameraManagerSource) ||
            UsesExternalReference(spectatorPawnSource);

        public bool HasConfiguredGameMode => IsReferenceConfigured(gameModeSource, gameModeClass, gameModeAssetLocation);
        public bool HasConfiguredPlayerController => IsReferenceConfigured(playerControllerSource, playerControllerClass, playerControllerAssetLocation);
        public bool HasConfiguredPawn => IsReferenceConfigured(pawnSource, pawnClass, pawnAssetLocation);
        public bool HasConfiguredPlayerState => IsReferenceConfigured(playerStateSource, playerStateClass, playerStateAssetLocation);
        public bool HasConfiguredCameraManager => IsReferenceConfigured(cameraManagerSource, cameraManagerClass, cameraManagerAssetLocation);
        public bool HasConfiguredSpectatorPawn => IsReferenceConfigured(spectatorPawnSource, spectatorPawnClass, spectatorPawnAssetLocation);

        public async UniTask<bool> ResolveReferencesAsync(CancellationToken cancellationToken = default, bool logWarnings = true)
        {
            if (!UsesExternalReferences)
            {
                return true;
            }

            bool valid = true;
            valid &= await ResolveReferenceAsync<GameMode>("GameModeClass", gameModeSource, gameModeAssetLocation, asset => resolvedGameModeClass = asset, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<PlayerController>("PlayerControllerClass", playerControllerSource, playerControllerAssetLocation, asset => resolvedPlayerControllerClass = asset, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<Pawn>("PawnClass", pawnSource, pawnAssetLocation, asset => resolvedPawnClass = asset, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<PlayerState>("PlayerStateClass", playerStateSource, playerStateAssetLocation, asset => resolvedPlayerStateClass = asset, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<CameraManager>("CameraManagerClass", cameraManagerSource, cameraManagerAssetLocation, asset => resolvedCameraManagerClass = asset, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<SpectatorPawn>("SpectatorPawnClass", spectatorPawnSource, spectatorPawnAssetLocation, asset => resolvedSpectatorPawnClass = asset, cancellationToken, logWarnings);
            return valid;
        }

        public void ClearResolvedReferences()
        {
            resolvedGameModeClass = null;
            resolvedPlayerControllerClass = null;
            resolvedPawnClass = null;
            resolvedPlayerStateClass = null;
            resolvedCameraManagerClass = null;
            resolvedSpectatorPawnClass = null;
        }

        /// <summary>
        /// Validates that all required references are assigned.
        /// Call this explicitly when the WorldSettings is about to be used (e.g., at game startup).
        /// </summary>
        public bool Validate(bool logWarnings = true)
        {
            bool valid = true;
            valid &= ValidateReference("GameModeClass", gameModeSource, gameModeClass, gameModeAssetLocation, true, logWarnings);
            valid &= ValidateReference("PlayerControllerClass", playerControllerSource, playerControllerClass, playerControllerAssetLocation, true, logWarnings);
            valid &= ValidateReference("PawnClass", pawnSource, pawnClass, pawnAssetLocation, true, logWarnings);
            valid &= ValidateReference("PlayerStateClass", playerStateSource, playerStateClass, playerStateAssetLocation, false, logWarnings);
            valid &= ValidateReference("CameraManagerClass", cameraManagerSource, cameraManagerClass, cameraManagerAssetLocation, false, logWarnings);
            valid &= ValidateReference("SpectatorPawnClass", spectatorPawnSource, spectatorPawnClass, spectatorPawnAssetLocation, false, logWarnings);
            return valid;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ClearDirectReferenceWhenUsingAssetSource(gameModeSource, ref gameModeClass);
            ClearDirectReferenceWhenUsingAssetSource(playerControllerSource, ref playerControllerClass);
            ClearDirectReferenceWhenUsingAssetSource(pawnSource, ref pawnClass);
            ClearDirectReferenceWhenUsingAssetSource(playerStateSource, ref playerStateClass);
            ClearDirectReferenceWhenUsingAssetSource(cameraManagerSource, ref cameraManagerClass);
            ClearDirectReferenceWhenUsingAssetSource(spectatorPawnSource, ref spectatorPawnClass);

            if (!Application.isPlaying)
            {
                ClearResolvedReferences();
            }
        }
#endif

        private static T GetResolvedReference<T>(WorldSettingsReferenceSource source, T directReference, T resolvedReference) where T : UnityEngine.Object
        {
            return source == WorldSettingsReferenceSource.DirectReference ? directReference : resolvedReference;
        }

        private static bool UsesExternalReference(WorldSettingsReferenceSource source)
        {
            return source != WorldSettingsReferenceSource.DirectReference;
        }

        private static bool IsReferenceConfigured<T>(WorldSettingsReferenceSource source, T directReference, string assetLocation) where T : UnityEngine.Object
        {
            return source == WorldSettingsReferenceSource.DirectReference
                ? directReference != null
                : !string.IsNullOrWhiteSpace(assetLocation);
        }

        private bool ValidateReference<T>(string label, WorldSettingsReferenceSource source, T directReference, string assetLocation, bool required, bool logWarnings) where T : UnityEngine.Object
        {
            if (IsReferenceConfigured(source, directReference, assetLocation))
            {
                return true;
            }

            if (!required)
            {
                return true;
            }

            if (logWarnings)
            {
                string sourceLabel = GetSourceDescription(source);
                Debug.LogWarning($"[WorldSettings] '{name}': {label} is not assigned for {sourceLabel} mode.");
            }

            return false;
        }

        private async UniTask<bool> ResolveReferenceAsync<T>(
            string label,
            WorldSettingsReferenceSource source,
            string assetLocation,
            Action<T> assignResolvedReference,
            CancellationToken cancellationToken,
            bool logWarnings) where T : UnityEngine.Object
        {
            if (source == WorldSettingsReferenceSource.DirectReference)
            {
                assignResolvedReference(null);
                return true;
            }

            if (string.IsNullOrWhiteSpace(assetLocation))
            {
                assignResolvedReference(null);
                if (logWarnings)
                {
                    Debug.LogWarning($"[WorldSettings] '{name}': {label} is set to {GetSourceDescription(source)} mode but no location is configured.");
                }
                return false;
            }

            WorldSettingsAssetLoadResult<T> result = await WorldSettingsReferenceResolverRegistry.ResolveAsync<T>(source, assetLocation, cancellationToken);
            assignResolvedReference(result.Asset);

            if (result.Success)
            {
                return true;
            }

            if (logWarnings)
            {
                Debug.LogWarning($"[WorldSettings] '{name}': Failed to resolve {label} from '{assetLocation}'. {result.Error}");
            }

            return false;
        }

#if UNITY_EDITOR
        private static void ClearDirectReferenceWhenUsingAssetSource<T>(WorldSettingsReferenceSource source, ref T directReference) where T : UnityEngine.Object
        {
            if (source != WorldSettingsReferenceSource.DirectReference)
            {
                directReference = null;
            }
        }
#endif

        private static string GetSourceDescription(WorldSettingsReferenceSource source)
        {
            switch (source)
            {
                case WorldSettingsReferenceSource.DirectReference:
                    return "direct reference";
                case WorldSettingsReferenceSource.AssetReference:
                    return "asset reference location";
                case WorldSettingsReferenceSource.PathLocation:
                    return "path location";
                default:
                    return "external reference";
            }
        }
    }

    public readonly struct WorldSettingsAssetLoadResult<T> where T : UnityEngine.Object
    {
        public readonly bool Success;
        public readonly T Asset;
        public readonly string Error;

        public WorldSettingsAssetLoadResult(bool success, T asset, string error)
        {
            Success = success;
            Asset = asset;
            Error = error;
        }
    }

    public static class WorldSettingsReferenceResolverRegistry
    {
        private static readonly System.Collections.Generic.List<IWorldSettingsReferenceResolver> resolvers = new System.Collections.Generic.List<IWorldSettingsReferenceResolver>();
        private static readonly object resolverLock = new object();

        public static void Register(IWorldSettingsReferenceResolver resolver)
        {
            if (resolver == null)
            {
                return;
            }

            lock (resolverLock)
            {
                if (!resolvers.Contains(resolver))
                {
                    resolvers.Add(resolver);
                }
            }
        }

        public static void Unregister(IWorldSettingsReferenceResolver resolver)
        {
            if (resolver == null)
            {
                return;
            }

            lock (resolverLock)
            {
                resolvers.Remove(resolver);
            }
        }

        internal static async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(WorldSettingsReferenceSource source, string location, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            IWorldSettingsReferenceResolver resolver = FindResolver(source);
            if (resolver != null)
            {
                return await resolver.ResolveAsync<T>(location, cancellationToken);
            }

            if (source == WorldSettingsReferenceSource.AssetReference)
            {
                return await WorldSettingsAssetManagementBridge.LoadAssetAsync<T>(location, cancellationToken);
            }

            return new WorldSettingsAssetLoadResult<T>(false, null, $"No resolver registered for source mode '{source}'.");
        }

        private static IWorldSettingsReferenceResolver FindResolver(WorldSettingsReferenceSource source)
        {
            lock (resolverLock)
            {
                for (int i = resolvers.Count - 1; i >= 0; i--)
                {
                    if (resolvers[i] != null && resolvers[i].Supports(source))
                    {
                        return resolvers[i];
                    }
                }
            }

            return null;
        }
    }

    public static class WorldSettingsAssetManagementBridge
    {
        private const string AssetManagementLocatorTypeName = "CycloneGames.AssetManagement.Runtime.AssetManagementLocator, CycloneGames.AssetManagement.Runtime";

        private static readonly Type assetManagementLocatorType = Type.GetType(AssetManagementLocatorTypeName);
        private static readonly PropertyInfo defaultPackageProperty = assetManagementLocatorType?.GetProperty("DefaultPackage", BindingFlags.Public | BindingFlags.Static);

        public static bool IsAvailable => defaultPackageProperty != null;

        internal static async UniTask<WorldSettingsAssetLoadResult<T>> LoadAssetAsync<T>(string location, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            if (!IsAvailable)
            {
                return new WorldSettingsAssetLoadResult<T>(false, null, "CycloneGames.AssetManagement is not available.");
            }

            object package = defaultPackageProperty.GetValue(null, null);
            if (package == null)
            {
                return new WorldSettingsAssetLoadResult<T>(false, null, "AssetManagementLocator.DefaultPackage is null.");
            }

            MethodInfo loadMethod = FindLoadAssetAsyncMethod(package.GetType());
            if (loadMethod == null)
            {
                return new WorldSettingsAssetLoadResult<T>(false, null, "Default package does not expose LoadAssetAsync<T>().");
            }

            object handle = null;

            try
            {
                handle = loadMethod.MakeGenericMethod(typeof(T)).Invoke(package, new object[]
                {
                    location,
                    null,
                    null,
                    null,
                    cancellationToken,
                });

                if (handle == null)
                {
                    return new WorldSettingsAssetLoadResult<T>(false, null, "Asset handle creation returned null.");
                }

                PropertyInfo taskProperty = handle.GetType().GetProperty("Task", BindingFlags.Public | BindingFlags.Instance);
                if (taskProperty != null && taskProperty.GetValue(handle, null) is UniTask task)
                {
                    await task.AttachExternalCancellation(cancellationToken);
                }

                string error = handle.GetType().GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)?.GetValue(handle, null) as string;
                if (!string.IsNullOrEmpty(error))
                {
                    return new WorldSettingsAssetLoadResult<T>(false, null, error);
                }

                T asset = handle.GetType().GetProperty("Asset", BindingFlags.Public | BindingFlags.Instance)?.GetValue(handle, null) as T;
                if (asset == null)
                {
                    return new WorldSettingsAssetLoadResult<T>(false, null, "Asset handle completed but returned null.");
                }

                return new WorldSettingsAssetLoadResult<T>(true, asset, null);
            }
            catch (TargetInvocationException ex)
            {
                return new WorldSettingsAssetLoadResult<T>(false, null, ex.InnerException?.Message ?? ex.Message);
            }
            catch (Exception ex)
            {
                return new WorldSettingsAssetLoadResult<T>(false, null, ex.Message);
            }
            finally
            {
                if (handle is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static MethodInfo FindLoadAssetAsyncMethod(Type packageType)
        {
            MethodInfo[] methods = packageType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "LoadAssetAsync" || !method.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 5)
                {
                    return method;
                }
            }

            return null;
        }
    }
}