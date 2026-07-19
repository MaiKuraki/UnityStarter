using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public enum WorldSettingsReferenceSource : byte
    {
        DirectReference = 0,
        AssetReference = 1,
        PathLocation = 2,
    }

    /// <summary>
    /// Resolves external authoring references. Implementations are composed explicitly by
    /// <see cref="GameInstance"/>; the framework does not use a global resolver registry.
    /// </summary>
    public interface IWorldSettingsReferenceResolver
    {
        bool Supports(WorldSettingsReferenceSource source);

        UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
            string location,
            CancellationToken cancellationToken) where T : UnityEngine.Object;
    }

    /// <summary>
    /// Immutable runtime view of a <see cref="WorldSettings"/> asset. The world owns this
    /// object and disposes it when the world stops, releasing every external asset lease in
    /// reverse acquisition order.
    /// </summary>
    public sealed class WorldDefinition : IDisposable
    {
        private readonly IDisposable[] leases;
        private readonly int ownerThreadId;
        private int leaseCount;
        private bool isDisposed;

        internal WorldDefinition(
            GameMode gameModeClass,
            PlayerController playerControllerClass,
            Pawn pawnClass,
            PlayerState playerStateClass,
            CameraManager cameraManagerClass,
            SpectatorPawn spectatorPawnClass,
            IDisposable[] leases,
            int leaseCount)
        {
            GameModeClass = gameModeClass;
            PlayerControllerClass = playerControllerClass;
            PawnClass = pawnClass;
            PlayerStateClass = playerStateClass;
            CameraManagerClass = cameraManagerClass;
            SpectatorPawnClass = spectatorPawnClass;
            this.leases = leases ?? Array.Empty<IDisposable>();
            this.leaseCount = leaseCount;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public GameMode GameModeClass { get; }
        public PlayerController PlayerControllerClass { get; }
        public Pawn PawnClass { get; }
        public PlayerState PlayerStateClass { get; }
        public CameraManager CameraManagerClass { get; }
        public SpectatorPawn SpectatorPawnClass { get; }
        public bool IsDisposed => isDisposed;

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "WorldDefinition must be disposed on the thread that resolved it.");
            }

            isDisposed = true;
            for (int i = leaseCount - 1; i >= 0; i--)
            {
                try
                {
                    leases[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    leases[i] = null;
                }
            }

            leaseCount = 0;
        }
    }

    [CreateAssetMenu(fileName = "WorldSettings", menuName = "CycloneGames/GameplayFramework/WorldSettings")]
    public sealed class WorldSettings : ScriptableObject
    {
        private const int ReferenceCount = 6;

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

        // These properties expose authoring data only. Runtime code consumes WorldDefinition.
        public GameMode GameModeClass => gameModeClass;
        public PlayerController PlayerControllerClass => playerControllerClass;
        public Pawn PawnClass => pawnClass;
        public PlayerState PlayerStateClass => playerStateClass;
        public CameraManager CameraManagerClass => cameraManagerClass;
        public SpectatorPawn SpectatorPawnClass => spectatorPawnClass;

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

        /// <summary>
        /// Resolves this authoring asset into an immutable runtime definition. Expected
        /// configuration failures throw <see cref="InvalidOperationException"/>; cancellation
        /// is propagated unchanged. Every partially acquired lease is released before failure.
        /// </summary>
        public async UniTask<WorldDefinition> ResolveDefinitionAsync(
            IWorldSettingsReferenceResolver resolver = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(cancellationToken);

            var leases = new IDisposable[ReferenceCount];
            int leaseCount = 0;

            try
            {
                ResolvedReference<GameMode> gameMode = await ResolveRequiredReferenceAsync(
                    "GameModeClass", gameModeSource, gameModeClass, gameModeAssetLocation, resolver, cancellationToken);
                AddLease(gameMode.Lease, leases, ref leaseCount);

                ResolvedReference<PlayerController> playerController = await ResolveRequiredReferenceAsync(
                    "PlayerControllerClass", playerControllerSource, playerControllerClass, playerControllerAssetLocation, resolver, cancellationToken);
                AddLease(playerController.Lease, leases, ref leaseCount);

                ResolvedReference<Pawn> pawn = await ResolveRequiredReferenceAsync(
                    "PawnClass", pawnSource, pawnClass, pawnAssetLocation, resolver, cancellationToken);
                AddLease(pawn.Lease, leases, ref leaseCount);

                ResolvedReference<PlayerState> playerState = await ResolveRequiredReferenceAsync(
                    "PlayerStateClass", playerStateSource, playerStateClass, playerStateAssetLocation, resolver, cancellationToken);
                AddLease(playerState.Lease, leases, ref leaseCount);

                ResolvedReference<CameraManager> cameraManager = await ResolveOptionalReferenceAsync(
                    "CameraManagerClass", cameraManagerSource, cameraManagerClass, cameraManagerAssetLocation, resolver, cancellationToken);
                AddLease(cameraManager.Lease, leases, ref leaseCount);

                ResolvedReference<SpectatorPawn> spectatorPawn = await ResolveOptionalReferenceAsync(
                    "SpectatorPawnClass", spectatorPawnSource, spectatorPawnClass, spectatorPawnAssetLocation, resolver, cancellationToken);
                AddLease(spectatorPawn.Lease, leases, ref leaseCount);

                return new WorldDefinition(
                    gameMode.Asset,
                    playerController.Asset,
                    pawn.Asset,
                    playerState.Asset,
                    cameraManager.Asset,
                    spectatorPawn.Asset,
                    leases,
                    leaseCount);
            }
            catch
            {
                // An arbitrary resolver may fault or cancel from a worker thread. Rollback owns
                // Unity-related leases, so cleanup must return to the main thread first.
                await UniTask.SwitchToMainThread();
                DisposeLeases(leases, leaseCount);
                throw;
            }
        }

        public bool Validate(bool logWarnings = true)
        {
            bool valid = true;
            valid &= ValidateReference("GameModeClass", gameModeSource, gameModeClass, gameModeAssetLocation, true, logWarnings);
            valid &= ValidateReference("PlayerControllerClass", playerControllerSource, playerControllerClass, playerControllerAssetLocation, true, logWarnings);
            valid &= ValidateReference("PawnClass", pawnSource, pawnClass, pawnAssetLocation, true, logWarnings);
            valid &= ValidateReference("PlayerStateClass", playerStateSource, playerStateClass, playerStateAssetLocation, true, logWarnings);
            valid &= ValidateReference("CameraManagerClass", cameraManagerSource, cameraManagerClass, cameraManagerAssetLocation, false, logWarnings);
            valid &= ValidateReference("SpectatorPawnClass", spectatorPawnSource, spectatorPawnClass, spectatorPawnAssetLocation, false, logWarnings);
            return valid;
        }

        private async UniTask<ResolvedReference<T>> ResolveRequiredReferenceAsync<T>(
            string label,
            WorldSettingsReferenceSource source,
            T directReference,
            string location,
            IWorldSettingsReferenceResolver resolver,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            ResolvedReference<T> result = await ResolveReferenceAsync(
                label, source, directReference, location, resolver, cancellationToken, optional: false);

            if (result.Asset == null)
            {
                result.Lease?.Dispose();
                throw new InvalidOperationException($"WorldSettings '{name}' requires a valid {label}.");
            }

            return result;
        }

        private UniTask<ResolvedReference<T>> ResolveOptionalReferenceAsync<T>(
            string label,
            WorldSettingsReferenceSource source,
            T directReference,
            string location,
            IWorldSettingsReferenceResolver resolver,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            return ResolveReferenceAsync(label, source, directReference, location, resolver, cancellationToken, optional: true);
        }

        private async UniTask<ResolvedReference<T>> ResolveReferenceAsync<T>(
            string label,
            WorldSettingsReferenceSource source,
            T directReference,
            string location,
            IWorldSettingsReferenceResolver resolver,
            CancellationToken cancellationToken,
            bool optional) where T : UnityEngine.Object
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (source == WorldSettingsReferenceSource.DirectReference)
            {
                if (!optional && directReference == null)
                {
                    throw new InvalidOperationException($"WorldSettings '{name}' has no direct reference for {label}.");
                }

                return new ResolvedReference<T>(directReference, null);
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                if (optional)
                {
                    return default;
                }

                throw new InvalidOperationException(
                    $"WorldSettings '{name}' has no external location for required {label}.");
            }

            if (resolver == null || !resolver.Supports(source))
            {
                throw new InvalidOperationException(
                    $"WorldSettings '{name}' requires a resolver for source '{source}' ({label}).");
            }

            WorldSettingsAssetLoadResult<T> loadResult = await resolver.ResolveAsync<T>(location, cancellationToken);
            IDisposable pendingLease = loadResult.Lease;
            try
            {
                // Resolver implementations may complete on a worker thread. Validation touches
                // UnityEngine.Object and cleanup may release Unity-owned resources, so marshal
                // without cancellation before inspecting or transferring the returned lease.
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();

                if (!loadResult.Success || loadResult.Asset == null)
                {
                    string error = string.IsNullOrWhiteSpace(loadResult.Error)
                        ? "Unknown resolver failure."
                        : loadResult.Error;
                    throw new InvalidOperationException(
                        $"WorldSettings '{name}' could not resolve {label} from '{location}': {error}");
                }

                var resolved = new ResolvedReference<T>(loadResult.Asset, pendingLease);
                pendingLease = null;
                return resolved;
            }
            finally
            {
                if (pendingLease != null)
                {
                    try
                    {
                        pendingLease.Dispose();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }

        private bool ValidateReference<T>(
            string label,
            WorldSettingsReferenceSource source,
            T directReference,
            string assetLocation,
            bool required,
            bool logWarnings) where T : UnityEngine.Object
        {
            if (IsReferenceConfigured(source, directReference, assetLocation) || !required)
            {
                return true;
            }

            if (logWarnings)
            {
                Debug.LogWarning($"[WorldSettings] '{name}': required reference {label} is not configured.", this);
            }

            return false;
        }

        private static bool UsesExternalReference(WorldSettingsReferenceSource source)
        {
            return source != WorldSettingsReferenceSource.DirectReference;
        }

        private static bool IsReferenceConfigured<T>(
            WorldSettingsReferenceSource source,
            T directReference,
            string assetLocation) where T : UnityEngine.Object
        {
            return source == WorldSettingsReferenceSource.DirectReference
                ? directReference != null
                : !string.IsNullOrWhiteSpace(assetLocation);
        }

        private static void AddLease(IDisposable lease, IDisposable[] leases, ref int leaseCount)
        {
            if (lease == null)
            {
                return;
            }

            leases[leaseCount++] = lease;
        }

        private static void DisposeLeases(IDisposable[] leases, int leaseCount)
        {
            for (int i = leaseCount - 1; i >= 0; i--)
            {
                try
                {
                    leases[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    leases[i] = null;
                }
            }
        }

        private readonly struct ResolvedReference<T> where T : UnityEngine.Object
        {
            public ResolvedReference(T asset, IDisposable lease)
            {
                Asset = asset;
                Lease = lease;
            }

            public T Asset { get; }
            public IDisposable Lease { get; }
        }
    }

    public readonly struct WorldSettingsAssetLoadResult<T> where T : UnityEngine.Object
    {
        public WorldSettingsAssetLoadResult(bool success, T asset, string error, IDisposable lease = null)
        {
            Success = success;
            Asset = asset;
            Error = error;
            Lease = lease;
        }

        public bool Success { get; }
        public T Asset { get; }
        public string Error { get; }
        public IDisposable Lease { get; }
    }
}
