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
            IWorldSettingsReferenceResolver referenceResolver = null,
            ISceneTransitionHandler sceneTransitionHandler = null,
            IGameSession gameSession = null,
            WorldRuntimeLimits runtimeLimits = null)
        {
            ActorLifetime = actorLifetime ?? throw new ArgumentNullException(nameof(actorLifetime));
            ReferenceResolver = referenceResolver;
            SceneTransitionHandler = sceneTransitionHandler;
            GameSession = gameSession;
            RuntimeLimits = runtimeLimits ?? WorldRuntimeLimits.Default;
        }

        public IActorLifetime ActorLifetime { get; }
        public IWorldSettingsReferenceResolver ReferenceResolver { get; }
        public ISceneTransitionHandler SceneTransitionHandler { get; }
        public IGameSession GameSession { get; }
        public WorldRuntimeLimits RuntimeLimits { get; }

        public static GameplayWorldComposition CreateDefault(WorldRuntimeLimits runtimeLimits = null)
        {
            return new GameplayWorldComposition(
                new UnityActorLifetime(),
                runtimeLimits: runtimeLimits);
        }
    }
}
