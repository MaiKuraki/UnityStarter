using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class GameStateAndPlayerStartTests
    {
        private readonly List<GameObject> objects = new List<GameObject>(4);

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void GameState_PlayerArrayRejectsDuplicates()
        {
            GameState gameState = CreateActor<GameState>("GameState");
            PlayerState playerState = CreateActor<PlayerState>("PlayerState");

            Assert.IsFalse(gameState.AddPlayerState(null));
            Assert.IsTrue(gameState.AddPlayerState(playerState));
            Assert.IsFalse(gameState.AddPlayerState(playerState));
            Assert.AreEqual(1, gameState.GetNumPlayers());
            Assert.AreSame(playerState, gameState.PlayerArray[0]);
        }

        [Test]
        public void MatchState_EnforcesLegalTransitionTable()
        {
            TestGameState gameState = CreateActor<TestGameState>("GameState");

            Assert.IsFalse(gameState.TrySetMatchState(MatchState.InProgress, out _));
            Assert.IsTrue(gameState.TrySetMatchState(MatchState.WaitingToStart, out _));
            Assert.IsTrue(gameState.TrySetMatchState(MatchState.InProgress, out _));
            Assert.IsTrue(gameState.TrySetMatchState(MatchState.WaitingPostMatch, out _));
            Assert.AreEqual(3, gameState.MatchStateChangeCount);
            Assert.AreEqual(MatchState.WaitingPostMatch, gameState.MatchState);
        }

        [Test]
        public void MatchState_RejectsReentrantTransitionFromObserver()
        {
            ReentrantGameState gameState = CreateActor<ReentrantGameState>("ReentrantGameState");

            Assert.IsTrue(gameState.TrySetMatchState(MatchState.WaitingToStart, out _));

            Assert.IsFalse(gameState.ReentrantTransitionResult);
            Assert.AreEqual(MatchState.WaitingToStart, gameState.MatchState);
        }

        [Test]
        public void MatchState_UsesInjectedDoublePrecisionClockAndRestoresSnapshot()
        {
            var clock = new ManualMatchClock();
            TestGameState source = CreateActor<TestGameState>("SourceGameState");
            source.ConfigureMatchClock(clock);

            clock.Seconds = 1d;
            Assert.IsTrue(source.TrySetMatchState(MatchState.WaitingToStart, out _));
            clock.Seconds = 2d;
            Assert.IsTrue(source.TrySetMatchState(MatchState.InProgress, out _));
            clock.Seconds = 5d;
            MatchStateSnapshot snapshot = source.CaptureMatchStateSnapshot();
            Assert.AreEqual(3d, snapshot.ElapsedSeconds);

            TestGameState target = CreateActor<TestGameState>("TargetGameState");
            target.ConfigureMatchClock(clock);
            clock.Seconds = 7d;
            Assert.IsTrue(
                target.TryRestoreMatchStateSnapshot(snapshot, out string error),
                error);

            Assert.AreEqual(MatchState.InProgress, target.MatchState);
            Assert.AreEqual(5d, target.ElapsedTimeSeconds);
            Assert.AreEqual(1, target.MatchStateChangeCount);
        }

        [Test]
        public void MatchState_RestoreRejectsDifferentClockEpochWithoutMutation()
        {
            var sourceClock = new ManualMatchClock();
            GameState source = CreateActor<GameState>("SourceGameState");
            source.ConfigureMatchClock(sourceClock);
            MatchStateSnapshot snapshot = source.CaptureMatchStateSnapshot();

            var targetClock = new ManualMatchClock();
            GameState target = CreateActor<GameState>("TargetGameState");
            target.ConfigureMatchClock(targetClock);

            Assert.IsFalse(target.TryRestoreMatchStateSnapshot(snapshot, out string error));
            StringAssert.Contains("clock epoch", error);
            Assert.AreEqual(MatchState.EnteringMap, target.MatchState);
        }

        [Test]
        public void MatchState_ClockCannotChangeAfterRuntimeStateExists()
        {
            GameState gameState = CreateActor<GameState>("GameState");
            var clock = new ManualMatchClock();
            gameState.ConfigureMatchClock(clock);
            _ = gameState.ElapsedTimeSeconds;

            Assert.Throws<InvalidOperationException>(
                () => gameState.ConfigureMatchClock(new ManualMatchClock()));
            Assert.DoesNotThrow(() => gameState.ConfigureMatchClock(clock));
        }

        [Test]
        public void WorldComposition_ConfiguresReplicatedGameStateClock()
        {
            var clock = new ManualMatchClock();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: clock);
            GameState gameState = testWorld.CreateAuthoringActor<GameState>("ReplicatedGameState");
            testWorld.World.RegisterActor(gameState);
            testWorld.World.SetReplicatedGameState(gameState);

            clock.Seconds = 1d;
            Assert.IsTrue(gameState.TrySetMatchState(MatchState.WaitingToStart, out _));
            MatchStateSnapshot snapshot = gameState.CaptureMatchStateSnapshot();

            Assert.AreSame(clock, testWorld.Instance.MatchClock);
            Assert.AreSame(clock, testWorld.World.MatchClock);
            Assert.AreEqual(clock.ClockEpoch, snapshot.ClockEpoch);
        }

        [Test]
        public void RegisterReplicatedGameState_ConfiguresClockBeforeBeginPlay()
        {
            var clock = new ManualMatchClock { Seconds = 42d };
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: clock);
            BeginPlayReadingGameState gameState =
                testWorld.CreateAuthoringActor<BeginPlayReadingGameState>("ReplicatedGameState");

            Assert.DoesNotThrow(() => testWorld.World.RegisterActor(gameState));
            Assert.DoesNotThrow(() => testWorld.World.SetReplicatedGameState(gameState));

            Assert.IsTrue(gameState.ReadElapsedDuringBeginPlay);
            Assert.AreEqual(0d, gameState.ElapsedDuringBeginPlay);
            Assert.AreEqual(clock.ClockEpoch, gameState.CaptureMatchStateSnapshot().ClockEpoch);
        }

        [Test]
        public void PlayerStarts_AreScopedToOwningWorld()
        {
            PlayerStart expected = null;
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                configure: fixture => expected = fixture.CreateAuthoringActor<PlayerStart>("SpawnA"));

            Assert.AreEqual(1, testWorld.World.PlayerStarts.Count);
            Assert.AreSame(expected, testWorld.World.PlayerStarts[0]);
        }

        private T CreateActor<T>(string name) where T : Actor
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        private sealed class TestGameState : GameState
        {
            public int MatchStateChangeCount { get; private set; }

            protected override void OnMatchStateChanged(MatchState oldState, MatchState newState)
            {
                MatchStateChangeCount++;
            }
        }

        private sealed class ReentrantGameState : GameState
        {
            public bool ReentrantTransitionResult { get; private set; }

            protected override void OnMatchStateChanged(MatchState oldState, MatchState newState)
            {
                ReentrantTransitionResult = TrySetMatchState(MatchState.Aborted, out _);
            }
        }

        private sealed class BeginPlayReadingGameState : GameState
        {
            public bool ReadElapsedDuringBeginPlay { get; private set; }
            public double ElapsedDuringBeginPlay { get; private set; }

            protected override void BeginPlay()
            {
                ElapsedDuringBeginPlay = ElapsedTimeSeconds;
                ReadElapsedDuringBeginPlay = true;
            }
        }

        private sealed class ManualMatchClock : IMatchClock
        {
            private readonly Guid clockEpoch = Guid.NewGuid();

            public Guid ClockEpoch => clockEpoch;
            public double Seconds { get; set; }

            public MatchTimestamp CurrentTimestamp =>
                new MatchTimestamp(clockEpoch, Seconds);
        }
    }
}
