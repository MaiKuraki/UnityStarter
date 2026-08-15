using System;
using CycloneGames.GameplayFramework.Core;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Explicit dependencies consumed by GameplayWorldHost. DI containers and manual bootstrap
    /// code construct the same value and retain ownership of the supplied services.
    /// </summary>
    public sealed class GameplayWorldComposition
    {
        public GameplayWorldComposition(
            IActorLifetime actorLifetime,
            IGameplayWorldTerminalCleanupOwner terminalCleanupOwner,
            IWorldSettingsReferenceResolver referenceResolver = null,
            ISceneTransitionHandler sceneTransitionHandler = null,
            IGameSession gameSession = null,
            WorldRuntimeLimits runtimeLimits = null,
            IWorldActorSource actorSource = null,
            IMatchClock matchClock = null,
            ICameraOutputLeaseArbiter cameraOutputLeaseArbiter = null)
        {
            ActorLifetime = actorLifetime ?? throw new ArgumentNullException(nameof(actorLifetime));
            TerminalCleanupOwner = terminalCleanupOwner ??
                throw new ArgumentNullException(nameof(terminalCleanupOwner));
            ReferenceResolver = referenceResolver;
            SceneTransitionHandler = sceneTransitionHandler;
            GameSession = gameSession;
            RuntimeLimits = runtimeLimits ?? WorldRuntimeLimits.Default;
            ActorSource = actorSource;
            MatchClock = matchClock ?? UnityMatchClock.Scaled;
            CameraOutputLeaseArbiter =
                cameraOutputLeaseArbiter ?? new CameraOutputLeaseArbiter();
        }

        public IActorLifetime ActorLifetime { get; }
        public IGameplayWorldTerminalCleanupOwner TerminalCleanupOwner { get; }
        public IWorldSettingsReferenceResolver ReferenceResolver { get; }
        public ISceneTransitionHandler SceneTransitionHandler { get; }
        public IGameSession GameSession { get; }
        public WorldRuntimeLimits RuntimeLimits { get; }
        public IWorldActorSource ActorSource { get; }
        public IMatchClock MatchClock { get; }
        public ICameraOutputLeaseArbiter CameraOutputLeaseArbiter { get; }

        public static GameplayWorldComposition CreateDefault(
            IGameplayWorldTerminalCleanupOwner terminalCleanupOwner,
            WorldRuntimeLimits runtimeLimits = null,
            IWorldActorSource actorSource = null,
            IMatchClock matchClock = null,
            ICameraOutputLeaseArbiter cameraOutputLeaseArbiter = null)
        {
            return new GameplayWorldComposition(
                new UnityActorLifetime(),
                terminalCleanupOwner,
                runtimeLimits: runtimeLimits,
                actorSource: actorSource,
                matchClock: matchClock,
                cameraOutputLeaseArbiter: cameraOutputLeaseArbiter);
        }
    }
}
