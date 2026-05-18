using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class CameraBlendStateTests
    {
        [Test]
        public void Start_ClampsNegativeDuration_ToInactiveState()
        {
            CameraBlendState state = default;
            CameraPose startPose = new CameraPose(Vector3.zero, Quaternion.identity, 60f);
            CameraPose targetPose = new CameraPose(new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), 90f);

            state.Start(startPose, -1f, CameraBlendCurveType.Linear);
            CameraPose result = state.Evaluate(targetPose, 0.5f);

            Assert.IsFalse(state.IsActive);
            Assert.AreEqual(0f, state.Duration);
            Assert.AreEqual(targetPose.Position, result.Position);
            Assert.AreEqual(targetPose.Fov, result.Fov);
        }

        [Test]
        public void Evaluate_AdvancesLinearBlend_AndCompletesAtDuration()
        {
            CameraBlendState state = default;
            CameraPose startPose = new CameraPose(Vector3.zero, Quaternion.identity, 60f);
            CameraPose targetPose = new CameraPose(new Vector3(10f, 0f, 0f), Quaternion.identity, 100f);

            state.Start(startPose, 2f, CameraBlendCurveType.Linear);
            CameraPose quarterPose = state.Evaluate(targetPose, 0.5f);
            float activeDuration = state.Duration;
            float activeRemaining = state.Remaining;
            CameraPose finalPose = state.Evaluate(targetPose, 1.5f);

            Assert.AreEqual(2f, activeDuration);
            Assert.AreEqual(1.5f, activeRemaining);
            Assert.AreEqual(0f, state.Duration);
            Assert.AreEqual(0f, state.Remaining);
            Assert.IsFalse(state.IsActive);
            Assert.AreEqual(2.5f, quarterPose.Position.x, 0.0001f);
            Assert.AreEqual(70f, quarterPose.Fov, 0.0001f);
            Assert.AreEqual(targetPose.Position, finalPose.Position);
            Assert.AreEqual(targetPose.Fov, finalPose.Fov);
        }

        [Test]
        public void Evaluate_UsesCustomCurve_WhenCustomCurveIsAssigned()
        {
            CameraBlendState state = default;
            CameraPose startPose = new CameraPose(Vector3.zero, Quaternion.identity, 60f);
            CameraPose targetPose = new CameraPose(new Vector3(10f, 0f, 0f), Quaternion.identity, 80f);

            state.Start(startPose, 1f, new FixedCameraBlendCurve(0.5f));
            CameraPose result = state.Evaluate(targetPose, 0.1f);

            Assert.IsTrue(state.HasCustomCurve);
            Assert.AreEqual(CameraBlendCurveType.Custom, state.CurveType);
            Assert.AreEqual(5f, result.Position.x, 0.0001f);
            Assert.AreEqual(70f, result.Fov, 0.0001f);
        }

        private sealed class FixedCameraBlendCurve : ICameraBlendCurve
        {
            private readonly float value;

            public FixedCameraBlendCurve(float value)
            {
                this.value = value;
            }

            public float Evaluate(float t) => value;
        }
    }
}
