using System;
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

        [NonSerialized] private IDisposable resolvedGameModeLease;
        [NonSerialized] private IDisposable resolvedPlayerControllerLease;
        [NonSerialized] private IDisposable resolvedPawnLease;
        [NonSerialized] private IDisposable resolvedPlayerStateLease;
        [NonSerialized] private IDisposable resolvedCameraManagerLease;
        [NonSerialized] private IDisposable resolvedSpectatorPawnLease;

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
            valid &= await ResolveReferenceAsync<GameMode>("GameModeClass", gameModeSource, gameModeAssetLocation, SetResolvedGameMode, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<PlayerController>("PlayerControllerClass", playerControllerSource, playerControllerAssetLocation, SetResolvedPlayerController, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<Pawn>("PawnClass", pawnSource, pawnAssetLocation, SetResolvedPawn, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<PlayerState>("PlayerStateClass", playerStateSource, playerStateAssetLocation, SetResolvedPlayerState, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<CameraManager>("CameraManagerClass", cameraManagerSource, cameraManagerAssetLocation, SetResolvedCameraManager, cancellationToken, logWarnings);
            valid &= await ResolveReferenceAsync<SpectatorPawn>("SpectatorPawnClass", spectatorPawnSource, spectatorPawnAssetLocation, SetResolvedSpectatorPawn, cancellationToken, logWarnings);
            return valid;
        }

        public void ClearResolvedReferences()
        {
            ClearResolvedReference(ref resolvedGameModeClass, ref resolvedGameModeLease);
            ClearResolvedReference(ref resolvedPlayerControllerClass, ref resolvedPlayerControllerLease);
            ClearResolvedReference(ref resolvedPawnClass, ref resolvedPawnLease);
            ClearResolvedReference(ref resolvedPlayerStateClass, ref resolvedPlayerStateLease);
            ClearResolvedReference(ref resolvedCameraManagerClass, ref resolvedCameraManagerLease);
            ClearResolvedReference(ref resolvedSpectatorPawnClass, ref resolvedSpectatorPawnLease);
        }

        private void OnDisable()
        {
            ClearResolvedReferences();
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
            Action<T, IDisposable> assignResolvedReference,
            CancellationToken cancellationToken,
            bool logWarnings) where T : UnityEngine.Object
        {
            if (source == WorldSettingsReferenceSource.DirectReference)
            {
                assignResolvedReference(null, null);
                return true;
            }

            if (string.IsNullOrWhiteSpace(assetLocation))
            {
                assignResolvedReference(null, null);
                if (logWarnings)
                {
                    Debug.LogWarning($"[WorldSettings] '{name}': {label} is set to {GetSourceDescription(source)} mode but no location is configured.");
                }
                return false;
            }

            WorldSettingsAssetLoadResult<T> result = await WorldSettingsReferenceResolverRegistry.ResolveAsync<T>(source, assetLocation, cancellationToken);

            if (result.Success)
            {
                if (result.Asset == null)
                {
                    result.Lease?.Dispose();
                    assignResolvedReference(null, null);
                    if (logWarnings)
                    {
                        Debug.LogWarning($"[WorldSettings] '{name}': Resolver returned success for {label} from '{assetLocation}' but the asset was null.");
                    }
                    return false;
                }

                assignResolvedReference(result.Asset, result.Lease);
                return true;
            }

            result.Lease?.Dispose();
            assignResolvedReference(null, null);

            if (logWarnings)
            {
                Debug.LogWarning($"[WorldSettings] '{name}': Failed to resolve {label} from '{assetLocation}'. {result.Error}");
            }

            return false;
        }

        private void SetResolvedGameMode(GameMode asset, IDisposable lease)
        {
            ReplaceResolvedReference(asset, lease, ref resolvedGameModeClass, ref resolvedGameModeLease);
        }

        private void SetResolvedPlayerController(PlayerController asset, IDisposable lease)
        {
            ReplaceResolvedReference(asset, lease, ref resolvedPlayerControllerClass, ref resolvedPlayerControllerLease);
        }

        private void SetResolvedPawn(Pawn asset, IDisposable lease)
        {
            ReplaceResolvedReference(asset, lease, ref resolvedPawnClass, ref resolvedPawnLease);
        }

        private void SetResolvedPlayerState(PlayerState asset, IDisposable lease)
        {
            ReplaceResolvedReference(asset, lease, ref resolvedPlayerStateClass, ref resolvedPlayerStateLease);
        }

        private void SetResolvedCameraManager(CameraManager asset, IDisposable lease)
        {
            ReplaceResolvedReference(asset, lease, ref resolvedCameraManagerClass, ref resolvedCameraManagerLease);
        }

        private void SetResolvedSpectatorPawn(SpectatorPawn asset, IDisposable lease)
        {
            ReplaceResolvedReference(asset, lease, ref resolvedSpectatorPawnClass, ref resolvedSpectatorPawnLease);
        }

        private static void ReplaceResolvedReference<T>(T asset, IDisposable lease, ref T resolvedReference, ref IDisposable resolvedLease) where T : UnityEngine.Object
        {
            if (!ReferenceEquals(resolvedLease, lease))
            {
                resolvedLease?.Dispose();
            }

            resolvedReference = asset;
            resolvedLease = lease;
        }

        private static void ClearResolvedReference<T>(ref T resolvedReference, ref IDisposable resolvedLease) where T : UnityEngine.Object
        {
            resolvedReference = null;
            resolvedLease?.Dispose();
            resolvedLease = null;
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
        public readonly IDisposable Lease;

        public WorldSettingsAssetLoadResult(bool success, T asset, string error, IDisposable lease = null)
        {
            Success = success;
            Asset = asset;
            Error = error;
            Lease = lease;
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

        public static bool HasResolver(WorldSettingsReferenceSource source)
        {
            return FindResolver(source) != null;
        }

        internal static async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(WorldSettingsReferenceSource source, string location, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            IWorldSettingsReferenceResolver resolver = FindResolver(source);
            if (resolver != null)
            {
                return await resolver.ResolveAsync<T>(location, cancellationToken);
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
}
