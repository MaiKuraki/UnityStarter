using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class CameraContextTests
    {
        private readonly List<GameObject> targetObjects = new List<GameObject>(4);
        private readonly List<ScriptableObject> scriptableObjects = new List<ScriptableObject>(2);
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
                ownerObject = null;
            }

            for (int i = 0; i < scriptableObjects.Count; i++)
            {
                if (scriptableObjects[i] != null)
                {
                    Object.DestroyImmediate(scriptableObjects[i]);
                }
            }

            scriptableObjects.Clear();
        }

        [Test]
        public void TryPushCameraMode_ReturnsResultAndRejectsDuplicateInstance()
        {
            CameraContext context = new CameraContext(null, 2);
            TestCameraMode mode = new TestCameraMode();

            Assert.IsTrue(context.TryPushCameraMode(mode));
            Assert.IsFalse(context.TryPushCameraMode(mode));
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreEqual(1, mode.ActivateCount);
        }

        [Test]
        public void CachedCameraContext_RejectsWorkerThreadLiveStateAccess()
        {
            CameraContext context = new CameraContext(null, 2);
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = context.CameraModeCount;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

            worker.Start();
            worker.Join();

            Assert.IsInstanceOf<InvalidOperationException>(failure);
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
            Assert.IsTrue(context.TryPushOrReplaceOldest(third, out CameraMode replacedMode));

            Assert.AreEqual(2, context.CameraModeCount);
            Assert.AreSame(first, replacedMode);
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
        public void TryPushOrReplaceOldest_RejectsDuplicateWithoutReplacing()
        {
            CameraContext context = new CameraContext(null, 2);
            TestCameraMode first = new TestCameraMode();
            TestCameraMode second = new TestCameraMode();
            context.TryPushCameraMode(first);
            context.TryPushCameraMode(second);

            Assert.IsFalse(context.TryPushOrReplaceOldest(second, out CameraMode replacedMode));
            Assert.IsNull(replacedMode);
            Assert.AreSame(first, context.GetCameraModeAt(0));
            Assert.AreSame(second, context.GetCameraModeAt(1));
            Assert.AreEqual(0, first.DeactivateCount);
            Assert.AreEqual(1, second.ActivateCount);
        }

        [Test]
        public void Clear_DeactivatesStackInReverseOrderThenBaseMode()
        {
            List<string> deactivationOrder = new List<string>(3);
            CameraContext context = new CameraContext(null, 2);
            TestCameraMode baseMode = new TestCameraMode("Base", deactivationOrder);
            TestCameraMode first = new TestCameraMode("First", deactivationOrder);
            TestCameraMode second = new TestCameraMode("Second", deactivationOrder);
            context.SetBaseCameraMode(baseMode);
            context.TryPushCameraMode(first);
            context.TryPushCameraMode(second);

            context.Clear();

            CollectionAssert.AreEqual(new[] { "Second", "First", "Base" }, deactivationOrder);
            Assert.AreEqual(0, context.CameraModeCount);
            Assert.IsNull(context.BaseCameraMode);
            Assert.IsNull(context.GetPrimaryCameraMode());
        }

        [Test]
        public void TryPushCameraMode_ActivationFailureRollsBackStack()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var mode = new ThrowingCameraMode(throwOnActivate: true, throwOnDeactivate: false);

            Assert.IsFalse(context.TryPushCameraMode(mode));
            Assert.AreEqual(0, context.CameraModeCount);
            Assert.IsNull(context.GetPrimaryCameraMode());
            Assert.IsFalse(context.HasModeLifecycleFault);
        }

        [Test]
        public void TryPushCameraMode_ActivationAndCleanupFailureRetainsModeAndFreezesContext()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var mode = new ThrowingCameraMode(throwOnActivate: true, throwOnDeactivate: true);

            Assert.IsFalse(context.TryPushCameraMode(mode));

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(mode, context.GetCameraModeAt(0));
            Assert.IsNull(context.GetPrimaryCameraMode());
            Assert.IsFalse(context.TryPushCameraMode(new TestCameraMode()));
            Assert.IsTrue(context.ContainsCameraMode(mode));
        }

        [Test]
        public void TryPushCameraMode_ActivationOutOfMemoryFaultsAndRetainsCleanupHandle()
        {
            CameraContext context = new CameraContext(null, 2);
            var mode = new OutOfMemoryCameraMode(throwOnActivate: true, throwOnceOnDeactivate: false);

            Assert.Throws<OutOfMemoryException>(() => context.TryPushCameraMode(mode));

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(mode, context.GetCameraModeAt(0));
            Assert.IsTrue(context.ContainsCameraMode(mode));
            Assert.IsNull(context.GetPrimaryCameraMode());

            Assert.IsTrue(context.Clear());
            Assert.IsFalse(context.HasModeLifecycleFault);
            Assert.AreEqual(0, context.CameraModeCount);
        }

        [Test]
        public void ActivationFailureLoggingOutOfMemory_FaultsAndRetainsCleanupHandle()
        {
            CameraContext context = new CameraContext(null, 2);
            var mode = new ThrowingCameraMode(
                throwOnActivate: true,
                throwOnDeactivate: false);

            using (new ScopedOutOfMemoryLogWriter())
            {
                Assert.Throws<OutOfMemoryException>(() => context.TryPushCameraMode(mode));
            }

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(mode, context.GetCameraModeAt(0));
            Assert.IsNull(context.GetPrimaryCameraMode());
            Assert.IsTrue(context.Clear());
            Assert.IsFalse(context.HasModeLifecycleFault);
        }

        [Test]
        public void DeactivationFailureLoggingOutOfMemory_FaultsAndRetainsCleanupHandle()
        {
            using var silentLog = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var mode = new ThrowingCameraMode(
                throwOnActivate: false,
                throwOnDeactivate: true);
            Assert.IsTrue(context.TryPushCameraMode(mode));

            silentLog.Dispose();
            using (new ScopedOutOfMemoryLogWriter())
            {
                Assert.Throws<OutOfMemoryException>(() => context.RemoveCameraMode(mode));
            }

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(mode, context.GetCameraModeAt(0));
            Assert.IsNull(context.GetPrimaryCameraMode());
        }

        [Test]
        public void SetBaseCameraMode_RollbackReactivationFailureRemovesInactiveMode()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var previous = new ReactivationFailingCameraMode();
            var replacement = new ThrowingCameraMode(
                throwOnActivate: true,
                throwOnDeactivate: false);
            context.SetBaseCameraMode(previous);

            context.SetBaseCameraMode(replacement);

            Assert.IsNull(context.BaseCameraMode);
            Assert.IsNull(context.GetPrimaryCameraMode());
            Assert.AreEqual(2, previous.ActivateCount);
            Assert.AreEqual(2, previous.DeactivateCount);
            Assert.AreEqual(1, replacement.ActivateCount);
            Assert.IsFalse(context.HasModeLifecycleFault);
        }

        [Test]
        public void SetBaseCameraMode_RollbackReactivationOutOfMemoryRetainsPreviousMode()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var previous = new ReactivationOutOfMemoryCameraMode();
            var replacement = new ThrowingCameraMode(
                throwOnActivate: true,
                throwOnDeactivate: false);
            context.SetBaseCameraMode(previous);

            Assert.Throws<OutOfMemoryException>(() => context.SetBaseCameraMode(replacement));

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreSame(previous, context.BaseCameraMode);
            Assert.IsTrue(context.ContainsCameraMode(previous));
            Assert.IsNull(context.GetPrimaryCameraMode());

            Assert.IsTrue(context.Clear());
            Assert.IsFalse(context.HasModeLifecycleFault);
            Assert.IsNull(context.BaseCameraMode);
        }

        [Test]
        public void SetBaseCameraMode_DeactivationFailureDoesNotReactivateUnknownMode()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var previous = new ThrowingCameraMode(
                throwOnActivate: false,
                throwOnDeactivate: true);
            var replacement = new TestCameraMode();
            context.SetBaseCameraMode(previous);

            context.SetBaseCameraMode(replacement);

            Assert.AreSame(previous, context.BaseCameraMode);
            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.IsNull(context.GetPrimaryCameraMode());
            Assert.AreEqual(1, previous.ActivateCount);
            Assert.AreEqual(1, previous.DeactivateCount);
            Assert.AreEqual(0, replacement.ActivateCount);
        }

        [Test]
        public void SetBaseCameraMode_ActivationAndCleanupFailureRetainsReplacementAndFreezesContext()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var previous = new TestCameraMode();
            var replacement = new ThrowingCameraMode(
                throwOnActivate: true,
                throwOnDeactivate: true);
            context.SetBaseCameraMode(previous);

            context.SetBaseCameraMode(replacement);

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreSame(replacement, context.BaseCameraMode);
            Assert.IsTrue(context.ContainsCameraMode(replacement));
            Assert.IsNull(context.GetPrimaryCameraMode());
            Assert.AreEqual(1, previous.DeactivateCount);
        }

        [Test]
        public void TryPushOrReplaceOldest_RollbackReactivationFailureRemovesInactiveOldest()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var oldest = new ReactivationFailingCameraMode();
            var newest = new TestCameraMode();
            var replacement = new ThrowingCameraMode(
                throwOnActivate: true,
                throwOnDeactivate: false);
            Assert.IsTrue(context.TryPushCameraMode(oldest));
            Assert.IsTrue(context.TryPushCameraMode(newest));

            Assert.IsFalse(context.TryPushOrReplaceOldest(
                replacement,
                out CameraMode replacedMode));

            Assert.IsNull(replacedMode);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(newest, context.GetCameraModeAt(0));
            Assert.AreSame(newest, context.GetPrimaryCameraMode());
            Assert.AreEqual(2, oldest.ActivateCount);
            Assert.AreEqual(2, oldest.DeactivateCount);
            Assert.AreEqual(1, replacement.ActivateCount);
            Assert.IsFalse(context.HasModeLifecycleFault);
        }

        [Test]
        public void TryPushOrReplaceOldest_DeactivationFailureDoesNotReactivateOldest()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var oldest = new ThrowingCameraMode(
                throwOnActivate: false,
                throwOnDeactivate: true);
            var newest = new TestCameraMode();
            var replacement = new TestCameraMode();
            Assert.IsTrue(context.TryPushCameraMode(oldest));
            Assert.IsTrue(context.TryPushCameraMode(newest));

            Assert.IsFalse(context.TryPushOrReplaceOldest(replacement, out CameraMode replaced));

            Assert.IsNull(replaced);
            Assert.AreSame(oldest, context.GetCameraModeAt(0));
            Assert.AreSame(newest, context.GetCameraModeAt(1));
            Assert.AreEqual(1, oldest.ActivateCount);
            Assert.AreEqual(1, oldest.DeactivateCount);
            Assert.AreEqual(0, replacement.ActivateCount);
            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.IsNull(context.GetPrimaryCameraMode());
        }

        [Test]
        public void TryPushOrReplaceOldest_ActivationAndCleanupFailureRetainsReplacement()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var oldest = new TestCameraMode();
            var newest = new TestCameraMode();
            var replacement = new ThrowingCameraMode(
                throwOnActivate: true,
                throwOnDeactivate: true);
            Assert.IsTrue(context.TryPushCameraMode(oldest));
            Assert.IsTrue(context.TryPushCameraMode(newest));

            Assert.IsFalse(context.TryPushOrReplaceOldest(replacement, out CameraMode replaced));

            Assert.IsNull(replaced);
            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(2, context.CameraModeCount);
            Assert.AreSame(newest, context.GetCameraModeAt(0));
            Assert.AreSame(replacement, context.GetCameraModeAt(1));
            Assert.IsTrue(context.ContainsCameraMode(replacement));
            Assert.IsNull(context.GetPrimaryCameraMode());
        }

        [Test]
        public void TryPushOrReplaceOldest_ActivationOutOfMemoryRetainsUncommittedReplacement()
        {
            CameraContext context = new CameraContext(null, 2);
            var oldest = new TestCameraMode();
            var newest = new TestCameraMode();
            var replacement = new OutOfMemoryCameraMode(
                throwOnActivate: true,
                throwOnceOnDeactivate: false);
            Assert.IsTrue(context.TryPushCameraMode(oldest));
            Assert.IsTrue(context.TryPushCameraMode(newest));

            Assert.Throws<OutOfMemoryException>(() =>
                context.TryPushOrReplaceOldest(replacement, out _));

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(newest, context.GetCameraModeAt(0));
            Assert.IsTrue(context.ContainsCameraMode(replacement));
            Assert.IsNull(context.GetPrimaryCameraMode());

            Assert.IsTrue(context.Clear());
            Assert.IsFalse(context.HasModeLifecycleFault);
            Assert.AreEqual(0, context.CameraModeCount);
            Assert.IsFalse(context.ContainsCameraMode(replacement));
        }

        [Test]
        public void RemoveCameraMode_DeactivationFailureRetainsModeAndReturnsFalse()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var mode = new ThrowingCameraMode(throwOnActivate: false, throwOnDeactivate: true);
            Assert.IsTrue(context.TryPushCameraMode(mode));

            Assert.IsFalse(context.RemoveCameraMode(mode));

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(mode, context.GetCameraModeAt(0));
            Assert.IsNull(context.GetPrimaryCameraMode());
        }

        [Test]
        public void Clear_ContinuesAfterModeDeactivationFailure()
        {
            using var logScope = new ScopedSilentLogWriter();
            CameraContext context = new CameraContext(null, 2);
            var baseMode = new TestCameraMode();
            var first = new TestCameraMode();
            var throwing = new ThrowOnceDeactivationCameraMode();
            context.SetBaseCameraMode(baseMode);
            context.TryPushCameraMode(first);
            context.TryPushCameraMode(throwing);

            context.Clear();

            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(throwing, context.GetCameraModeAt(0));
            Assert.IsNull(context.BaseCameraMode);
            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.IsNull(context.GetPrimaryCameraMode());
            Assert.AreEqual(1, first.DeactivateCount);
            Assert.AreEqual(1, baseMode.DeactivateCount);
            Assert.AreEqual(1, throwing.DeactivateCount);

            context.Clear();

            Assert.AreEqual(0, context.CameraModeCount);
            Assert.IsFalse(context.HasModeLifecycleFault);
            Assert.AreEqual(2, throwing.DeactivateCount);
        }

        [Test]
        public void Clear_DeactivationOutOfMemoryFaultsAndPreservesRetryState()
        {
            CameraContext context = new CameraContext(null, 2);
            var last = new TestCameraMode();
            var throwing = new OutOfMemoryCameraMode(
                throwOnActivate: false,
                throwOnceOnDeactivate: true);
            Assert.IsTrue(context.TryPushCameraMode(throwing));
            Assert.IsTrue(context.TryPushCameraMode(last));

            Assert.Throws<OutOfMemoryException>(() => context.Clear());

            Assert.IsTrue(context.HasModeLifecycleFault);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.IsTrue(context.ContainsCameraMode(throwing));
            Assert.IsFalse(context.ContainsCameraMode(last));
            Assert.AreEqual(1, last.DeactivateCount);
            Assert.IsNull(context.GetPrimaryCameraMode());

            Assert.IsTrue(context.Clear());
            Assert.IsFalse(context.HasModeLifecycleFault);
            Assert.AreEqual(0, context.CameraModeCount);
        }

        [Test]
        public void PlayerControllerTeardown_RetainsFaultedContextUntilCleanupSucceeds()
        {
            using var logScope = new ScopedSilentLogWriter();
            PlayerController owner = CreateOwner();
            CameraContext context = owner.GetCameraContext();
            var throwing = new ThrowOnceDeactivationCameraMode();
            Assert.IsTrue(context.TryPushCameraMode(throwing));

            Assert.IsFalse(owner.TryReleaseCameraContextForWorldTeardown());

            Assert.AreSame(context, owner.GetCameraContext());
            Assert.IsTrue(context.HasModeLifecycleFault);

            Assert.IsTrue(owner.TryReleaseCameraContextForWorldTeardown());

            Assert.AreNotSame(context, owner.GetCameraContext());
            Assert.IsFalse(context.HasModeLifecycleFault);
        }

        [Test]
        public void CameraActionBinding_RejectsActiveActionOverflow()
        {
            PlayerController owner = CreateOwner();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "maxActiveActions", 1);
            UnityLifecycleTestUtility.InvokeAwake(binding);
            CameraActionPreset preset = CreatePreset();

            Assert.IsTrue(binding.PlayPreset("First", preset, policy: CameraActionBinding.TriggerPolicy.Stack, autoRemoveOnFinish: false));
            Assert.IsFalse(binding.PlayPreset("Second", preset, policy: CameraActionBinding.TriggerPolicy.Stack, autoRemoveOnFinish: false));
            Assert.AreEqual(1, binding.ActiveActionCount);
            Assert.AreEqual(1, owner.GetCameraContext().CameraModeCount);
        }

        [Test]
        public void CameraActionBinding_ReturnsRejectedModeToBoundedPool()
        {
            PlayerController owner = CreateOwner();
            CameraContext context = owner.GetCameraContext();
            for (int i = 0; i < context.MaxCameraModes; i++)
            {
                Assert.IsTrue(context.TryPushCameraMode(new TestCameraMode()));
            }

            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "maxPooledModes", 1);
            UnityLifecycleTestUtility.InvokeAwake(binding);
            CameraActionPreset preset = CreatePreset();

            Assert.IsFalse(binding.PlayPreset("Rejected", preset, policy: CameraActionBinding.TriggerPolicy.Stack, autoRemoveOnFinish: false));
            Assert.IsFalse(binding.PlayPreset("RejectedAgain", preset, policy: CameraActionBinding.TriggerPolicy.Stack, autoRemoveOnFinish: false));
            Assert.AreEqual(0, binding.ActiveActionCount);
            Assert.AreEqual(1, binding.PooledModeCount);
            Assert.AreEqual(context.MaxCameraModes, context.CameraModeCount);
        }

        [Test]
        public void CameraActionBinding_StopAllUsesOriginalOwnerAndKeepsPoolBounded()
        {
            PlayerController owner = CreateOwner();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "maxActiveActions", 2);
            SetPrivateField(binding, "maxPooledModes", 1);
            UnityLifecycleTestUtility.InvokeAwake(binding);
            CameraActionPreset preset = CreatePreset();

            Assert.IsTrue(binding.PlayPreset("First", preset, policy: CameraActionBinding.TriggerPolicy.Stack, autoRemoveOnFinish: false));
            Assert.IsTrue(binding.PlayPreset("Second", preset, policy: CameraActionBinding.TriggerPolicy.Stack, autoRemoveOnFinish: false));
            SetPrivateField(binding, "playerController", null);
            SetPrivateField(binding, "autoResolvePlayerController", false);

            binding.StopAllActions();

            Assert.AreEqual(0, owner.GetCameraContext().CameraModeCount);
            Assert.AreEqual(0, binding.ActiveActionCount);
            Assert.AreEqual(1, binding.PooledModeCount);
        }

        [Test]
        public void CameraActionBinding_StopDuringEvaluationCommitsRemovalBeforePoolReturn()
        {
            PlayerController owner = CreateOwner();
            CameraContext context = owner.GetCameraContext();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "maxPooledModes", 1);
            UnityLifecycleTestUtility.InvokeAwake(binding);
            CameraActionPreset preset = CreatePreset();
            Assert.IsTrue(binding.PlayPreset(
                "Deferred",
                preset,
                policy: CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false));
            MethodInfo beginEvaluation = typeof(CameraContext).GetMethod(
                "TryBeginEvaluation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo endEvaluation = typeof(CameraContext).GetMethod(
                "EndEvaluation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo lateUpdate = typeof(CameraActionBinding).GetMethod(
                "LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(beginEvaluation);
            Assert.IsNotNull(endEvaluation);
            Assert.IsNotNull(lateUpdate);
            Assert.IsTrue((bool)beginEvaluation.Invoke(context, null));

            Assert.IsTrue(binding.StopAction("Deferred"));
            Assert.AreEqual(1, binding.ActiveActionCount);
            Assert.AreEqual(0, binding.PooledModeCount);
            Assert.AreEqual(1, context.CameraModeCount);

            endEvaluation.Invoke(context, null);

            Assert.AreEqual(0, context.CameraModeCount);
            lateUpdate.Invoke(binding, null);
            Assert.AreEqual(0, binding.ActiveActionCount);
            Assert.AreEqual(1, binding.PooledModeCount);
        }

        [Test]
        public void CameraActionBinding_CachedReferenceRejectsWorkerThreadLiveStateAccess()
        {
            CreateOwner();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = binding.ActiveActionCount;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

            worker.Start();
            worker.Join();

            Assert.IsInstanceOf<InvalidOperationException>(failure);
        }

        [Test]
        public void CameraActionUnityAdapters_AreClosedAndRejectLiveAccessOutsideTheirAwakeThread()
        {
            Assert.IsTrue(typeof(AnimatorCameraActionBridge).IsSealed);
            Assert.IsTrue(typeof(TimelineCameraActionReceiver).IsSealed);

            ownerObject = new GameObject("CameraActionAdapters");
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            AnimatorCameraActionBridge animatorBridge =
                ownerObject.AddComponent<AnimatorCameraActionBridge>();
            TimelineCameraActionReceiver timelineReceiver =
                ownerObject.AddComponent<TimelineCameraActionReceiver>();

            Assert.Throws<InvalidOperationException>(() =>
                animatorBridge.PlayCameraAction("BeforeAwake"));
            Assert.Throws<InvalidOperationException>(() =>
                timelineReceiver.OnNotify(default, null, null));

            UnityLifecycleTestUtility.InvokeAwake(binding);
            UnityLifecycleTestUtility.InvokeAwake(animatorBridge);
            UnityLifecycleTestUtility.InvokeAwake(timelineReceiver);

            Exception animatorFailure = null;
            Exception timelineFailure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    animatorBridge.StopAllCameraActions();
                }
                catch (Exception exception)
                {
                    animatorFailure = exception;
                }

                try
                {
                    timelineReceiver.OnNotify(default(Playable), null, null);
                }
                catch (Exception exception)
                {
                    timelineFailure = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)));

            Assert.IsInstanceOf<InvalidOperationException>(animatorFailure);
            Assert.IsInstanceOf<InvalidOperationException>(timelineFailure);
        }

        [Test]
        public void TimelineCameraActionReceiver_AwakeRejectsMissingBinding()
        {
            ownerObject = new GameObject("TimelineReceiverWithoutBinding");
            TimelineCameraActionReceiver receiver =
                ownerObject.AddComponent<TimelineCameraActionReceiver>();

            Assert.Throws<InvalidOperationException>(() =>
                UnityLifecycleTestUtility.InvokeAwake(receiver));
        }

        [Test]
        public void CameraActionMap_RequiresWarmupAndPublishesAnOwnerThreadValueSnapshot()
        {
            CameraActionPreset preset = CreatePreset();
            CameraActionMap map = ScriptableObject.CreateInstance<CameraActionMap>();
            scriptableObjects.Add(map);
            var authoringEntries = new List<CameraActionMap.Entry>
            {
                new CameraActionMap.Entry(
                    "Dodge",
                    preset,
                    CameraActionBinding.TriggerPolicy.ReplaceSameKey,
                    autoRemoveOnFinish: true,
                    durationOverride: 0.25f),
            };
            SetPrivateField(map, "entries", authoringEntries);

            Assert.IsTrue(typeof(CameraActionMap).IsSealed);
            Assert.IsNull(typeof(CameraActionMap).GetMethod("GetEntries"));
            Assert.Throws<InvalidOperationException>(() =>
                map.TryGetEntry("Dodge", out _));

            map.Warmup();
            authoringEntries.Clear();

            Assert.AreEqual(1, map.EntryCount);
            CameraActionMap.Entry indexedEntry = map.GetEntry(0);
            Assert.AreEqual("Dodge", indexedEntry.ActionKey);
            Assert.AreSame(preset, indexedEntry.Preset);
            Assert.IsTrue(map.TryGetEntry("Dodge", out CameraActionMap.Entry keyedEntry));
            Assert.AreEqual("Dodge", keyedEntry.ActionKey);

            Exception workerFailure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = map.EntryCount;
                }
                catch (Exception exception)
                {
                    workerFailure = exception;
                }
            });
            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)));
            Assert.IsInstanceOf<InvalidOperationException>(workerFailure);
        }

        [Test]
        public void CameraActionMap_FailedWarmupDoesNotPublishPartialStateAndCanRetry()
        {
            CameraActionPreset preset = CreatePreset();
            CameraActionMap map = ScriptableObject.CreateInstance<CameraActionMap>();
            scriptableObjects.Add(map);
            var validEntry = new CameraActionMap.Entry(
                "Valid",
                preset,
                CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false,
                durationOverride: -1f);
            SetPrivateField(map, "entries", new List<CameraActionMap.Entry>
            {
                validEntry,
                default,
            });

            Assert.Throws<InvalidOperationException>(map.Warmup);
            Assert.Throws<InvalidOperationException>(() =>
                map.TryGetEntry("Valid", out _));

            SetPrivateField(map, "entries", new List<CameraActionMap.Entry> { validEntry });
            map.Warmup();

            Assert.IsTrue(map.TryGetEntry("Valid", out _));
            Assert.AreEqual(1, map.EntryCount);
        }

        [Test]
        public void CameraActionMap_RejectsAuthoringOverflowBeforePublishingRuntimeState()
        {
            CameraActionPreset preset = CreatePreset();
            CameraActionMap map = ScriptableObject.CreateInstance<CameraActionMap>();
            scriptableObjects.Add(map);
            var entry = new CameraActionMap.Entry(
                "Bounded",
                preset,
                CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false,
                durationOverride: -1f);
            var entries = new List<CameraActionMap.Entry>(
                CameraActionMap.MaximumEntryCount + 1);
            for (int index = 0; index <= CameraActionMap.MaximumEntryCount; index++)
            {
                entries.Add(entry);
            }
            SetPrivateField(map, "entries", entries);

            Assert.Throws<InvalidOperationException>(map.Warmup);
            Assert.Throws<InvalidOperationException>(() =>
                map.TryGetEntry("Bounded", out _));
        }

        [TestCase("maxActiveActions")]
        [TestCase("maxPooledModes")]
        public void CameraActionBinding_AwakeRejectsRuntimeBudgetsAboveHardCeilings(
            string fieldName)
        {
            ownerObject = new GameObject("InvalidCameraActionBudget");
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, fieldName, int.MaxValue);

            Assert.Throws<InvalidOperationException>(() =>
                UnityLifecycleTestUtility.InvokeAwake(binding));
        }

        [Test]
        public void CameraActionBinding_AwakeWarmsMapAndUsesValidatedInlineValueEntries()
        {
            PlayerController owner = CreateOwner();
            CameraActionPreset mappedPreset = CreatePreset();
            CameraActionPreset inlinePreset = CreatePreset();
            CameraActionMap map = ScriptableObject.CreateInstance<CameraActionMap>();
            scriptableObjects.Add(map);
            SetPrivateField(map, "entries", new List<CameraActionMap.Entry>
            {
                new CameraActionMap.Entry(
                    "Mapped",
                    mappedPreset,
                    CameraActionBinding.TriggerPolicy.Stack,
                    autoRemoveOnFinish: false,
                    durationOverride: -1f),
            });

            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "actionMap", map);
            SetPrivateField(binding, "actionEntries", new List<CameraActionBinding.CameraActionEntry>
            {
                new CameraActionBinding.CameraActionEntry(
                    "Inline",
                    inlinePreset,
                    CameraActionBinding.TriggerPolicy.Stack,
                    autoRemoveOnFinish: false,
                    durationOverride: -1f),
            });

            UnityLifecycleTestUtility.InvokeAwake(binding);

            Assert.AreEqual(1, map.EntryCount);
            Assert.IsTrue(binding.PlayAction("Mapped"));
            Assert.IsTrue(binding.PlayAction("Inline"));
            Assert.AreEqual(2, binding.ActiveActionCount);
            Assert.AreEqual(2, owner.GetCameraContext().CameraModeCount);
        }

        [Test]
        public void CameraActionBinding_AwakeRejectsInlineAuthoringOverflow()
        {
            CameraActionPreset preset = CreatePreset();
            ownerObject = new GameObject("InlineCameraActionOverflow");
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            var entry = new CameraActionBinding.CameraActionEntry(
                "Bounded",
                preset,
                CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false,
                durationOverride: -1f);
            var entries = new List<CameraActionBinding.CameraActionEntry>(
                CameraActionBinding.MaximumInlineActionEntryCount + 1);
            for (int index = 0;
                 index <= CameraActionBinding.MaximumInlineActionEntryCount;
                 index++)
            {
                entries.Add(entry);
            }
            SetPrivateField(binding, "actionEntries", entries);

            Assert.Throws<InvalidOperationException>(() =>
                UnityLifecycleTestUtility.InvokeAwake(binding));
        }

        [Test]
        public void TimelineCameraActionReceiver_PublishesBoundedLookupAndRoutesNotification()
        {
            PlayerController owner = CreateOwner();
            CameraActionPreset preset = CreatePreset();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "actionEntries", new List<CameraActionBinding.CameraActionEntry>
            {
                new CameraActionBinding.CameraActionEntry(
                    "Timeline",
                    preset,
                    CameraActionBinding.TriggerPolicy.Stack,
                    autoRemoveOnFinish: false,
                    durationOverride: -1f),
            });
            UnityLifecycleTestUtility.InvokeAwake(binding);

            TestNotification notification =
                ScriptableObject.CreateInstance<TestNotification>();
            scriptableObjects.Add(notification);
            TimelineCameraActionReceiver receiver =
                ownerObject.AddComponent<TimelineCameraActionReceiver>();
            SetPrivateField(receiver, "signalMappings", new List<TimelineCameraActionReceiver.SignalMapping>
            {
                new TimelineCameraActionReceiver.SignalMapping(
                    notification,
                    "Timeline",
                    stopOnReceive: false,
                    durationOverride: -1f),
            });
            UnityLifecycleTestUtility.InvokeAwake(receiver);

            receiver.OnNotify(default, notification, null);

            Assert.AreEqual(1, binding.ActiveActionCount);
            Assert.AreEqual(1, owner.GetCameraContext().CameraModeCount);
        }

        [Test]
        public void TimelineCameraActionReceiver_RejectsMappingOverflowBeforePublishing()
        {
            ownerObject = new GameObject("TimelineMappingOverflow");
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            UnityLifecycleTestUtility.InvokeAwake(binding);
            TestNotification notification =
                ScriptableObject.CreateInstance<TestNotification>();
            scriptableObjects.Add(notification);
            var mapping = new TimelineCameraActionReceiver.SignalMapping(
                notification,
                "Bounded",
                stopOnReceive: false,
                durationOverride: -1f);
            var mappings = new List<TimelineCameraActionReceiver.SignalMapping>(
                TimelineCameraActionReceiver.MaximumSignalMappingCount + 1);
            for (int index = 0;
                 index <= TimelineCameraActionReceiver.MaximumSignalMappingCount;
                 index++)
            {
                mappings.Add(mapping);
            }

            TimelineCameraActionReceiver receiver =
                ownerObject.AddComponent<TimelineCameraActionReceiver>();
            SetPrivateField(receiver, "signalMappings", mappings);

            Assert.Throws<InvalidOperationException>(() =>
                UnityLifecycleTestUtility.InvokeAwake(receiver));
            Assert.Throws<InvalidOperationException>(() =>
                receiver.OnNotify(default, notification, null));
        }

        [Test]
        public void CameraActionSerializedValueTypes_DoNotExposeMutableFields()
        {
            Assert.Zero(typeof(CameraActionBinding.CameraActionEntry).GetFields(
                BindingFlags.Instance | BindingFlags.Public).Length);
            Assert.Zero(typeof(CameraActionMap.Entry).GetFields(
                BindingFlags.Instance | BindingFlags.Public).Length);
            Assert.Zero(typeof(TimelineCameraActionReceiver.SignalMapping).GetFields(
                BindingFlags.Instance | BindingFlags.Public).Length);
        }

        [Test]
        public void CameraActionBinding_ReleaseUsesCommittedContextAfterOwnerBecomesFakeNull()
        {
            var controllerObject = new GameObject("Owner");
            var owner = controllerObject.AddComponent<NoDestroyCleanupPlayerController>();
            UnityLifecycleTestUtility.InvokeAwake(owner);
            CameraContext committedContext = owner.GetCameraContext();
            var bindingObject = new GameObject("Binding");
            targetObjects.Add(bindingObject);
            CameraActionBinding binding = bindingObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "playerController", owner);
            SetPrivateField(binding, "autoResolvePlayerController", false);
            SetPrivateField(binding, "maxPooledModes", 1);
            UnityLifecycleTestUtility.InvokeAwake(binding);
            CameraActionPreset preset = CreatePreset();
            Assert.IsTrue(binding.PlayPreset(
                "Committed",
                preset,
                policy: CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false));
            Assert.AreEqual(1, committedContext.CameraModeCount);

            Object.DestroyImmediate(controllerObject);
            Assert.IsTrue(owner == null);

            binding.StopAllActions();

            Assert.AreEqual(0, committedContext.CameraModeCount);
            Assert.AreEqual(0, binding.ActiveActionCount);
            Assert.AreEqual(1, binding.PooledModeCount);
        }

        [Test]
        public void CameraActionBinding_DestroyedDuringContextMutationTransfersEveryPendingMode()
        {
            PlayerController owner = CreateOwner();
            CameraContext context = owner.GetCameraContext();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            UnityLifecycleTestUtility.InvokeAwake(binding);
            CameraActionPreset preset = CreatePreset();

            Assert.IsTrue(binding.PlayPreset(
                "First",
                preset,
                policy: CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false));
            Assert.IsTrue(binding.PlayPreset(
                "Second",
                preset,
                policy: CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false));
            Assert.AreEqual(2, context.CameraModeCount);

            var destroyBindingMode = new DestroyBindingOnActivateCameraMode(binding);
            Assert.IsTrue(context.TryPushCameraMode(destroyBindingMode));

            Assert.IsTrue(binding == null);
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreSame(destroyBindingMode, context.GetCameraModeAt(0));
        }

        [Test]
        public void CameraActionBinding_PublishesCleanupOwnerBeforeControllerPushCallbacks()
        {
            ownerObject = new GameObject("DestroyBindingAfterPush");
            var owner = ownerObject.AddComponent<DestroyBindingAfterPushPlayerController>();
            UnityLifecycleTestUtility.InvokeAwake(owner);
            CameraContext context = owner.GetCameraContext();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            UnityLifecycleTestUtility.InvokeAwake(binding);
            owner.BindingToDestroy = binding;

            Assert.IsTrue(binding.PlayPreset(
                "Transactional",
                CreatePreset(),
                policy: CameraActionBinding.TriggerPolicy.Stack,
                autoRemoveOnFinish: false));

            Assert.IsTrue(binding == null);
            Assert.AreEqual(0, context.CameraModeCount);
        }

        [Test]
        public void CameraActionBinding_AwakePreallocatesConfiguredRuntimeBudgets()
        {
            ownerObject = new GameObject("Owner");
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            SetPrivateField(binding, "maxActiveActions", 24);
            SetPrivateField(binding, "maxPooledModes", 20);
            FieldInfo poolField = typeof(CameraActionBinding).GetField(
                "modePool",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object initialPool = poolField?.GetValue(binding);
            MethodInfo awake = typeof(CameraActionBinding).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(poolField);
            Assert.IsNotNull(awake);
            awake.Invoke(binding, null);

            object activeActions = typeof(CameraActionBinding)
                .GetField("activeActions", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(binding);
            object resizedPool = poolField.GetValue(binding);
            Assert.IsNotNull(activeActions);
            int activeCapacity = (int)activeActions.GetType()
                .GetProperty("Capacity")
                .GetValue(activeActions);

            Assert.GreaterOrEqual(activeCapacity, 24);
            Assert.IsNotNull(resizedPool);
            Assert.AreNotSame(initialPool, resizedPool);
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
            PlayerController owner = ownerObject.AddComponent<PlayerController>();
            UnityLifecycleTestUtility.InvokeAwake(owner);
            return owner;
        }

        private Actor CreateTarget(string name)
        {
            GameObject gameObject = new GameObject(name);
            targetObjects.Add(gameObject);
            Actor target = gameObject.AddComponent<Actor>();
            UnityLifecycleTestUtility.InvokeAwake(target);
            return target;
        }

        private CameraActionPreset CreatePreset()
        {
            CameraActionPreset preset = ScriptableObject.CreateInstance<CameraActionPreset>();
            scriptableObjects.Add(preset);
            return preset;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing field: {fieldName}");
            field.SetValue(target, value);
        }

        private sealed class TestCameraMode : CameraMode
        {
            private readonly string name;
            private readonly List<string> deactivationOrder;

            public int ActivateCount { get; private set; }
            public int DeactivateCount { get; private set; }

            public TestCameraMode()
            {
            }

            public TestCameraMode(string name, List<string> deactivationOrder)
            {
                this.name = name;
                this.deactivationOrder = deactivationOrder;
            }

            public override void OnActivate(CameraContext context)
            {
                ActivateCount++;
            }

            public override void OnDeactivate(CameraContext context)
            {
                DeactivateCount++;
                deactivationOrder?.Add(name);
            }

            public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class ThrowingCameraMode : CameraMode
        {
            private readonly bool throwOnActivate;
            private readonly bool throwOnDeactivate;

            public ThrowingCameraMode(bool throwOnActivate, bool throwOnDeactivate)
            {
                this.throwOnActivate = throwOnActivate;
                this.throwOnDeactivate = throwOnDeactivate;
            }

            public int ActivateCount { get; private set; }
            public int DeactivateCount { get; private set; }

            public override void OnActivate(CameraContext context)
            {
                ActivateCount++;
                if (throwOnActivate)
                {
                    throw new InvalidOperationException("Activation failure requested by test.");
                }
            }

            public override void OnDeactivate(CameraContext context)
            {
                DeactivateCount++;
                if (throwOnDeactivate)
                {
                    throw new InvalidOperationException("Deactivation failure requested by test.");
                }
            }

            public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class ReactivationFailingCameraMode : CameraMode
        {
            public int ActivateCount { get; private set; }
            public int DeactivateCount { get; private set; }

            public override void OnActivate(CameraContext context)
            {
                ActivateCount++;
                if (ActivateCount > 1)
                {
                    throw new InvalidOperationException(
                        "Reactivation failure requested by test.");
                }
            }

            public override void OnDeactivate(CameraContext context)
            {
                DeactivateCount++;
            }

            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class ReactivationOutOfMemoryCameraMode : CameraMode
        {
            private int activateCount;

            public override void OnActivate(CameraContext context)
            {
                activateCount++;
                if (activateCount > 1)
                {
                    throw new OutOfMemoryException(
                        "Reactivation OOM requested by test.");
                }
            }

            public override void OnDeactivate(CameraContext context)
            {
            }

            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class ThrowOnceDeactivationCameraMode : CameraMode
        {
            public int DeactivateCount { get; private set; }

            public override void OnDeactivate(CameraContext context)
            {
                DeactivateCount++;
                if (DeactivateCount == 1)
                {
                    throw new InvalidOperationException(
                        "First deactivation failure requested by test.");
                }
            }

            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class OutOfMemoryCameraMode : CameraMode
        {
            private readonly bool throwOnActivate;
            private readonly bool throwOnceOnDeactivate;

            public OutOfMemoryCameraMode(bool throwOnActivate, bool throwOnceOnDeactivate)
            {
                this.throwOnActivate = throwOnActivate;
                this.throwOnceOnDeactivate = throwOnceOnDeactivate;
            }

            public int DeactivateCount { get; private set; }

            public override void OnActivate(CameraContext context)
            {
                if (throwOnActivate)
                {
                    throw new OutOfMemoryException("Activation OOM requested by test.");
                }
            }

            public override void OnDeactivate(CameraContext context)
            {
                DeactivateCount++;
                if (throwOnceOnDeactivate && DeactivateCount == 1)
                {
                    throw new OutOfMemoryException("Deactivation OOM requested by test.");
                }
            }

            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class NoDestroyCleanupPlayerController : PlayerController
        {
            protected override void OnDestroy()
            {
                // Deliberately leave the pure C# CameraContext alive to model a Unity fake-null
                // owner whose independently hosted CameraActionBinding releases the action later.
            }
        }

        private sealed class DestroyBindingOnActivateCameraMode : CameraMode
        {
            private CameraActionBinding binding;

            public DestroyBindingOnActivateCameraMode(CameraActionBinding binding)
            {
                this.binding = binding;
            }

            public override void OnActivate(CameraContext context)
            {
                CameraActionBinding bindingToDestroy = binding;
                binding = null;
                UnityLifecycleTestUtility.InvokeOnDisable(bindingToDestroy);
                UnityLifecycleTestUtility.InvokeOnDestroy(bindingToDestroy);
                Object.DestroyImmediate(bindingToDestroy);
            }

            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class DestroyBindingAfterPushPlayerController : PlayerController
        {
            public CameraActionBinding BindingToDestroy { get; set; }

            public override bool TryPushCameraMode(CameraMode cameraMode)
            {
                bool pushed = base.TryPushCameraMode(cameraMode);
                CameraActionBinding binding = BindingToDestroy;
                BindingToDestroy = null;
                UnityLifecycleTestUtility.InvokeOnDisable(binding);
                UnityLifecycleTestUtility.InvokeOnDestroy(binding);
                Object.DestroyImmediate(binding);
                return pushed;
            }
        }

        private sealed class TestNotification : ScriptableObject, INotification
        {
            public PropertyName id => new PropertyName("CameraContextTests.Notification");
        }

        private sealed class ScopedSilentLogWriter : ILogWriter, IDisposable
        {
            private ILogWriter previousWriter;
            private bool isDisposed;

            public ScopedSilentLogWriter()
            {
                previousWriter = LogRuntime.Writer;
                if (!LogRuntime.TryReplaceWriter(previousWriter, this))
                {
                    throw new InvalidOperationException("The process log writer changed while the test scope was being installed.");
                }
            }

            public bool IsEnabled(LogSeverity severity, string category) => false;

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Dispose()
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
                ILogWriter writerToRestore = previousWriter;
                previousWriter = null;
                LogRuntime.TryReplaceWriter(this, writerToRestore);
            }
        }

        private sealed class ScopedOutOfMemoryLogWriter : ILogWriter, IDisposable
        {
            private readonly OutOfMemoryException failure = new OutOfMemoryException(
                "Logging out-of-memory failure requested by the test.");
            private ILogWriter previousWriter;
            private bool isDisposed;

            public ScopedOutOfMemoryLogWriter()
            {
                previousWriter = LogRuntime.Writer;
                if (!LogRuntime.TryReplaceWriter(previousWriter, this))
                {
                    throw new InvalidOperationException(
                        "The process log writer changed while the test scope was being installed.");
                }
            }

            public bool IsEnabled(LogSeverity severity, string category) => throw failure;

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw failure;

            public void Dispose()
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
                ILogWriter writerToRestore = previousWriter;
                previousWriter = null;
                LogRuntime.TryReplaceWriter(this, writerToRestore);
            }
        }
    }
}
