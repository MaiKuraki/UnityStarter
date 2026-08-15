using System;
using System.Collections.Generic;
using System.Threading;
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
        public void ResourceSet_RejectsMissingDestroyedDuplicateAndOverflowResources()
        {
            GameObject destroyed = CreateObject("Destroyed");
            Object.DestroyImmediate(destroyed);
            GameObject shared = CreateObject("Shared");

            Assert.IsFalse(CameraOutputResourceSet.TryCreate(
                1, null, null, null, null, out _, out _));
            Assert.IsFalse(CameraOutputResourceSet.TryCreate(
                1, destroyed, null, null, null, out _, out _));
            Assert.IsFalse(CameraOutputResourceSet.TryCreate(
                2, shared, shared, null, null, out _, out _));
            Assert.IsFalse(CameraOutputResourceSet.TryCreate(
                CameraOutputLimits.MaximumResourceCount + 1,
                shared,
                null,
                null,
                null,
                out _,
                out _));
        }

        [Test]
        public void CameraOutputBehaviour_LiveAccessRejectsWorkerThreadBeforeUnityAccess()
        {
            TestCameraOutput output = CreateObject("Output").AddComponent<TestCameraOutput>();
            Exception getterFailure = null;
            Exception discoveryFailure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = output.IsActive;
                }
                catch (Exception exception)
                {
                    getterFailure = exception;
                }

                try
                {
                    output.TryGetResourceSet(out _, out _);
                }
                catch (Exception exception)
                {
                    discoveryFailure = exception;
                }
            });

            worker.Start();
            worker.Join();

            Assert.IsInstanceOf<InvalidOperationException>(getterFailure);
            Assert.IsInstanceOf<InvalidOperationException>(discoveryFailure);
            UnityLifecycleTestUtility.InvokeAwake(output);
        }

        [Test]
        public void ResourceSet_IsStableBoundsCheckedAndAllocationFree()
        {
            GameObject first = CreateObject("First");
            GameObject second = CreateObject("Second");
            var resources = new CameraOutputResourceSet(first, second);

            Assert.AreEqual(2, resources.Count);
            Assert.AreSame(first, resources.GetResource(0));
            Assert.AreSame(second, resources.GetResource(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => resources.GetResource(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => resources.GetResource(2));

            Object observed = null;
            bool success = true;
            string error = null;
            for (int i = 0; i < 32; i++)
            {
                success &= resources.TryValidate(out error);
                observed = resources.GetResource(i & 1);
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                success &= resources.TryValidate(out error);
                observed = resources.GetResource(i & 1);
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(success, error);
            Assert.AreSame(second, observed);
            Assert.Zero(allocatedBytes);
        }

        [Test]
        public void ResourceDiscovery_IsStatelessAndLeavesSnapshotsIndependent()
        {
            GameObject first = CreateObject("First");
            GameObject second = CreateObject("Second");
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, first);
            output.SetResourceCount(1);

            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet firstSnapshot,
                out string firstError), firstError);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);

            Assert.DoesNotThrow(() => output.SetResource(0, second));
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet secondSnapshot,
                out string secondError), secondError);

            Assert.AreSame(first, firstSnapshot.GetResource(0));
            Assert.AreSame(second, secondSnapshot.GetResource(0));
            Assert.AreEqual(2, output.DiscoveryCount);
        }

        [Test]
        public void Activation_UsesProvidedSnapshotWithoutRediscovery()
        {
            CameraManager owner = CreateCameraManager("Owner");
            GameObject leased = CreateObject("Leased");
            GameObject replacement = CreateObject("Replacement");
            TestCameraOutput output = CreateOutput("Output");
            output.SetResource(0, leased);
            output.SetResourceCount(1);
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string discoveryError), discoveryError);
            output.SetResource(0, replacement);

            Assert.IsTrue(output.TryActivate(owner, in resources, out string error), error);

            Assert.AreEqual(1, output.DiscoveryCount);
            Assert.AreSame(leased, output.ActivatedResources.GetResource(0));
            output.Deactivate(owner);
        }

        [Test]
        public void ActivationReturningFalse_RemainsFaultedUntilExplicitDeactivation()
        {
            CameraManager owner = CreateCameraManager("Owner");
            TestCameraOutput output = CreateOutputWithOneResource("Output");
            output.FailActivation = true;
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string discoveryError), discoveryError);

            Assert.IsFalse(output.TryActivate(owner, in resources, out _));
            Assert.IsTrue(output.IsActive);
            Assert.AreSame(owner, output.Owner);

            output.Deactivate(owner);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
            Assert.AreEqual(1, output.DeactivateCount);
        }

        [Test]
        public void ActivationThrowing_RemainsFaultedUntilExplicitDeactivation()
        {
            CameraManager owner = CreateCameraManager("Owner");
            TestCameraOutput output = CreateOutputWithOneResource("Output");
            output.ThrowDuringActivation = true;
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string discoveryError), discoveryError);

            Assert.Throws<InvalidOperationException>(() =>
                output.TryActivate(owner, in resources, out _));
            Assert.IsTrue(output.IsActive);
            Assert.AreSame(owner, output.Owner);

            output.Deactivate(owner);
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
        }

        [Test]
        public void DeactivationThrowing_RetainsOwnerAndSupportsRetry()
        {
            CameraManager owner = CreateCameraManager("Owner");
            TestCameraOutput output = CreateOutputWithOneResource("Output");
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string discoveryError), discoveryError);
            Assert.IsTrue(output.TryActivate(owner, in resources, out string activationError), activationError);
            output.ThrowDuringDeactivation = true;

            Assert.Throws<InvalidOperationException>(() => output.Deactivate(owner));
            Assert.IsTrue(output.IsActive);
            Assert.AreSame(owner, output.Owner);
            Assert.Throws<InvalidOperationException>(() => output.SetResourceCount(1));

            output.ThrowDuringDeactivation = false;
            Assert.DoesNotThrow(() => output.Deactivate(owner));
            Assert.IsFalse(output.IsActive);
            Assert.IsNull(output.Owner);
            Assert.DoesNotThrow(() => output.SetResourceCount(1));
            Assert.AreEqual(2, output.DeactivateCount);
        }

        [Test]
        public void DestroyedWrongOwner_CannotBypassReferenceIdentityCheck()
        {
            CameraManager owner = CreateCameraManager("Owner");
            CameraManager wrongOwner = CreateCameraManager("WrongOwner");
            TestCameraOutput output = CreateOutputWithOneResource("Output");
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string discoveryError), discoveryError);
            Assert.IsTrue(output.TryActivate(owner, in resources, out string activationError), activationError);

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
        public void ActivationReentrancy_IsRejectedAndFaultCanBeReleased()
        {
            CameraManager owner = CreateCameraManager("Owner");
            TestCameraOutput output = CreateOutputWithOneResource("Output");
            output.ReenterDeactivationDuringActivation = true;
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string discoveryError), discoveryError);

            Assert.Throws<InvalidOperationException>(() =>
                output.TryActivate(owner, in resources, out _));
            Assert.IsTrue(output.IsActive);

            output.Deactivate(owner);
            Assert.IsFalse(output.IsActive);
            Assert.AreEqual(1, output.DeactivateCount);
        }

        [Test]
        public void UnityOutput_TargetCanChangeAfterDiscoveryButNotWhileBound()
        {
            CameraManager owner = CreateCameraManager("Owner");
            GameObject outputObject = CreateObject("Output");
            Camera first = CreateObject("FirstCamera").AddComponent<Camera>();
            Camera second = CreateObject("SecondCamera").AddComponent<Camera>();
            UnityCameraOutput output = outputObject.AddComponent<UnityCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(output);
            output.SetTargetCamera(first);
            Assert.IsTrue(output.TryGetResourceSet(
                out CameraOutputResourceSet resources,
                out string error), error);
            Assert.IsNull(output.ActiveCamera,
                "Resource discovery must not establish backend lifecycle state.");

            Assert.DoesNotThrow(() => output.SetTargetCamera(second));
            Assert.IsTrue(output.TryActivate(owner, in resources, out string activationError), activationError);
            Assert.AreSame(first, output.ActiveCamera);
            Assert.Throws<InvalidOperationException>(() => output.SetTargetCamera(second));

            output.Deactivate(owner);
            Assert.DoesNotThrow(() => output.SetTargetCamera(second));
        }

        private TestCameraOutput CreateOutputWithOneResource(string name)
        {
            TestCameraOutput output = CreateOutput(name);
            output.SetResource(0, CreateObject(name + "Resource"));
            output.SetResourceCount(1);
            return output;
        }

        private TestCameraOutput CreateOutput(string name)
        {
            TestCameraOutput output = CreateObject(name).AddComponent<TestCameraOutput>();
            UnityLifecycleTestUtility.InvokeAwake(output);
            return output;
        }

        private CameraManager CreateCameraManager(string name)
        {
            CameraManager manager = CreateObject(name).AddComponent<CameraManager>();
            UnityLifecycleTestUtility.InvokeAwake(manager);
            return manager;
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
                new Object[CameraOutputLimits.MaximumResourceCount + 1];
            private int resourceCount;

            public bool FailActivation { get; set; }
            public bool ThrowDuringActivation { get; set; }
            public bool ThrowDuringDeactivation { get; set; }
            public bool ReenterDeactivationDuringActivation { get; set; }
            public int DiscoveryCount { get; private set; }
            public int DeactivateCount { get; private set; }
            public CameraOutputResourceSet ActivatedResources { get; private set; }

            protected override Object OnGetOutputObject()
            {
                return ActivatedResources.IsValid
                    ? ActivatedResources.GetResource(0)
                    : resources[0];
            }

            public void SetResource(int index, Object resource)
            {
                ThrowIfLifecycleBound();
                resources[index] = resource;
            }

            public void SetResourceCount(int count)
            {
                ThrowIfLifecycleBound();
                resourceCount = count;
            }

            protected override bool OnTryGetResourceSet(
                out CameraOutputResourceSet resourceSet,
                out string error)
            {
                DiscoveryCount++;
                return CameraOutputResourceSet.TryCreate(
                    resourceCount,
                    resources[0],
                    resources[1],
                    resources[2],
                    resources[3],
                    out resourceSet,
                    out error);
            }

            protected override bool OnActivate(
                CameraManager newOwner,
                in CameraOutputResourceSet resourceSet,
                out string error)
            {
                ActivatedResources = resourceSet;
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

                ActivatedResources = default;
            }
        }
    }
}
