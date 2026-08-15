using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            Action<GameplayTestWorld> configure = null,
            IActorLifetime actorLifetime = null,
            WorldRuntimeLimits runtimeLimits = null,
            IWorldActorSource actorSource = null,
            bool discoverActiveSceneActors = true,
            IMatchClock matchClock = null,
            ICameraOutputLeaseArbiter cameraOutputLeaseArbiter = null,
            ISceneTransitionHandler sceneTransitionHandler = null)
        {
            GameplayTestWorld testWorld = Create(
                localPlayerCount,
                configure,
                actorLifetime,
                runtimeLimits,
                actorSource,
                discoverActiveSceneActors,
                matchClock,
                cameraOutputLeaseArbiter,
                sceneTransitionHandler);
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
            Action<GameplayTestWorld> configure = null,
            IActorLifetime actorLifetime = null,
            WorldRuntimeLimits runtimeLimits = null,
            IWorldActorSource actorSource = null,
            bool discoverActiveSceneActors = true,
            IMatchClock matchClock = null,
            ICameraOutputLeaseArbiter cameraOutputLeaseArbiter = null,
            ISceneTransitionHandler sceneTransitionHandler = null)
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
            IWorldActorSource effectiveActorSource = actorSource;
            if (effectiveActorSource == null && discoverActiveSceneActors)
            {
                effectiveActorSource = new SceneWorldActorSource(SceneManager.GetActiveScene());
            }

            testWorld.Instance = new GameInstance(
                actorLifetime ?? new UnityActorLifetime(),
                localPlayerCount,
                sceneTransitionHandler: sceneTransitionHandler,
                runtimeLimits: runtimeLimits,
                actorSource: effectiveActorSource,
                matchClock: matchClock,
                cameraOutputLeaseArbiter: cameraOutputLeaseArbiter);
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
            T actor = gameObject.AddComponent<T>();
            UnityLifecycleTestUtility.InvokeAwake(actor);
            return actor;
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
            Exception disposalFailure = null;
            GameInstance ownedInstance = Instance;
            if (ownedInstance != null)
            {
                try
                {
                    ownedInstance.Dispose();
                }
                catch (Exception exception)
                {
                    disposalFailure = exception;
                    if (!ownedInstance.IsDisposalComplete)
                    {
                        try
                        {
                            ownedInstance.Dispose();
                        }
                        catch
                        {
                            // Preserve the first failure while giving retryable terminal cleanup
                            // one deterministic pass to release test-owned scene resources.
                        }
                    }
                }
            }

            Instance = null;
            World = null;

            if (Settings != null)
            {
                try
                {
                    Object.DestroyImmediate(Settings);
                }
                catch (Exception exception)
                {
                    disposalFailure ??= exception;
                }
                Settings = null;
            }

            for (int i = authoringObjects.Count - 1; i >= 0; i--)
            {
                if (authoringObjects[i] != null)
                {
                    try
                    {
                        Object.DestroyImmediate(authoringObjects[i]);
                    }
                    catch (Exception exception)
                    {
                        disposalFailure ??= exception;
                    }
                }
            }

            authoringObjects.Clear();

            if (ownedInstance != null && !ownedInstance.IsDisposalComplete)
            {
                disposalFailure ??= new InvalidOperationException(
                    "GameplayTestWorld disposal returned without completing terminal cleanup.");
            }

            if (disposalFailure != null)
            {
                ExceptionDispatchInfo.Capture(disposalFailure).Throw();
            }
        }
    }
}
