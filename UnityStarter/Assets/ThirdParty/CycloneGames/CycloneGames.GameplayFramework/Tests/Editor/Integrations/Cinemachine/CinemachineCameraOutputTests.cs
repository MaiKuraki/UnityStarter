using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;

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

                Assert.IsTrue(output.TryPrepare(out Object resource, out string prepareError), prepareError);
                Assert.AreSame(brain, resource);
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
    }
}
