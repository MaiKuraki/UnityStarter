using System.Collections.Generic;
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
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
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

            Assert.IsFalse(gameState.TrySetMatchState(GameState.EMatchState.InProgress, out _));
            Assert.IsTrue(gameState.TrySetMatchState(GameState.EMatchState.WaitingToStart, out _));
            Assert.IsTrue(gameState.TrySetMatchState(GameState.EMatchState.InProgress, out _));
            Assert.IsTrue(gameState.TrySetMatchState(GameState.EMatchState.WaitingPostMatch, out _));
            Assert.AreEqual(3, gameState.MatchStateChangeCount);
            Assert.AreEqual(GameState.EMatchState.WaitingPostMatch, gameState.MatchState);
        }

        [Test]
        public void MatchState_RejectsReentrantTransitionFromObserver()
        {
            ReentrantGameState gameState = CreateActor<ReentrantGameState>("ReentrantGameState");

            Assert.IsTrue(gameState.TrySetMatchState(GameState.EMatchState.WaitingToStart, out _));

            Assert.IsFalse(gameState.ReentrantTransitionResult);
            Assert.AreEqual(GameState.EMatchState.WaitingToStart, gameState.MatchState);
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

            protected override void OnMatchStateChanged(EMatchState oldState, EMatchState newState)
            {
                MatchStateChangeCount++;
            }
        }

        private sealed class ReentrantGameState : GameState
        {
            public bool ReentrantTransitionResult { get; private set; }

            protected override void OnMatchStateChanged(EMatchState oldState, EMatchState newState)
            {
                ReentrantTransitionResult = TrySetMatchState(EMatchState.Aborted, out _);
            }
        }
    }
}
