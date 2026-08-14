using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Integrations.Cinemachine.Tests.Editor
{
    public sealed class CinemachineCameraOutputTests
    {
        [Test]
        public void Authoring_DefaultsToExplicitReferencesWithoutSceneDiscovery()
        {
            var outputObject = new GameObject("CinemachineOutput");
            try
            {
                CinemachineCameraOutput output = outputObject.AddComponent<CinemachineCameraOutput>();
                InvokeAwake(output);
                var serializedOutput = new SerializedObject(output);

                Assert.IsFalse(
                    serializedOutput.FindProperty("allowSceneDiscovery").boolValue);
            }
            finally
            {
                Object.DestroyImmediate(outputObject);
            }
        }

        [Test]
        public void Authoring_ExplicitSceneDiscoveryResolvesUniqueSameSceneResources()
        {
            RunInTemporaryMainStageScene("CinemachineDiscovery", scene =>
            {
                var outputObject = new GameObject("CinemachineOutput");
                var cameraObject = new GameObject("CinemachineCamera");
                var brainObject = new GameObject("CinemachineBrain");
                SceneManager.MoveGameObjectToScene(outputObject, scene);
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                SceneManager.MoveGameObjectToScene(brainObject, scene);
                CinemachineCameraOutput output =
                    outputObject.AddComponent<CinemachineCameraOutput>();
                InvokeAwake(output);
                CinemachineCamera virtualCamera =
                    cameraObject.AddComponent<CinemachineCamera>();
                brainObject.AddComponent<Camera>();
                CinemachineBrain brain = brainObject.AddComponent<CinemachineBrain>();
                var serializedOutput = new SerializedObject(output);
                serializedOutput.FindProperty("allowSceneDiscovery").boolValue = true;
                serializedOutput.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(output.TryGetResourceSet(
                    out CameraOutputResourceSet resources,
                    out string error), error);
                Assert.AreSame(brain, resources.GetResource(0));
                Assert.AreSame(virtualCamera, resources.GetResource(1));
                Assert.IsNull(output.ActiveBrain);
                Assert.IsNull(output.ActiveVirtualCamera);
            });
        }

        [Test]
        public void Authoring_SceneDiscoveryRejectsMultipleSameSceneBrains()
        {
            RunInTemporaryMainStageScene("CinemachineAmbiguity", scene =>
            {
                var outputObject = new GameObject("CinemachineOutput");
                var firstBrainObject = new GameObject("FirstBrain");
                var secondBrainObject = new GameObject("SecondBrain");
                SceneManager.MoveGameObjectToScene(outputObject, scene);
                SceneManager.MoveGameObjectToScene(firstBrainObject, scene);
                SceneManager.MoveGameObjectToScene(secondBrainObject, scene);
                CinemachineCameraOutput output =
                    outputObject.AddComponent<CinemachineCameraOutput>();
                InvokeAwake(output);
                outputObject.AddComponent<CinemachineCamera>();
                firstBrainObject.AddComponent<Camera>();
                firstBrainObject.AddComponent<CinemachineBrain>();
                secondBrainObject.SetActive(false);
                secondBrainObject.AddComponent<Camera>();
                CinemachineBrain secondBrain =
                    secondBrainObject.AddComponent<CinemachineBrain>();
                secondBrain.enabled = false;
                secondBrainObject.SetActive(true);
                var serializedOutput = new SerializedObject(output);
                serializedOutput.FindProperty("allowSceneDiscovery").boolValue = true;
                serializedOutput.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsFalse(output.TryGetResourceSet(out _, out string error));
                StringAssert.Contains("Multiple CinemachineBrain", error);
            });
        }

        [Test]
        public void ActivateApplyDeactivate_RestoresOwnedCinemachineState()
        {
            var managerObject = new GameObject("CameraManager");
            var brainObject = new GameObject("CinemachineBrain");
            try
            {
                CameraManager manager = managerObject.AddComponent<CameraManager>();
                InvokeAwake(manager);
                CinemachineCamera virtualCamera = managerObject.AddComponent<CinemachineCamera>();
                CinemachineCameraOutput output = managerObject.AddComponent<CinemachineCameraOutput>();
                InvokeAwake(output);
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

                Assert.IsTrue(output.TryGetResourceSet(
                    out CameraOutputResourceSet resources,
                    out string discoveryError), discoveryError);
                Assert.AreEqual(2, resources.Count);
                Assert.AreSame(brain, resources.GetResource(0));
                Assert.AreSame(virtualCamera, resources.GetResource(1));
                Assert.IsTrue(output.TryActivate(
                    manager,
                    in resources,
                    out string activationError), activationError);
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
            InvokeAwake(output);
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

        private static void RunInTemporaryMainStageScene(
            string testName,
            Action<Scene> testBody)
        {
            string folderName =
                $"__CycloneGamesGameplayFrameworkTests_{testName}_{Guid.NewGuid():N}";
            string folderPath = $"Assets/{folderName}";
            string baseScenePath = $"{folderPath}/BaseScene.unity";
            Scene baseScene = default;
            Scene testScene = default;
            ExceptionDispatchInfo firstFailure = null;

            try
            {
                string folderGuid = AssetDatabase.CreateFolder("Assets", folderName);
                if (string.IsNullOrEmpty(folderGuid))
                {
                    throw new InvalidOperationException(
                        $"Failed to create temporary test folder '{folderPath}'.");
                }

                baseScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(baseScene, baseScenePath))
                {
                    throw new InvalidOperationException(
                        $"Failed to save temporary base Scene '{baseScenePath}'.");
                }

                testScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
                testBody(testScene);
            }
            catch (Exception exception)
            {
                firstFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                CaptureCleanupFailure(ref firstFailure, () => CloseScene(testScene));
                CaptureCleanupFailure(ref firstFailure, () => ReplaceBaseScene(baseScene));
                CaptureCleanupFailure(ref firstFailure, () => DeleteAsset(baseScenePath));
                CaptureCleanupFailure(ref firstFailure, () => DeleteAsset(folderPath));
            }

            firstFailure?.Throw();
        }

        private static void CloseScene(Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded &&
                !EditorSceneManager.CloseScene(scene, removeScene: true))
            {
                throw new InvalidOperationException(
                    $"Failed to close temporary test Scene '{scene.name}'.");
            }
        }

        private static void ReplaceBaseScene(Scene baseScene)
        {
            if (!baseScene.IsValid() || !baseScene.isLoaded)
            {
                return;
            }

            Scene replacement = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            if (!replacement.IsValid() || !replacement.isLoaded)
            {
                throw new InvalidOperationException(
                    "Failed to replace the temporary saved base Scene.");
            }
        }

        private static void DeleteAsset(string assetPath)
        {
            if ((AssetDatabase.IsValidFolder(assetPath) ||
                 AssetDatabase.LoadMainAssetAtPath(assetPath) != null) &&
                !AssetDatabase.DeleteAsset(assetPath))
            {
                throw new InvalidOperationException(
                    $"Failed to delete temporary test asset '{assetPath}'.");
            }
        }

        private static void CaptureCleanupFailure(
            ref ExceptionDispatchInfo firstFailure,
            Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private static void InvokeAwake(MonoBehaviour behaviour)
        {
            MethodInfo awake = behaviour.GetType().GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (awake == null)
            {
                throw new InvalidOperationException(
                    $"Type '{behaviour.GetType().FullName}' does not declare an Awake lifecycle method.");
            }

            try
            {
                awake.Invoke(behaviour, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
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
                T actor = gameObject.AddComponent<T>();
                InvokeAwake(actor);
                return actor;
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
