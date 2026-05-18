using System.Collections.Generic;
using System.Reflection;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class GameStateAndPlayerStartTests
    {
        private readonly List<GameObject> objects = new List<GameObject>(8);

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
        }

        [Test]
        public void GameState_AddPlayerState_IgnoresNullAndDuplicates()
        {
            GameState gameState = CreateActor<GameState>("GameState");
            PlayerState playerState = CreateActor<PlayerState>("PlayerState");

            gameState.AddPlayerState(null);
            gameState.AddPlayerState(playerState);
            gameState.AddPlayerState(playerState);

            Assert.AreEqual(1, gameState.GetNumPlayers());
            Assert.AreSame(playerState, gameState.PlayerArray[0]);
        }

        [Test]
        public void GameState_SetMatchState_FiresOnlyWhenStateChanges()
        {
            TestGameState gameState = CreateActor<TestGameState>("GameState");

            gameState.SetMatchState(GameState.EMatchState.EnteringMap);
            gameState.SetMatchState(GameState.EMatchState.InProgress);
            gameState.SetMatchState(GameState.EMatchState.InProgress);
            gameState.SetMatchState(GameState.EMatchState.WaitingPostMatch);

            Assert.AreEqual(2, gameState.MatchStateChangeCount);
            Assert.AreEqual(GameState.EMatchState.WaitingPostMatch, gameState.MatchState);
            Assert.AreEqual(GameState.EMatchState.InProgress, gameState.LastOldState);
            Assert.AreEqual(GameState.EMatchState.WaitingPostMatch, gameState.LastNewState);
        }

        [Test]
        public void PlayerStart_RegistersOnEnableAndUnregistersOnDisable()
        {
            PlayerStart.ClearRegistryDirtyFlag();
            GameObject gameObject = CreateObject("PlayerStart");
            PlayerStart playerStart = gameObject.AddComponent<PlayerStart>();
            InvokeLifecycle(playerStart, "OnEnable");

            AssertContains(playerStart, true);
            Assert.IsTrue(PlayerStart.IsRegistryDirty);

            PlayerStart.ClearRegistryDirtyFlag();
            InvokeLifecycle(playerStart, "OnDisable");

            AssertContains(playerStart, false);
            Assert.IsTrue(PlayerStart.IsRegistryDirty);
        }

        private T CreateActor<T>(string name) where T : Actor
        {
            return CreateObject(name).AddComponent<T>();
        }

        private GameObject CreateObject(string name)
        {
            GameObject gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }

        private static void AssertContains(PlayerStart playerStart, bool expected)
        {
            IReadOnlyList<PlayerStart> starts = PlayerStart.GetAllPlayerStarts();
            bool found = false;
            for (int i = 0; i < starts.Count; i++)
            {
                if (ReferenceEquals(starts[i], playerStart))
                {
                    found = true;
                    break;
                }
            }

            Assert.AreEqual(expected, found);
        }

        private static void InvokeLifecycle(PlayerStart playerStart, string methodName)
        {
            MethodInfo method = typeof(PlayerStart).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(playerStart, null);
        }

        private sealed class TestGameState : GameState
        {
            public int MatchStateChangeCount { get; private set; }
            public EMatchState LastOldState { get; private set; }
            public EMatchState LastNewState { get; private set; }

            protected override void OnMatchStateChanged(EMatchState OldState, EMatchState NewState)
            {
                MatchStateChangeCount++;
                LastOldState = OldState;
                LastNewState = NewState;
            }
        }
    }
}
