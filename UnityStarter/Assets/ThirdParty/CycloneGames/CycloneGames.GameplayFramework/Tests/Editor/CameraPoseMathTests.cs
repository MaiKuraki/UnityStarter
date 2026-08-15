using System;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class CameraPoseMathTests
    {
        [Test]
        public void CameraPose_RejectsNonFiniteAndDegenerateValues()
        {
            Assert.Throws<ArgumentException>(() =>
                new CameraPose(
                    new Vector3(float.NaN, 0f, 0f),
                    Quaternion.identity,
                    60f));
            Assert.Throws<ArgumentException>(() =>
                new CameraPose(Vector3.zero, default, 60f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CameraPose(Vector3.zero, Quaternion.identity, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CameraPose(Vector3.zero, Quaternion.identity, 180f));

            Assert.IsFalse(CameraPose.TryCreate(
                Vector3.zero,
                default,
                60f,
                out CameraPose invalidPose));
            Assert.IsFalse(invalidPose.IsValid);
        }

        [Test]
        public void CameraPose_NormalizesValidQuaternion()
        {
            Assert.IsTrue(CameraPose.TryCreate(
                new Vector3(1f, 2f, 3f),
                new Quaternion(0f, 2f, 0f, 2f),
                75f,
                out CameraPose pose));

            Assert.IsTrue(pose.IsValid);
            Assert.AreEqual(1f, Quaternion.Dot(pose.Rotation, pose.Rotation), 0.0001f);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), pose.Position);
            Assert.AreEqual(75f, pose.Fov);
        }

        [Test]
        public void CameraManager_InvalidExtensionPosesRetainLastKnownGoodPose()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(localPlayerCount: 1);
            CameraManager manager = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<CameraManager>("CameraManagerPrefab"));
            PlayerController controller = testWorld.World.PlayerControllers[0];
            manager.InitializeFor(controller);
            manager.UpdateCamera(1f / 60f);
            CameraPose baseline = manager.CurrentPose;

            InvalidPoseActor invalidTarget = testWorld.World.SpawnActor(
                testWorld.CreateAuthoringActor<InvalidPoseActor>("InvalidPoseActorPrefab"));
            controller.SetViewTarget(invalidTarget);
            manager.UpdateCamera(1f / 60f);
            AssertPoseEqual(baseline, manager.CurrentPose);

            controller.SetViewTarget(controller.GetPawn());
            var invalidMode = new InvalidPoseCameraMode();
            Assert.IsTrue(controller.TryPushCameraMode(invalidMode));
            manager.UpdateCamera(1f / 60f);
            AssertPoseEqual(baseline, manager.CurrentPose);
            Assert.IsTrue(controller.RemoveCameraMode(invalidMode));

            var invalidProcessor = new InvalidPosePostProcessor();
            manager.RegisterPostProcessor(invalidProcessor);
            manager.UpdateCamera(1f / 60f);
            AssertPoseEqual(baseline, manager.CurrentPose);
            Assert.AreEqual(baseline.Position, manager.transform.position);
            Assert.AreEqual(baseline.Rotation, manager.transform.rotation);
        }

        [Test]
        public void ExponentialDecayT_ClampsNegativeInputs()
        {
            Assert.AreEqual(0f, CameraPoseMath.ExponentialDecayT(-1f, 0.25f), 0.0001f);
            Assert.AreEqual(0f, CameraPoseMath.ExponentialDecayT(5f, -0.25f), 0.0001f);
        }

        [Test]
        public void ExponentialDecayT_ReturnsExpectedDecayFactor()
        {
            float result = CameraPoseMath.ExponentialDecayT(2f, 0.25f);
            float expected = 1f - math.exp(-0.5f);

            Assert.AreEqual(expected, result, 0.0001f);
        }

        [Test]
        public void LookRotationSafe_ReturnsFallback_ForNearZeroDirection()
        {
            quaternion fallback = quaternion.EulerXYZ(new float3(0.1f, 0.2f, 0.3f));

            quaternion result = CameraPoseMath.LookRotationSafe(float3.zero, fallback);

            Assert.AreEqual(fallback.value.x, result.value.x, 0.0001f);
            Assert.AreEqual(fallback.value.y, result.value.y, 0.0001f);
            Assert.AreEqual(fallback.value.z, result.value.z, 0.0001f);
            Assert.AreEqual(fallback.value.w, result.value.w, 0.0001f);
        }

        [Test]
        public void IsInsideAngularDeadZone_HandlesForwardBoundaryBehindAndZeroDirection()
        {
            quaternion referenceRotation = quaternion.identity;

            Assert.IsTrue(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(0f, 0f, 1f), 10f, 10f));
            Assert.IsTrue(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(1f, 0f, 10f), 10f, 10f));
            Assert.IsFalse(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(2f, 0f, 1f), 10f, 10f));
            Assert.IsFalse(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(0f, 0f, -1f), 10f, 10f));
            Assert.IsTrue(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, float3.zero, 10f, 10f));
        }

        private static void AssertPoseEqual(in CameraPose expected, in CameraPose actual)
        {
            Assert.AreEqual(expected.Position, actual.Position);
            Assert.AreEqual(expected.Rotation, actual.Rotation);
            Assert.AreEqual(expected.Fov, actual.Fov);
        }

        private sealed class InvalidPoseActor : Actor
        {
            public override void CalcCamera(
                float deltaTime,
                out CameraPose outResult,
                float fallbackFov)
            {
                outResult = default;
            }
        }

        private sealed class InvalidPoseCameraMode : CameraMode
        {
            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return default;
            }
        }

        private sealed class InvalidPosePostProcessor : ICameraPostProcessor
        {
            public CameraPose Process(
                CameraPose desiredPose,
                CameraContext context,
                float deltaTime)
            {
                return default;
            }
        }
    }
}
