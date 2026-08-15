using System;
using System.Threading;
using CycloneGames.Logging;
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
            IWorldSettingsLeaseRegistrar leaseRegistrar,
            CancellationToken cancellationToken) where T : UnityEngine.Object;
    }

    /// <summary>
    /// Core-owned transfer point for one external asset lease per resolve call. Core reserves the
    /// slot before invoking the resolver, so the first non-null registration cannot fail because
    /// of capacity. A resolver that creates multiple handles must combine them into one lease,
    /// then register it before any await, cancellation observation, validation, or callback that
    /// can fail. The resolver must not dispose a registered lease.
    /// </summary>
    public interface IWorldSettingsLeaseRegistrar
    {
        void Register(IDisposable lease);
    }

    /// <summary>
    /// Retryable, owner-thread-bound ownership container for external WorldSettings leases whose
    /// rollback could not be confirmed. Successful disposal clears each slot immediately; a
    /// failed slot remains quarantined until a later <see cref="Dispose"/> call succeeds.
    /// </summary>
    public sealed class WorldSettingsLeaseQuarantine :
        IWorldSettingsLeaseRegistrar,
        IDisposable
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        private readonly IDisposable[] leases;
        private readonly int ownerThreadId;
        private int upperBound;
        private int pendingLeaseCount;
        private int activeRegistrationIndex = -1;
        private bool isDisposed;

        internal WorldSettingsLeaseQuarantine(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            leases = new IDisposable[capacity];
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public int PendingLeaseCount
        {
            get
            {
                AssertOwnerThread();
                return pendingLeaseCount;
            }
        }

        public bool IsDisposed
        {
            get
            {
                AssertOwnerThread();
                return isDisposed;
            }
        }

        public void Register(IDisposable lease)
        {
            AssertOwnerThread();
            if (lease == null)
            {
                return;
            }
            if (isDisposed || activeRegistrationIndex < 0)
            {
                throw new InvalidOperationException(
                    "WorldSettings lease registration is not active.");
            }

            if (leases[activeRegistrationIndex] != null)
            {
                throw new InvalidOperationException(
                    "A WorldSettings resolver may register only one lease per resolve call.");
            }

            leases[activeRegistrationIndex] = lease;
            pendingLeaseCount++;
        }

        public void Dispose()
        {
            AssertOwnerThread();
            if (isDisposed)
            {
                return;
            }

            CompleteRegistration();

            OutOfMemoryException terminalOutOfMemory = null;
            for (int index = upperBound - 1; index >= 0; index--)
            {
                IDisposable lease = leases[index];
                if (lease == null)
                {
                    continue;
                }

                try
                {
                    lease.Dispose();
                    leases[index] = null;
                    pendingLeaseCount--;
                }
                catch (Exception exception)
                {
                    if (!TryCaptureOutOfMemory(ref terminalOutOfMemory, exception))
                    {
                        try
                        {
                            Log.Error(
                                exception,
                                "WorldSettings external asset lease cleanup failed; the lease remains quarantined for retry.");
                        }
                        catch (Exception loggingException)
                        {
                            TryCaptureOutOfMemory(
                                ref terminalOutOfMemory,
                                loggingException);
                        }
                    }
                }
            }

            while (upperBound > 0 && leases[upperBound - 1] == null)
            {
                upperBound--;
            }

            isDisposed = pendingLeaseCount == 0;
            if (terminalOutOfMemory != null)
            {
                throw terminalOutOfMemory;
            }
        }

        internal void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "WorldSettings lease ownership must be accessed on the thread that began resolution.");
            }
        }

        internal void BeginRegistration()
        {
            AssertOwnerThread();
            if (isDisposed || activeRegistrationIndex >= 0 || upperBound >= leases.Length)
            {
                throw new InvalidOperationException(
                    "WorldSettings lease ownership cannot reserve another registration.");
            }

            activeRegistrationIndex = upperBound++;
        }

        internal void CompleteRegistration()
        {
            AssertOwnerThread();
            if (activeRegistrationIndex < 0)
            {
                return;
            }

            if (leases[activeRegistrationIndex] == null &&
                activeRegistrationIndex == upperBound - 1)
            {
                upperBound--;
            }

            activeRegistrationIndex = -1;
        }

        private static bool TryCaptureOutOfMemory(
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
    }

    /// <summary>
    /// Reports a resolution failure whose external leases could not all be rolled back. The
    /// owning GameInstance adopts those handles before the exception leaves the start transaction.
    /// </summary>
    public sealed class WorldSettingsLeaseCleanupException : Exception
    {
        private readonly WorldSettingsLeaseQuarantine leaseQuarantine;
        private bool ownershipTransferred;

        internal WorldSettingsLeaseCleanupException(
            WorldSettingsLeaseQuarantine leaseQuarantine)
            : base(
                "WorldSettings resolution failed and one or more external asset leases remain quarantined.")
        {
            this.leaseQuarantine = leaseQuarantine ??
                throw new ArgumentNullException(nameof(leaseQuarantine));
        }

        public Exception ResolutionFailure { get; private set; }
        public Exception CleanupFailure { get; private set; }
        public int PendingLeaseCount => leaseQuarantine.PendingLeaseCount;

        internal void Initialize(Exception resolutionFailure, Exception cleanupFailure)
        {
            ResolutionFailure = resolutionFailure;
            CleanupFailure = cleanupFailure;
        }

        internal WorldSettingsLeaseQuarantine TakeLeaseQuarantine()
        {
            if (ownershipTransferred)
            {
                throw new InvalidOperationException(
                    "WorldSettings lease quarantine ownership was already transferred.");
            }

            ownershipTransferred = true;
            return leaseQuarantine;
        }
    }

    /// <summary>
    /// Out-of-memory variant of rollback failure. The first cleanup OOM remains available through
    /// <see cref="CleanupFailure"/>, while the owning GameInstance adopts failed handles before
    /// this exception leaves the start transaction.
    /// </summary>
    public sealed class WorldSettingsLeaseCleanupOutOfMemoryException : OutOfMemoryException
    {
        private readonly WorldSettingsLeaseQuarantine leaseQuarantine;
        private bool ownershipTransferred;

        internal WorldSettingsLeaseCleanupOutOfMemoryException(
            WorldSettingsLeaseQuarantine leaseQuarantine)
            : base(
                "WorldSettings resolution rollback encountered out-of-memory and retained unresolved external asset leases.")
        {
            this.leaseQuarantine = leaseQuarantine ??
                throw new ArgumentNullException(nameof(leaseQuarantine));
        }

        public Exception ResolutionFailure { get; private set; }
        public Exception CleanupFailure { get; private set; }
        public OutOfMemoryException OutOfMemoryFailure { get; private set; }
        public int PendingLeaseCount => leaseQuarantine.PendingLeaseCount;

        internal void Initialize(
            Exception resolutionFailure,
            Exception cleanupFailure,
            OutOfMemoryException outOfMemoryFailure)
        {
            ResolutionFailure = resolutionFailure;
            CleanupFailure = cleanupFailure;
            OutOfMemoryFailure = outOfMemoryFailure ??
                throw new ArgumentNullException(nameof(outOfMemoryFailure));
        }

        internal WorldSettingsLeaseQuarantine TakeLeaseQuarantine()
        {
            if (ownershipTransferred)
            {
                throw new InvalidOperationException(
                    "WorldSettings lease quarantine ownership was already transferred.");
            }

            ownershipTransferred = true;
            return leaseQuarantine;
        }
    }

    /// <summary>
    /// Read-only runtime view resolved from a <see cref="WorldSettings"/> asset. World is the
    /// sole lifetime owner; consumers cannot dispose the underlying external asset leases.
    /// </summary>
    public interface IWorldDefinition
    {
        GameMode GameModeClass { get; }
        PlayerController PlayerControllerClass { get; }
        Pawn PawnClass { get; }
        PlayerState PlayerStateClass { get; }
        CameraManager CameraManagerClass { get; }
        SpectatorPawn SpectatorPawnClass { get; }
    }

    internal sealed class WorldDefinition : IWorldDefinition, IDisposable
    {
        private readonly WorldSettingsLeaseQuarantine leaseQuarantine;
        private bool isDisposed;

        internal WorldDefinition(
            GameMode gameModeClass,
            PlayerController playerControllerClass,
            Pawn pawnClass,
            PlayerState playerStateClass,
            CameraManager cameraManagerClass,
            SpectatorPawn spectatorPawnClass,
            WorldSettingsLeaseQuarantine leaseQuarantine)
        {
            this.gameModeClass = gameModeClass;
            this.playerControllerClass = playerControllerClass;
            this.pawnClass = pawnClass;
            this.playerStateClass = playerStateClass;
            this.cameraManagerClass = cameraManagerClass;
            this.spectatorPawnClass = spectatorPawnClass;
            this.leaseQuarantine = leaseQuarantine ??
                throw new ArgumentNullException(nameof(leaseQuarantine));
        }

        private readonly GameMode gameModeClass;
        private readonly PlayerController playerControllerClass;
        private readonly Pawn pawnClass;
        private readonly PlayerState playerStateClass;
        private readonly CameraManager cameraManagerClass;
        private readonly SpectatorPawn spectatorPawnClass;

        public GameMode GameModeClass
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return gameModeClass;
            }
        }

        public PlayerController PlayerControllerClass
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return playerControllerClass;
            }
        }

        public Pawn PawnClass
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return pawnClass;
            }
        }

        public PlayerState PlayerStateClass
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return playerStateClass;
            }
        }

        public CameraManager CameraManagerClass
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return cameraManagerClass;
            }
        }

        public SpectatorPawn SpectatorPawnClass
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return spectatorPawnClass;
            }
        }

        public bool IsDisposed
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return isDisposed;
            }
        }

        public int PendingLeaseCount
        {
            get
            {
                leaseQuarantine.AssertOwnerThread();
                return leaseQuarantine.PendingLeaseCount;
            }
        }

        public void Dispose()
        {
            leaseQuarantine.AssertOwnerThread();
            if (isDisposed)
            {
                return;
            }

            try
            {
                leaseQuarantine.Dispose();
            }
            finally
            {
                isDisposed = leaseQuarantine.IsDisposed;
            }
        }
    }

    [CreateAssetMenu(fileName = "WorldSettings", menuName = "CycloneGames/GameplayFramework/WorldSettings")]
    public sealed class WorldSettings : ScriptableObject
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;
        private const int ReferenceCount = 6;

        [Header("Game Mode")]
        [SerializeField] private GameMode gameModeClass;
        [SerializeField] private WorldSettingsReferenceSource gameModeSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string gameModeAssetLocation;

        [Header("Player")]
        [SerializeField] private PlayerController playerControllerClass;
        [SerializeField] private WorldSettingsReferenceSource playerControllerSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string playerControllerAssetLocation;

        [SerializeField] private Pawn pawnClass;
        [SerializeField] private WorldSettingsReferenceSource pawnSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string pawnAssetLocation;

        [SerializeField] private PlayerState playerStateClass;
        [SerializeField] private WorldSettingsReferenceSource playerStateSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string playerStateAssetLocation;

        [Header("Camera")]
        [SerializeField] private CameraManager cameraManagerClass;
        [SerializeField] private WorldSettingsReferenceSource cameraManagerSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string cameraManagerAssetLocation;

        [Header("Spectator")]
        [SerializeField] private SpectatorPawn spectatorPawnClass;
        [SerializeField] private WorldSettingsReferenceSource spectatorPawnSource = WorldSettingsReferenceSource.DirectReference;
        [SerializeField] private string spectatorPawnAssetLocation;

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
        /// is propagated unchanged when rollback succeeds. GameInstance adopts any unresolved
        /// rollback handles before the diagnostic lease-cleanup exception leaves startup.
        /// </summary>
        internal async UniTask<WorldDefinition> ResolveDefinitionAsync(
            IWorldSettingsReferenceResolver resolver = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(cancellationToken);

            var leaseQuarantine = new WorldSettingsLeaseQuarantine(ReferenceCount);
            var rollbackFailure = new WorldSettingsLeaseCleanupException(leaseQuarantine);
            var rollbackOutOfMemory =
                new WorldSettingsLeaseCleanupOutOfMemoryException(leaseQuarantine);

            try
            {
                ResolvedReference<GameMode> gameMode = await ResolveRequiredReferenceAsync(
                    "GameModeClass", gameModeSource, gameModeClass, gameModeAssetLocation,
                    resolver, leaseQuarantine, cancellationToken);

                ResolvedReference<PlayerController> playerController = await ResolveRequiredReferenceAsync(
                    "PlayerControllerClass", playerControllerSource, playerControllerClass, playerControllerAssetLocation,
                    resolver, leaseQuarantine, cancellationToken);

                ResolvedReference<Pawn> pawn = await ResolveRequiredReferenceAsync(
                    "PawnClass", pawnSource, pawnClass, pawnAssetLocation,
                    resolver, leaseQuarantine, cancellationToken);

                ResolvedReference<PlayerState> playerState = await ResolveRequiredReferenceAsync(
                    "PlayerStateClass", playerStateSource, playerStateClass, playerStateAssetLocation,
                    resolver, leaseQuarantine, cancellationToken);

                ResolvedReference<CameraManager> cameraManager = await ResolveOptionalReferenceAsync(
                    "CameraManagerClass", cameraManagerSource, cameraManagerClass, cameraManagerAssetLocation,
                    resolver, leaseQuarantine, cancellationToken);

                ResolvedReference<SpectatorPawn> spectatorPawn = await ResolveOptionalReferenceAsync(
                    "SpectatorPawnClass", spectatorPawnSource, spectatorPawnClass, spectatorPawnAssetLocation,
                    resolver, leaseQuarantine, cancellationToken);

                return new WorldDefinition(
                    gameMode.Asset,
                    playerController.Asset,
                    pawn.Asset,
                    playerState.Asset,
                    cameraManager.Asset,
                    spectatorPawn.Asset,
                    leaseQuarantine);
            }
            catch (Exception resolutionFailure)
            {
                // An arbitrary resolver may fault or cancel from a worker thread. Rollback owns
                // Unity-related leases, so cleanup must return to the main thread first.
                try
                {
                    await UniTask.SwitchToMainThread();
                }
                catch (Exception cleanupFailure)
                {
                    OutOfMemoryException terminalOutOfMemory =
                        FindOutOfMemory(resolutionFailure) ??
                        FindOutOfMemory(cleanupFailure);
                    if (terminalOutOfMemory != null)
                    {
                        rollbackOutOfMemory.Initialize(
                            resolutionFailure,
                            cleanupFailure,
                            terminalOutOfMemory);
                        throw rollbackOutOfMemory;
                    }

                    rollbackFailure.Initialize(resolutionFailure, cleanupFailure);
                    throw rollbackFailure;
                }

                leaseQuarantine.CompleteRegistration();
                if (leaseQuarantine.PendingLeaseCount == 0)
                {
                    throw;
                }

                OutOfMemoryException cleanupOutOfMemory = null;
                try
                {
                    leaseQuarantine.Dispose();
                }
                catch (OutOfMemoryException exception)
                {
                    cleanupOutOfMemory = exception;
                }

                if (!leaseQuarantine.IsDisposed)
                {
                    OutOfMemoryException terminalOutOfMemory =
                        FindOutOfMemory(resolutionFailure) ??
                        cleanupOutOfMemory;
                    if (terminalOutOfMemory != null)
                    {
                        rollbackOutOfMemory.Initialize(
                            resolutionFailure,
                            cleanupOutOfMemory,
                            terminalOutOfMemory);
                        throw rollbackOutOfMemory;
                    }

                    rollbackFailure.Initialize(resolutionFailure, cleanupFailure: null);
                    throw rollbackFailure;
                }

                throw;
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
            WorldSettingsLeaseQuarantine leaseQuarantine,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            ResolvedReference<T> result = await ResolveReferenceAsync(
                label, source, directReference, location, resolver, leaseQuarantine,
                cancellationToken, optional: false);

            if (result.Asset == null)
            {
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
            WorldSettingsLeaseQuarantine leaseQuarantine,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            return ResolveReferenceAsync(
                label, source, directReference, location, resolver, leaseQuarantine,
                cancellationToken, optional: true);
        }

        private async UniTask<ResolvedReference<T>> ResolveReferenceAsync<T>(
            string label,
            WorldSettingsReferenceSource source,
            T directReference,
            string location,
            IWorldSettingsReferenceResolver resolver,
            WorldSettingsLeaseQuarantine leaseQuarantine,
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

                return new ResolvedReference<T>(directReference);
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

            leaseQuarantine.BeginRegistration();
            WorldSettingsAssetLoadResult<T> loadResult = await resolver.ResolveAsync<T>(
                location,
                leaseQuarantine,
                cancellationToken);

            // Resolver implementations may complete on a worker thread. Validation touches
            // UnityEngine.Object and cleanup may release Unity-owned resources, so marshal
            // without cancellation before inspecting the returned asset.
            await UniTask.SwitchToMainThread();
            leaseQuarantine.CompleteRegistration();
            cancellationToken.ThrowIfCancellationRequested();

            if (!loadResult.Success || loadResult.Asset == null)
            {
                string error = string.IsNullOrWhiteSpace(loadResult.Error)
                    ? "Unknown resolver failure."
                    : loadResult.Error;
                throw new InvalidOperationException(
                    $"WorldSettings '{name}' could not resolve {label} from '{location}': {error}");
            }

            return new ResolvedReference<T>(loadResult.Asset);
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
                Log.Warning(
                    (AssetName: name, Label: label),
                    static (state, builder) =>
                    {
                        builder.Append("WorldSettings '");
                        builder.Append(state.AssetName);
                        builder.Append("' required reference '");
                        builder.Append(state.Label);
                        builder.Append("' is not configured.");
                    });
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

        private readonly struct ResolvedReference<T> where T : UnityEngine.Object
        {
            public ResolvedReference(T asset)
            {
                Asset = asset;
            }

            public T Asset { get; }
        }
    }

    public readonly struct WorldSettingsAssetLoadResult<T> where T : UnityEngine.Object
    {
        public WorldSettingsAssetLoadResult(bool success, T asset, string error)
        {
            Success = success;
            Asset = asset;
            Error = error;
        }

        public bool Success { get; }
        public T Asset { get; }
        public string Error { get; }
    }
}
