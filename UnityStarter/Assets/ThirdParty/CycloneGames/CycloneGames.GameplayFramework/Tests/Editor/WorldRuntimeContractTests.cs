using System;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class WorldRuntimeContractTests
    {
        [Test]
        public void StartWorld_InvalidNetMode_IsRejectedBeforeWorldPublication()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                testWorld.StartWorld((WorldNetMode)byte.MaxValue));
            Assert.IsNull(testWorld.Instance.CurrentWorld);
        }

        [Test]
        public void LiveWorldReads_RejectWorkerThreadAccess()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            World world = testWorld.World;
            GameMode gameMode = world.GameMode;
            OwnerThreadReadOnlyList<PlayerController> cachedControllerView = world.PlayerControllers;
            OwnerThreadReadOnlyList<PlayerController>.Enumerator cachedControllerEnumerator =
                cachedControllerView.GetEnumerator();
            OwnerThreadReadOnlyList<LocalPlayer> cachedLocalPlayerView = testWorld.Instance.LocalPlayers;
            LocalPlayer cachedLocalPlayer = cachedLocalPlayerView[0];
            IWorldDefinition cachedDefinition = world.Definition;
            Action[] reads =
            {
                () => _ = world.LifecycleState,
                () => _ = world.GameInstance,
                () => _ = world.GetGameInstance(),
                () => _ = world.MatchClock,
                () => _ = world.Definition,
                () => _ = world.LifetimeToken,
                () => _ = world.SceneTransitionHandler,
                () => _ = world.GameMode,
                () => _ = world.GameState,
                () => _ = world.PlayerControllers.Count,
                () => _ = world.ActorCount,
                () => _ = world.GetPlayerController(0),
                () => _ = world.GetTickActorCount(ActorTickPhase.Update),
                () => _ = world.IsActorRegistered(gameMode),
                () => world.TryGetActor<GameMode>(out _),
                () => _ = cachedControllerView.Count,
                () => _ = cachedControllerView[0],
                () => _ = cachedControllerEnumerator.MoveNext(),
                () => _ = cachedLocalPlayerView.Count,
                () => _ = cachedLocalPlayer.PlayerController,
                () => _ = testWorld.Instance.MatchClock,
                () => _ = cachedDefinition.PawnClass,
            };
            var failures = new Exception[reads.Length];
            var thread = new Thread(() =>
            {
                for (int i = 0; i < reads.Length; i++)
                {
                    try
                    {
                        reads[i]();
                    }
                    catch (Exception exception)
                    {
                        failures[i] = exception;
                    }
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "Worker thread did not finish within the test timeout.");
            for (int i = 0; i < failures.Length; i++)
            {
                Assert.IsInstanceOf<InvalidOperationException>(
                    failures[i],
                    $"Live World read at index {i} did not reject worker-thread access.");
            }
        }
    }
}
