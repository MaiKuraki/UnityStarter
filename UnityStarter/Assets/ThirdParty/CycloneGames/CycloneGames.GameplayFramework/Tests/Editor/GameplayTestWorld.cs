using System;
using System.Collections.Generic;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    internal sealed class GameplayTestWorld : IDisposable
    {
        private readonly List<GameObject> authoringObjects = new List<GameObject>(8);

        private GameplayTestWorld() { }

        public GameInstance Instance { get; private set; }
        public World World { get; private set; }
        public WorldSettings Settings { get; private set; }

        public static GameplayTestWorld Start(
            int localPlayerCount = 0,
            IGameSession session = null,
            WorldNetMode netMode = WorldNetMode.Standalone,
            Action<GameplayTestWorld> configure = null)
        {
            GameplayTestWorld testWorld = Create(localPlayerCount, configure);
            try
            {
                testWorld.StartWorld(netMode, session);
                return testWorld;
            }
            catch
            {
                testWorld.Dispose();
                throw;
            }
        }

        public static GameplayTestWorld Create(
            int localPlayerCount = 0,
            Action<GameplayTestWorld> configure = null)
        {
            var testWorld = new GameplayTestWorld
            {
                Settings = ScriptableObject.CreateInstance<WorldSettings>(),
            };

            testWorld.SetReference("gameModeClass", testWorld.CreateAuthoringActor<GameMode>("GameModePrefab"));
            testWorld.SetReference("playerControllerClass", testWorld.CreateAuthoringActor<PlayerController>("PlayerControllerPrefab"));
            testWorld.SetReference("pawnClass", testWorld.CreateAuthoringActor<Pawn>("PawnPrefab"));
            testWorld.SetReference("playerStateClass", testWorld.CreateAuthoringActor<PlayerState>("PlayerStatePrefab"));
            configure?.Invoke(testWorld);
            testWorld.Instance = new GameInstance(new DefaultUnityObjectSpawner(), localPlayerCount);
            return testWorld;
        }

        public World StartWorld(
            WorldNetMode netMode = WorldNetMode.Standalone,
            IGameSession session = null)
        {
            World = Instance
                .StartWorldAsync(Settings, netMode, session)
                .GetAwaiter()
                .GetResult();
            return World;
        }

        public T CreateAuthoringActor<T>(string name) where T : Actor
        {
            var gameObject = new GameObject(name);
            authoringObjects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        public void SetReference(string fieldName, Object value)
        {
            var serializedSettings = new SerializedObject(Settings);
            SerializedProperty property = serializedSettings.FindProperty(fieldName);
            if (property == null)
            {
                throw new InvalidOperationException($"WorldSettings field '{fieldName}' was not found.");
            }

            property.objectReferenceValue = value;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        public void Dispose()
        {
            Instance?.Dispose();
            Instance = null;
            World = null;

            if (Settings != null)
            {
                Object.DestroyImmediate(Settings);
                Settings = null;
            }

            for (int i = authoringObjects.Count - 1; i >= 0; i--)
            {
                if (authoringObjects[i] != null)
                {
                    Object.DestroyImmediate(authoringObjects[i]);
                }
            }

            authoringObjects.Clear();
        }
    }
}
