using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Integrations.Cinemachine.Tests.Editor
{
    public sealed class CinemachineCameraOutputTests
    {
        [Test]
        public void ActivateApplyDeactivate_RestoresOwnedCinemachineState()
        {
            var managerObject = new GameObject("CameraManager");
            var brainObject = new GameObject("CinemachineBrain");
            try
            {
                CameraManager manager = managerObject.AddComponent<CameraManager>();
                CinemachineCamera virtualCamera = managerObject.AddComponent<CinemachineCamera>();
                CinemachineCameraOutput output = managerObject.AddComponent<CinemachineCameraOutput>();
                brainObject.AddComponent<Camera>();
                CinemachineBrain brain = brainObject.AddComponent<CinemachineBrain>();
                CinemachineBrain.UpdateMethods initialUpdateMethod = brain.UpdateMethod;
                var follow = new GameObject("Follow").transform;
                follow.SetParent(managerObject.transform, worldPositionStays: false);
                var lookAt = new GameObject("LookAt").transform;
                lookAt.SetParent(managerObject.transform, worldPositionStays: false);
                virtualCamera.Follow = follow;
                virtualCamera.LookAt = lookAt;
                output.SetVirtualCamera(virtualCamera);
                output.SetBrain(brain);

                Assert.IsTrue(output.TryPrepare(out string prepareError), prepareError);
                Assert.AreEqual(2, output.PreparedResourceCount);
                Assert.AreSame(brain, output.GetPreparedResource(0));
                Assert.AreSame(virtualCamera, output.GetPreparedResource(1));
                Assert.IsTrue(output.TryActivate(manager, out string activationError), activationError);
                Assert.AreEqual(CinemachineBrain.UpdateMethods.ManualUpdate, brain.UpdateMethod);
                Assert.IsNull(virtualCamera.Follow);
                Assert.IsNull(virtualCamera.LookAt);

                var pose = new CameraPose(
                    new Vector3(3f, 4f, 5f),
                    Quaternion.Euler(10f, 20f, 0f),
                    72f);
                output.ApplyPose(in pose);

                Assert.AreEqual(pose.Position, virtualCamera.transform.position);
                Assert.AreEqual(pose.Fov, virtualCamera.Lens.FieldOfView, 0.0001f);
                output.Deactivate(manager);
                Assert.AreEqual(initialUpdateMethod, brain.UpdateMethod);
                Assert.AreSame(follow, virtualCamera.Follow);
                Assert.AreSame(lookAt, virtualCamera.LookAt);
                Assert.IsFalse(output.IsActive);
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
                Object.DestroyImmediate(brainObject);
            }
        }

        [Test]
        public void CompositeLease_RejectsSameVirtualCameraWithDifferentBrains()
        {
            using var world = new CinemachineTestWorld();
            var resources = new List<GameObject>(3);
            try
            {
                CinemachineCamera sharedVirtualCamera = CreateVirtualCamera(resources, "SharedVirtualCamera");
                CinemachineBrain firstBrain = CreateBrain(resources, "FirstBrain");
                CinemachineBrain secondBrain = CreateBrain(resources, "SecondBrain");
                CameraManager first = CreateManager(world, "First", sharedVirtualCamera, firstBrain);
                CameraManager conflicting = CreateManager(world, "Conflicting", sharedVirtualCamera, secondBrain);

                Assert.IsNotNull(first.ActiveOutput);
                Assert.IsNull(conflicting.ActiveOutput);
                Assert.IsTrue(world.World.DestroyActor(first));
                Assert.IsTrue(conflicting.TryResolveAndBindOutput());
                Assert.IsNotNull(conflicting.ActiveOutput);
            }
            finally
            {
                DestroyResources(resources);
            }
        }

        [Test]
        public void CompositeLease_RejectsSameBrainWithDifferentVirtualCameras()
        {
            using var world = new CinemachineTestWorld();
            var resources = new List<GameObject>(3);
            try
            {
                CinemachineBrain sharedBrain = CreateBrain(resources, "SharedBrain");
                CinemachineCamera firstVirtualCamera = CreateVirtualCamera(resources, "FirstVirtualCamera");
                CinemachineCamera secondVirtualCamera = CreateVirtualCamera(resources, "SecondVirtualCamera");
                CameraManager first = CreateManager(world, "First", firstVirtualCamera, sharedBrain);
                CameraManager conflicting = CreateManager(world, "Conflicting", secondVirtualCamera, sharedBrain);

                Assert.IsNotNull(first.ActiveOutput);
                Assert.IsNull(conflicting.ActiveOutput);
                Assert.IsTrue(world.World.DestroyActor(first));
                Assert.IsTrue(conflicting.TryResolveAndBindOutput());
                Assert.IsNotNull(conflicting.ActiveOutput);
            }
            finally
            {
                DestroyResources(resources);
            }
        }

        private static CameraManager CreateManager(
            CinemachineTestWorld world,
            string name,
            CinemachineCamera virtualCamera,
            CinemachineBrain brain)
        {
            CameraManager prefab = world.CreateAuthoringActor<CameraManager>(name + "Prefab");
            CameraManager manager = world.World.SpawnActor(prefab);
            CinemachineCameraOutput output = manager.gameObject.AddComponent<CinemachineCameraOutput>();
            output.SetVirtualCamera(virtualCamera);
            output.SetBrain(brain);
            manager.SetCameraOutput(output, rebindImmediately: false);
            manager.InitializeFor(world.World.PlayerControllers[0]);
            return manager;
        }

        private static CinemachineCamera CreateVirtualCamera(List<GameObject> resources, string name)
        {
            var gameObject = new GameObject(name);
            resources.Add(gameObject);
            return gameObject.AddComponent<CinemachineCamera>();
        }

        private static CinemachineBrain CreateBrain(List<GameObject> resources, string name)
        {
            var gameObject = new GameObject(name);
            resources.Add(gameObject);
            gameObject.AddComponent<Camera>();
            return gameObject.AddComponent<CinemachineBrain>();
        }

        private static void DestroyResources(List<GameObject> resources)
        {
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                if (resources[i] != null)
                {
                    Object.DestroyImmediate(resources[i]);
                }
            }
        }

        private sealed class CinemachineTestWorld : IDisposable
        {
            private readonly List<GameObject> authoringObjects = new List<GameObject>(8);
            private readonly WorldSettings settings;
            private readonly GameInstance gameInstance;

            public CinemachineTestWorld()
            {
                settings = ScriptableObject.CreateInstance<WorldSettings>();
                SetReference("gameModeClass", CreateAuthoringActor<GameMode>("GameModePrefab"));
                SetReference("playerControllerClass", CreateAuthoringActor<PlayerController>("PlayerControllerPrefab"));
                SetReference("pawnClass", CreateAuthoringActor<Pawn>("PawnPrefab"));
                SetReference("playerStateClass", CreateAuthoringActor<PlayerState>("PlayerStatePrefab"));
                gameInstance = new GameInstance(new UnityActorLifetime(), localPlayerCount: 1);
                World = gameInstance.StartWorldAsync(settings).GetAwaiter().GetResult();
            }

            public World World { get; }

            public T CreateAuthoringActor<T>(string name) where T : Actor
            {
                var gameObject = new GameObject(name);
                authoringObjects.Add(gameObject);
                return gameObject.AddComponent<T>();
            }

            public void Dispose()
            {
                gameInstance?.Dispose();
                if (settings != null)
                {
                    Object.DestroyImmediate(settings);
                }

                for (int i = authoringObjects.Count - 1; i >= 0; i--)
                {
                    if (authoringObjects[i] != null)
                    {
                        Object.DestroyImmediate(authoringObjects[i]);
                    }
                }
            }

            private void SetReference(string fieldName, Object value)
            {
                var serializedSettings = new SerializedObject(settings);
                SerializedProperty property = serializedSettings.FindProperty(fieldName);
                if (property == null)
                {
                    throw new InvalidOperationException($"WorldSettings field '{fieldName}' was not found.");
                }

                property.objectReferenceValue = value;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
