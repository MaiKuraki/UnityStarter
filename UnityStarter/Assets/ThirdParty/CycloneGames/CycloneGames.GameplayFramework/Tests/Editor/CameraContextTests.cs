using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class CameraContextTests
    {
        private readonly List<GameObject> targetObjects = new List<GameObject>(4);
        private GameObject ownerObject;

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < targetObjects.Count; i++)
            {
                if (targetObjects[i] != null)
                {
                    Object.DestroyImmediate(targetObjects[i]);
                }
            }

            targetObjects.Clear();

            if (ownerObject != null)
            {
                Object.DestroyImmediate(ownerObject);
            }
        }

        [Test]
        public void TryPushCameraMode_RejectsNullAndCapacityOverflow()
        {
            CameraContext context = new CameraContext(null, 1);
            TestCameraMode first = new TestCameraMode();
            TestCameraMode second = new TestCameraMode();

            Assert.IsFalse(context.TryPushCameraMode(null));
            Assert.IsTrue(context.TryPushCameraMode(first));
            Assert.IsFalse(context.TryPushCameraMode(second));
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(first, context.GetPrimaryCameraMode());
            Assert.AreEqual(1, first.ActivateCount);
            Assert.AreEqual(0, second.ActivateCount);
        }

        [Test]
        public void TryPushOrReplaceOldest_DeactivatesOldestAndPreservesStackOrder()
        {
            CameraContext context = new CameraContext(null, 2);
            TestCameraMode first = new TestCameraMode();
            TestCameraMode second = new TestCameraMode();
            TestCameraMode third = new TestCameraMode();

            Assert.IsTrue(context.TryPushCameraMode(first));
            Assert.IsTrue(context.TryPushCameraMode(second));
            Assert.IsTrue(context.TryPushOrReplaceOldest(third));

            Assert.AreEqual(2, context.CameraModeCount);
            Assert.AreSame(second, context.GetCameraModeAt(0));
            Assert.AreSame(third, context.GetCameraModeAt(1));
            Assert.AreEqual(1, first.DeactivateCount);
            Assert.AreSame(third, context.GetPrimaryCameraMode());
        }

        [Test]
        public void RemoveCameraMode_DeactivatesAndCompactsStack()
        {
            CameraContext context = new CameraContext(null, 3);
            TestCameraMode first = new TestCameraMode();
            TestCameraMode second = new TestCameraMode();
            TestCameraMode third = new TestCameraMode();
            context.TryPushCameraMode(first);
            context.TryPushCameraMode(second);
            context.TryPushCameraMode(third);

            Assert.IsTrue(context.RemoveCameraMode(second));

            Assert.AreEqual(2, context.CameraModeCount);
            Assert.AreSame(first, context.GetCameraModeAt(0));
            Assert.AreSame(third, context.GetCameraModeAt(1));
            Assert.AreEqual(1, second.DeactivateCount);
        }

        [Test]
        public void ResolveViewTarget_UsesPolicyAndManualOverride()
        {
            PlayerController owner = CreateOwner();
            Actor target = CreateTarget("Suggested");
            Actor manualTarget = CreateTarget("Manual");
            CameraContext context = new CameraContext(owner, 2);
            context.SetViewTargetPolicy(new DefaultGameplayViewTargetPolicy());

            Actor resolved = context.ResolveViewTarget(target);
            context.SetManualViewTargetOverride(manualTarget);
            Actor manualResolved = context.ResolveViewTarget(target);
            context.ClearManualViewTargetOverride();
            Actor restored = context.ResolveViewTarget(target);

            Assert.AreSame(target, resolved);
            Assert.AreSame(manualTarget, manualResolved);
            Assert.AreSame(target, restored);
        }

        private PlayerController CreateOwner()
        {
            ownerObject = new GameObject("Owner");
            return ownerObject.AddComponent<PlayerController>();
        }

        private Actor CreateTarget(string name)
        {
            GameObject gameObject = new GameObject(name);
            targetObjects.Add(gameObject);
            return gameObject.AddComponent<Actor>();
        }

        private sealed class TestCameraMode : CameraMode
        {
            public int ActivateCount { get; private set; }
            public int DeactivateCount { get; private set; }

            public override void OnActivate(CameraContext context)
            {
                ActivateCount++;
            }

            public override void OnDeactivate(CameraContext context)
            {
                DeactivateCount++;
            }

            public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
            {
                return basePose;
            }
        }
    }
}
