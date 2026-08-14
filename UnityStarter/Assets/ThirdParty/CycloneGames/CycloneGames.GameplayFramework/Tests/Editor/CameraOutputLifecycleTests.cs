using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class CameraOutputLifecycleTests
    {
        private readonly List<GameObject> objects = new List<GameObject>(12);

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                GameObject gameObject = objects[i];
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            objects.Clear();
        }

        [Test]
        public void Prepare_RejectsMissingDestroyedDuplicateAndOverflowResources()
        {
            TestCameraOutput missing = CreateOutput("Missing");
            missing.SetResourceCount(1);
            Assert.IsFalse(missing.TryPrepare(out _));
            Assert.Zero(missing.PreparedResourceCount);

            GameObject destroyedResource = CreateObject("DestroyedResource");
            TestCameraOutput destroyed = CreateOutput("Destroyed");
            destroyed.SetResource(0, destroyedResource);
            destroyed.SetResourceCount(1);
            Object.DestroyImmediate(destroyedResource);
            Assert.IsFalse(destroyed.TryPrepare(out _));
            Assert.Zero(destroyed.PreparedResourceCount);

            GameObject shared = CreateObject("Shared");
            TestCameraOutput duplicate = CreateOutput("Duplicate");
            duplicate.SetResource(0, shared);
            duplicate.SetResource(1, shared);
            duplicate.SetResourceCount(2);
            Assert.IsFalse(duplicate.TryPrepare(out _));
            Assert.Zero(duplicate.PreparedResourceCount);

            TestCameraOutput overflow = CreateOutput("Overflow");
            for (int i = 0; i <= CameraOutputLimits.MaximumPreparedResourceCount; i++)
            {
                overflow.SetResource(i, CreateObject("Resource" + i));
            }
            overflow.SetResourceCount(CameraOutputLimits.MaximumPreparedResourceCount + 1);
            Assert.IsFalse(overflow.TryPrepare(out _));
            Assert.Zero(overflow.PreparedResourceCount);
        }

        [Test]
        public void PreparedResourceAccess_IsStableAndBoundsCheckedUntilDeactivation()
        {
            GameObject first = CreateObject("First");
            GameObject second = CreateObject("Second");
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, first);
            output.SetResource(1, second);
            output.SetResourceCount(2);

            Assert.IsTrue(output.TryPrepare(out string error), error);
            Assert.AreEqual(2, output.PreparedResourceCount);
            Assert.AreSame(first, output.GetPreparedResource(0));
            Assert.AreSame(second, output.GetPreparedResource(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => output.GetPreparedResource(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => output.GetPreparedResource(2));
            Assert.Throws<InvalidOperationException>(() => output.SetResourceCount(1));

            output.Deactivate(null);
            Assert.Zero(output.PreparedResourceCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => output.GetPreparedResource(0));
        }

        [Test]
        public void PreparedSteadyState_DoesNotAllocateManagedMemory()
        {
            GameObject resource = CreateObject("Resource");
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, resource);
            output.SetResourceCount(1);
            Assert.IsTrue(output.TryPrepare(out string prepareError), prepareError);

            string error = null;
            Object observed = null;
            bool success = true;
            for (int i = 0; i < 32; i++)
            {
                success &= output.TryPrepare(out error);
                observed = output.GetPreparedResource(0);
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                success &= output.TryPrepare(out error);
                observed = output.GetPreparedResource(0);
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(success, error);
            Assert.AreSame(resource, observed);
            Assert.Zero(allocatedBytes);
        }

        [Test]
        public void ActivationReturningFalse_RollsBackPreparedState()
        {
            CameraManager owner = CreateObject("Owner").AddComponent<CameraManager>();
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, CreateObject("Resource"));
            output.SetResourceCount(1);
            output.FailActivation = true;

            Assert.IsFalse(output.TryActivate(owner, out _));
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
            Assert.Zero(output.PreparedResourceCount);
            Assert.AreEqual(1, output.DeactivateCount);
            Assert.AreEqual(1, output.ReleasePreparedResourcesCount);
        }

        [Test]
        public void ActivationThrowing_RollsBackPreparedState()
        {
            CameraManager owner = CreateObject("Owner").AddComponent<CameraManager>();
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, CreateObject("Resource"));
            output.SetResourceCount(1);
            output.ThrowDuringActivation = true;

            Assert.Throws<InvalidOperationException>(() => output.TryActivate(owner, out _));
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
            Assert.Zero(output.PreparedResourceCount);
            Assert.AreEqual(1, output.DeactivateCount);
            Assert.AreEqual(1, output.ReleasePreparedResourcesCount);
        }

        [Test]
        public void DeactivationThrowing_StillClearsOwnerAndPreparedState()
        {
            CameraManager owner = CreateObject("Owner").AddComponent<CameraManager>();
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, CreateObject("Resource"));
            output.SetResourceCount(1);
            Assert.IsTrue(output.TryActivate(owner, out string activationError), activationError);
            output.ThrowDuringDeactivation = true;

            Assert.Throws<InvalidOperationException>(() => output.Deactivate(owner));
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
            Assert.Zero(output.PreparedResourceCount);
            Assert.AreEqual(1, output.DeactivateCount);
            Assert.AreEqual(1, output.ReleasePreparedResourcesCount);
        }

        [Test]
        public void DestroyedWrongOwner_CannotBypassReferenceIdentityCheck()
        {
            CameraManager owner = CreateObject("Owner").AddComponent<CameraManager>();
            CameraManager wrongOwner = CreateObject("WrongOwner").AddComponent<CameraManager>();
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, CreateObject("Resource"));
            output.SetResourceCount(1);
            Assert.IsTrue(output.TryActivate(owner, out string activationError), activationError);

            Object.DestroyImmediate(wrongOwner.gameObject);
            Assert.IsTrue(wrongOwner == null);
            Assert.IsFalse(ReferenceEquals(wrongOwner, null));
            output.Deactivate(wrongOwner);

            Assert.IsTrue(output.IsActive);
            Assert.AreSame(owner, output.Owner);
            output.Deactivate(owner);
            Assert.IsFalse(output.IsActive);
        }

        [Test]
        public void ResourceDestroyedWhileActive_IsReportedAndReleasedSafely()
        {
            CameraManager owner = CreateObject("Owner").AddComponent<CameraManager>();
            GameObject resource = CreateObject("Resource");
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, resource);
            output.SetResourceCount(1);
            Assert.IsTrue(output.TryActivate(owner, out string activationError), activationError);

            Object.DestroyImmediate(resource);
            Assert.IsFalse(output.TryPrepare(out string prepareError));
            Assert.IsNotEmpty(prepareError);
            Assert.AreEqual(1, output.PreparedResourceCount);
            Assert.DoesNotThrow(() => output.Deactivate(owner));
            Assert.Zero(output.PreparedResourceCount);
            Assert.IsFalse(output.IsActive);
        }

        [Test]
        public void ActivationReentrancy_IsRejectedAndRolledBack()
        {
            CameraManager owner = CreateObject("Owner").AddComponent<CameraManager>();
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, CreateObject("Resource"));
            output.SetResourceCount(1);
            output.ReenterDeactivationDuringActivation = true;

            Assert.Throws<InvalidOperationException>(() => output.TryActivate(owner, out _));
            Assert.IsFalse(output.IsActive);
            Assert.Zero(output.PreparedResourceCount);
            Assert.AreEqual(1, output.DeactivateCount);
        }

        [Test]
        public void UnityOutput_TargetCannotChangeWhilePrepared()
        {
            GameObject outputObject = CreateObject("Output");
            Camera first = CreateObject("FirstCamera").AddComponent<Camera>();
            Camera second = CreateObject("SecondCamera").AddComponent<Camera>();
            UnityCameraOutput output = outputObject.AddComponent<UnityCameraOutput>();
            output.SetTargetCamera(first);
            Assert.IsTrue(output.TryPrepare(out string error), error);

            Assert.Throws<InvalidOperationException>(() => output.SetTargetCamera(second));
            output.Deactivate(null);
            Assert.DoesNotThrow(() => output.SetTargetCamera(second));
        }

        private TestCameraOutput CreateOutput(string name)
        {
            return CreateObject(name).AddComponent<TestCameraOutput>();
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }

        private sealed class TestCameraOutput : CameraOutputBehaviour
        {
            private readonly Object[] resources =
                new Object[CameraOutputLimits.MaximumPreparedResourceCount + 1];
            private int resourceCount;

            public bool FailActivation { get; set; }
            public bool ThrowDuringActivation { get; set; }
            public bool ThrowDuringDeactivation { get; set; }
            public bool ReenterDeactivationDuringActivation { get; set; }
            public int DeactivateCount { get; private set; }
            public int ReleasePreparedResourcesCount { get; private set; }

            public override Object OutputObject =>
                PreparedResourceCount > 0 ? GetPreparedResource(0) : null;

            public void SetResource(int index, Object resource)
            {
                ThrowIfPreparedOrActive();
                resources[index] = resource;
            }

            public void SetResourceCount(int count)
            {
                ThrowIfPreparedOrActive();
                resourceCount = count;
            }

            protected override bool OnTryPrepare(out string error)
            {
                for (int i = 0; i < resourceCount; i++)
                {
                    if (!TryAddPreparedResource(resources[i], out error))
                    {
                        return false;
                    }
                }

                error = null;
                return true;
            }

            protected override bool OnActivate(CameraManager newOwner, out string error)
            {
                if (ReenterDeactivationDuringActivation)
                {
                    Deactivate(newOwner);
                }

                if (ThrowDuringActivation)
                {
                    throw new InvalidOperationException("Activation failure requested by the test.");
                }

                error = FailActivation ? "Activation rejection requested by the test." : null;
                return !FailActivation;
            }

            protected override void OnApplyPose(in CameraPose pose)
            {
            }

            protected override void OnDeactivate()
            {
                DeactivateCount++;
                if (ThrowDuringDeactivation)
                {
                    throw new InvalidOperationException("Deactivation failure requested by the test.");
                }
            }

            protected override void OnReleasePreparedResources()
            {
                ReleasePreparedResourcesCount++;
            }
        }
    }
}
