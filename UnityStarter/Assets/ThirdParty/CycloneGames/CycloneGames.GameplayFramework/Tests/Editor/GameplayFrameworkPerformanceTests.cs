using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class GameplayFrameworkPerformanceTests
    {
        [Test, Performance]
        public void CameraContext_PushRemove_Benchmark()
        {
            var context = new CameraContext(null, 8);
            var mode = new NoOpCameraMode();

            Measure.Method(() =>
                {
                    context.TryPushCameraMode(mode);
                    context.RemoveCameraMode(mode);
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(50_000)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void GameSession_RegisterUnregister_Benchmark()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            PlayerController playerController = testWorld.World.SpawnActor(
                testWorld.World.Definition.PlayerControllerClass);
            PlayerState playerState = testWorld.World.SpawnActor(
                testWorld.World.Definition.PlayerStateClass);
            playerState.SetPlayerId(77);
            playerController.InitializePlayer(testWorld.World, playerState, null);
            var session = new GameSession(maxPlayers: 1, maxSpectators: 0);

            Measure.Method(() =>
                {
                    session.TryRegisterPlayer(playerController, spectator: false, out _);
                    session.UnregisterPlayer(playerController);
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(50_000)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void ActorTick_OneThousandOptInActors_Benchmark()
        {
            const int actorCount = 1_000;
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            NoOpTickActor prefab = testWorld.CreateAuthoringActor<NoOpTickActor>("NoOpTickActorPrefab");
            prefab.Configure();
            for (int i = 0; i < actorCount; i++)
            {
                testWorld.World.SpawnActor(prefab);
            }

            Assert.AreEqual(
                actorCount,
                testWorld.World.GetTickActorCount(ActorTickPhase.Update));
            testWorld.Instance.Tick(ActorTickPhase.Update, 1f / 60f);

            Measure.Method(() =>
                {
                    testWorld.Instance.Tick(ActorTickPhase.Update, 1f / 60f);
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(100)
                .GC()
                .Run();
        }

        private sealed class NoOpCameraMode : CameraMode
        {
            public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class NoOpTickActor : Actor
        {
            public void Configure()
            {
                ConfigureActorTick(ActorTickPhase.Update, startWithTickEnabled: true);
            }

            protected override void Tick(float deltaSeconds)
            {
                _ = deltaSeconds;
            }
        }
    }
}
