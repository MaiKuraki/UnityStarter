using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;
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
        public void PushCameraMode_ReturnsResultAndRejectsDuplicateInstance()
        {
            CameraContext context = new CameraContext(null, 2);
            TestCameraMode mode = new TestCameraMode();

            Assert.IsTrue(context.PushCameraMode(mode));
            Assert.IsFalse(context.PushCameraMode(mode));
            Assert.AreEqual(1, context.CameraModeCount);
            Assert.AreEqual(1, mode.ActivateCount);
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
            bool previousIgnoreState = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                CameraContext context = new CameraContext(null, 2);
                var mode = new ThrowingCameraMode(throwOnActivate: true, throwOnDeactivate: false);

                Assert.IsFalse(context.TryPushCameraMode(mode));
                Assert.AreEqual(0, context.CameraModeCount);
                Assert.IsNull(context.GetPrimaryCameraMode());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreState;
            }
        }

        [Test]
        public void Clear_ContinuesAfterModeDeactivationFailure()
        {
            bool previousIgnoreState = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                CameraContext context = new CameraContext(null, 2);
                var baseMode = new TestCameraMode();
                var first = new TestCameraMode();
                var throwing = new ThrowingCameraMode(throwOnActivate: false, throwOnDeactivate: true);
                context.SetBaseCameraMode(baseMode);
                context.TryPushCameraMode(first);
                context.TryPushCameraMode(throwing);

                context.Clear();

                Assert.AreEqual(0, context.CameraModeCount);
                Assert.IsNull(context.BaseCameraMode);
                Assert.AreEqual(1, first.DeactivateCount);
                Assert.AreEqual(1, baseMode.DeactivateCount);
                Assert.AreEqual(1, throwing.DeactivateCount);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreState;
            }
        }

        [Test]
        public void CameraActionBinding_RejectsActiveActionOverflow()
        {
            PlayerController owner = CreateOwner();
            CameraActionBinding binding = ownerObject.AddComponent<CameraActionBinding>();
            CameraActionPreset preset = CreatePreset();
            SetPrivateField(binding, "maxActiveActions", 1);

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
            CameraActionPreset preset = CreatePreset();
            SetPrivateField(binding, "maxPooledModes", 1);

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
            CameraActionPreset preset = CreatePreset();
            SetPrivateField(binding, "maxActiveActions", 2);
            SetPrivateField(binding, "maxPooledModes", 1);

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

        private CameraActionPreset CreatePreset()
        {
            CameraActionPreset preset = ScriptableObject.CreateInstance<CameraActionPreset>();
            scriptableObjects.Add(preset);
            return preset;
        }

        private static void SetPrivateField(CameraActionBinding binding, string fieldName, object value)
        {
            FieldInfo field = typeof(CameraActionBinding).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing field: {fieldName}");
            field.SetValue(binding, value);
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
    }
}
