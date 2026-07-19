using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class PlayerStateTests
    {
        private readonly List<GameObject> objects = new List<GameObject>(4);

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void Snapshot_RoundTripsFrameworkIdentity()
        {
            PlayerState source = CreatePlayerState("Source");
            source.SetPlayerName("PlayerOne");
            source.SetPlayerId(42);
            Assert.IsTrue(source.TryRestoreSnapshot(new PlayerStateSnapshot
            {
                PlayerName = "PlayerOne",
                PlayerId = 42,
                IsSpectator = true,
                SchemaVersion = PlayerStateSnapshot.CurrentSchemaVersion,
            }, out _));

            PlayerStateSnapshot snapshot = source.CaptureSnapshot();
            PlayerState target = CreatePlayerState("Target");

            Assert.IsTrue(target.TryRestoreSnapshot(snapshot, out string error), error);
            Assert.AreEqual("PlayerOne", target.GetPlayerName());
            Assert.AreEqual(42, target.GetPlayerId());
            Assert.IsTrue(target.IsSpectator());
            Assert.AreEqual(PlayerStateSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        }

        [Test]
        public void Snapshot_RejectsEveryNonCurrentSchema()
        {
            PlayerState state = CreatePlayerState("State");
            Assert.IsFalse(state.TryRestoreSnapshot(new PlayerStateSnapshot
            {
                SchemaVersion = 0,
                PlayerName = "Invalid",
                PlayerId = 7,
            }, out string missingVersionError));
            StringAssert.Contains("Unsupported", missingVersionError);

            Assert.IsFalse(state.TryRestoreSnapshot(new PlayerStateSnapshot
            {
                SchemaVersion = PlayerStateSnapshot.CurrentSchemaVersion + 1,
                PlayerName = "Future",
                PlayerId = 8,
            }, out string error));
            StringAssert.Contains("Unsupported", error);
            Assert.IsNull(state.GetPlayerName());
        }

        [Test]
        public void CopyProperties_CopiesIdentityWithoutPawn()
        {
            PlayerState source = CreatePlayerState("Source");
            source.TryRestoreSnapshot(new PlayerStateSnapshot
            {
                PlayerName = "CopiedPlayer",
                PlayerId = 11,
                IsSpectator = true,
            }, out _);
            PlayerState target = CreatePlayerState("Target");

            target.CopyProperties(source);

            Assert.AreEqual("CopiedPlayer", target.GetPlayerName());
            Assert.AreEqual(11, target.GetPlayerId());
            Assert.IsTrue(target.IsSpectator());
        }

        [Test]
        public void Possession_PublishesPlayerStatePawnAfterCommit()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Controller controllerPrefab = testWorld.CreateAuthoringActor<Controller>("ControllerPrefab");
            Controller controller = testWorld.World.SpawnActor(controllerPrefab);
            PlayerState playerState = testWorld.World.SpawnActor(testWorld.World.Definition.PlayerStateClass);
            Pawn pawn = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            controller.Initialize(testWorld.World, playerState);
            int eventCount = 0;

            playerState.OnPawnSetEvent += (state, newPawn, oldPawn) =>
            {
                eventCount++;
                Assert.AreSame(newPawn, state.GetPawn());
                Assert.AreSame(newPawn, controller.GetPawn());
                if (newPawn != null)
                {
                    Assert.AreSame(controller, newPawn.Controller);
                }
            };

            controller.Possess(pawn);

            Assert.AreEqual(1, eventCount);
            Assert.AreSame(pawn, playerState.GetPawn());
        }

        [Test]
        public void PlayerName_IsBounded()
        {
            PlayerState state = CreatePlayerState("State");
            Assert.Throws<System.ArgumentException>(() =>
                state.SetPlayerName(new string('x', PlayerLoginRequest.MaxPlayerNameLength + 1)));
        }

        private PlayerState CreatePlayerState(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject.AddComponent<PlayerState>();
        }
    }
}
