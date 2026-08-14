using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Logging;
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
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            GameState gameState = RegisterActor<GameState>(testWorld, "GameState");
            PlayerState playerState = RegisterActor<PlayerState>(testWorld, "PlayerState");

            Assert.IsFalse(gameState.AddPlayerState(null));
            Assert.IsTrue(gameState.AddPlayerState(playerState));
            Assert.IsFalse(gameState.AddPlayerState(playerState));
            Assert.AreEqual(1, gameState.GetNumPlayers());
            Assert.AreSame(playerState, gameState.PlayerArray[0]);
        }

        [Test]
        public void MatchState_EnforcesLegalTransitionTable()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            TestGameState gameState = RegisterActor<TestGameState>(testWorld, "GameState");

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
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            ReentrantGameState gameState =
                RegisterActor<ReentrantGameState>(testWorld, "ReentrantGameState");

            Assert.IsTrue(gameState.TrySetMatchState(MatchState.WaitingToStart, out _));

            Assert.IsFalse(gameState.ReentrantTransitionResult);
            Assert.AreEqual(MatchState.WaitingToStart, gameState.MatchState);
        }

        [Test]
        public void MatchState_UsesInjectedDoublePrecisionClockAndRestoresSnapshot()
        {
            var clock = new ManualMatchClock();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: clock);
            TestGameState source = RegisterActor<TestGameState>(testWorld, "SourceGameState");

            clock.Seconds = 1d;
            Assert.IsTrue(source.TrySetMatchState(MatchState.WaitingToStart, out _));
            clock.Seconds = 2d;
            Assert.IsTrue(source.TrySetMatchState(MatchState.InProgress, out _));
            clock.Seconds = 5d;
            MatchStateSnapshot snapshot = source.CaptureMatchStateSnapshot();
            Assert.AreEqual(3d, snapshot.ElapsedSeconds);

            TestGameState target = RegisterActor<TestGameState>(testWorld, "TargetGameState");
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
            using GameplayTestWorld sourceWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                discoverActiveSceneActors: false,
                matchClock: sourceClock);
            GameState source = RegisterActor<GameState>(sourceWorld, "SourceGameState");
            MatchStateSnapshot snapshot = source.CaptureMatchStateSnapshot();

            var targetClock = new ManualMatchClock();
            using GameplayTestWorld targetWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                discoverActiveSceneActors: false,
                matchClock: targetClock);
            GameState target = RegisterActor<GameState>(targetWorld, "TargetGameState");

            Assert.IsFalse(target.TryRestoreMatchStateSnapshot(snapshot, out string error));
            StringAssert.Contains("clock epoch", error);
            Assert.AreEqual(MatchState.EnteringMap, target.MatchState);
        }

        [Test]
        public void MatchStateObserverOutOfMemory_PropagatesAfterCommittedTransitionAndClearsGuard()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            ThrowingGameState gameState =
                RegisterActor<ThrowingGameState>(testWorld, "GameState");
            var expectedOutOfMemory = new OutOfMemoryException(
                "Synthetic direct match-state observer exhaustion.");
            gameState.MatchStateChangedException = expectedOutOfMemory;

            OutOfMemoryException actualOutOfMemory = Assert.Throws<OutOfMemoryException>(() =>
                gameState.TrySetMatchState(MatchState.WaitingToStart, out _));
            Assert.AreSame(expectedOutOfMemory, actualOutOfMemory);
            Assert.AreEqual(MatchState.WaitingToStart, gameState.MatchState);

            gameState.MatchStateChangedException = null;
            Assert.IsTrue(gameState.TrySetMatchState(MatchState.InProgress, out string error), error);
            Assert.AreEqual(MatchState.InProgress, gameState.MatchState);
        }

        [Test]
        public void MatchStateObserverNestedOutOfMemory_PropagatesSameInstanceAndClearsGuard()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            ThrowingGameState gameState =
                RegisterActor<ThrowingGameState>(testWorld, "GameState");
            var expectedOutOfMemory = new OutOfMemoryException(
                "Synthetic nested match-state observer exhaustion.");
            gameState.MatchStateChangedException = new AggregateException(
                new InvalidOperationException("Synthetic non-terminal observer failure."),
                new AggregateException(expectedOutOfMemory));

            OutOfMemoryException actualOutOfMemory = Assert.Throws<OutOfMemoryException>(() =>
                gameState.TrySetMatchState(MatchState.WaitingToStart, out _));

            Assert.AreSame(expectedOutOfMemory, actualOutOfMemory);
            Assert.AreEqual(MatchState.WaitingToStart, gameState.MatchState);

            gameState.MatchStateChangedException = null;
            Assert.IsTrue(gameState.TrySetMatchState(MatchState.InProgress, out string error), error);
            Assert.AreEqual(MatchState.InProgress, gameState.MatchState);
        }

        [Test]
        public void MatchStateObserverOrdinaryException_IsolatedAfterCommittedTransition()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            ThrowingGameState gameState =
                RegisterActor<ThrowingGameState>(testWorld, "GameState");
            gameState.MatchStateChangedException =
                new InvalidOperationException("Synthetic ordinary match-state observer failure.");
            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));

            try
            {
                Assert.IsTrue(
                    gameState.TrySetMatchState(MatchState.WaitingToStart, out string error),
                    error);
                Assert.AreEqual(MatchState.WaitingToStart, gameState.MatchState);

                gameState.MatchStateChangedException = null;
                Assert.IsTrue(gameState.TrySetMatchState(MatchState.InProgress, out error), error);
                Assert.AreEqual(MatchState.InProgress, gameState.MatchState);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }
        }

        [Test]
        public void RestoreObserverOutOfMemory_PropagatesAfterCommittedRestoreAndClearsGuard()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            GameState source = RegisterActor<GameState>(testWorld, "SourceGameState");
            Assert.IsTrue(source.TrySetMatchState(MatchState.WaitingToStart, out _));
            MatchStateSnapshot snapshot = source.CaptureMatchStateSnapshot();

            ThrowingGameState target =
                RegisterActor<ThrowingGameState>(testWorld, "TargetGameState");
            var expectedOutOfMemory = new OutOfMemoryException(
                "Synthetic direct restore observer exhaustion.");
            target.MatchStateChangedException = expectedOutOfMemory;

            OutOfMemoryException actualOutOfMemory = Assert.Throws<OutOfMemoryException>(() =>
                target.TryRestoreMatchStateSnapshot(in snapshot, out _));
            Assert.AreSame(expectedOutOfMemory, actualOutOfMemory);
            Assert.AreEqual(MatchState.WaitingToStart, target.MatchState);

            target.MatchStateChangedException = null;
            Assert.IsTrue(target.TrySetMatchState(MatchState.InProgress, out string error), error);
            Assert.AreEqual(MatchState.InProgress, target.MatchState);
        }

        [Test]
        public void RestoreObserverNestedOutOfMemory_PropagatesSameInstanceAndClearsGuard()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            GameState source = RegisterActor<GameState>(testWorld, "SourceGameState");
            Assert.IsTrue(source.TrySetMatchState(MatchState.WaitingToStart, out _));
            MatchStateSnapshot snapshot = source.CaptureMatchStateSnapshot();

            ThrowingGameState target =
                RegisterActor<ThrowingGameState>(testWorld, "TargetGameState");
            var expectedOutOfMemory = new OutOfMemoryException(
                "Synthetic nested restore observer exhaustion.");
            target.MatchStateChangedException = new AggregateException(
                new InvalidOperationException("Synthetic non-terminal restore observer failure."),
                new AggregateException(expectedOutOfMemory));

            OutOfMemoryException actualOutOfMemory = Assert.Throws<OutOfMemoryException>(() =>
                target.TryRestoreMatchStateSnapshot(in snapshot, out _));

            Assert.AreSame(expectedOutOfMemory, actualOutOfMemory);
            Assert.AreEqual(MatchState.WaitingToStart, target.MatchState);

            target.MatchStateChangedException = null;
            Assert.IsTrue(target.TrySetMatchState(MatchState.InProgress, out string error), error);
            Assert.AreEqual(MatchState.InProgress, target.MatchState);
        }

        [Test]
        public void RestoreObserverOrdinaryException_IsolatedAfterCommittedRestore()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: new ManualMatchClock());
            GameState source = RegisterActor<GameState>(testWorld, "SourceGameState");
            Assert.IsTrue(source.TrySetMatchState(MatchState.WaitingToStart, out _));
            MatchStateSnapshot snapshot = source.CaptureMatchStateSnapshot();

            ThrowingGameState target =
                RegisterActor<ThrowingGameState>(testWorld, "TargetGameState");
            target.MatchStateChangedException =
                new InvalidOperationException("Synthetic ordinary restore observer failure.");
            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));

            try
            {
                Assert.IsTrue(
                    target.TryRestoreMatchStateSnapshot(in snapshot, out string error),
                    error);
                Assert.AreEqual(MatchState.WaitingToStart, target.MatchState);

                target.MatchStateChangedException = null;
                Assert.IsTrue(target.TrySetMatchState(MatchState.InProgress, out error), error);
                Assert.AreEqual(MatchState.InProgress, target.MatchState);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }
        }

        [Test]
        public void MatchState_UnregisteredFacadeRejectsRuntimeAccess()
        {
            GameState gameState = CreateActor<GameState>("GameState");

            Assert.Throws<InvalidOperationException>(() => _ = gameState.ElapsedTimeSeconds);
            Assert.Throws<InvalidOperationException>(() =>
                gameState.TrySetMatchState(MatchState.WaitingToStart, out _));
            Assert.Throws<InvalidOperationException>(() => _ = gameState.PlayerArray);
        }

        [Test]
        public void ElapsedTime_WorkerFirstReadRejectsBeforeClockAccessOrStateOwnership()
        {
            var clock = new ManualMatchClock { Seconds = 3d };
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                matchClock: clock);
            GameState gameState = RegisterActor<GameState>(testWorld, "GameState");
            int clockReadsBeforeWorker = clock.TimestampReadCount;
            Exception workerException = null;

            var worker = new System.Threading.Thread(() =>
            {
                try
                {
                    _ = gameState.ElapsedTimeSeconds;
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");
            Assert.IsInstanceOf<InvalidOperationException>(workerException);
            Assert.AreEqual(clockReadsBeforeWorker, clock.TimestampReadCount);
            Assert.AreEqual(0d, gameState.ElapsedTimeSeconds);
        }

        [Test]
        public void PlayerArray_CachedViewAndEnumeratorRejectWorkerThreadAccess()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client);
            GameState gameState = RegisterActor<GameState>(testWorld, "GameState");
            OwnerThreadReadOnlyList<PlayerState> view = gameState.PlayerArray;
            OwnerThreadReadOnlyList<PlayerState>.Enumerator enumerator = view.GetEnumerator();
            Exception countException = null;
            Exception enumeratorException = null;

            var worker = new System.Threading.Thread(() =>
            {
                try
                {
                    _ = view.Count;
                }
                catch (Exception exception)
                {
                    countException = exception;
                }

                try
                {
                    enumerator.MoveNext();
                }
                catch (Exception exception)
                {
                    enumeratorException = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");
            Assert.IsInstanceOf<InvalidOperationException>(countException);
            Assert.IsInstanceOf<InvalidOperationException>(enumeratorException);
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
            T actor = gameObject.AddComponent<T>();
            UnityLifecycleTestUtility.InvokeAwake(actor);
            return actor;
        }

        private static T RegisterActor<T>(GameplayTestWorld testWorld, string name) where T : Actor
        {
            T actor = testWorld.CreateAuthoringActor<T>(name);
            testWorld.World.RegisterActor(actor);
            return actor;
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

        private sealed class ThrowingGameState : GameState
        {
            public Exception MatchStateChangedException { get; set; }

            protected override void OnMatchStateChanged(MatchState oldState, MatchState newState)
            {
                if (MatchStateChangedException != null)
                {
                    throw MatchStateChangedException;
                }
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
            public int TimestampReadCount { get; private set; }

            public MatchTimestamp CurrentTimestamp
            {
                get
                {
                    TimestampReadCount++;
                    return new MatchTimestamp(clockEpoch, Seconds);
                }
            }
        }
    }
}
