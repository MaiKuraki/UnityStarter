using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Foundation2D.Tests.Support;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Foundation2D.Runtime.Tests
{
    internal sealed class BurstTestSpriteSequenceRenderer : MonoBehaviour, ISpriteSequenceRenderer
    {
        internal int LastAppliedFrame { get; private set; } = -1;
        internal int ApplyCount { get; private set; }

        public void Initialize(IReadOnlyList<Sprite> frames)
        {
        }

        public void ApplyFrame(int frameIndex, bool forceRefresh)
        {
            LastAppliedFrame = frameIndex;
            ApplyCount++;
        }

        public void SetVisible(bool visible)
        {
        }
    }

    public sealed class SpriteSequenceBurstManagerPlayModeTests
    {
        [UnityTest]
        public IEnumerator Controller_HasOneBatchOwner_AndCanTransferAfterUnregister()
        {
            var controllerObject = new GameObject("Foundation2D.Owner.Controller");
            controllerObject.SetActive(false);
            SpriteSequenceController controller = controllerObject.AddComponent<SpriteSequenceController>();
            var firstObject = new GameObject("Foundation2D.Owner.First");
            var secondObject = new GameObject("Foundation2D.Owner.Second");
            SpriteSequenceBurstManager first = firstObject.AddComponent<SpriteSequenceBurstManager>();
            SpriteSequenceBurstManager second = secondObject.AddComponent<SpriteSequenceBurstManager>();

            Assert.That(first.RegisterController(controller, out bool registrationAdded), Is.True);
            Assert.That(registrationAdded, Is.True);
            Assert.That(first.OwnedControllerCount, Is.EqualTo(1));

            Assert.That(first.RegisterController(controller, out registrationAdded), Is.True);
            Assert.That(registrationAdded, Is.False);

            RecordedLogEntry[] entries;
            using (var logs = new RecordingLogWriterScope("CycloneGames.Foundation2D.Burst"))
            {
                Assert.That(second.RegisterController(controller, out registrationAdded), Is.False);
                Assert.That(registrationAdded, Is.False);
                Assert.That(second.OwnedControllerCount, Is.Zero);
                entries = logs.Snapshot();
            }

            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(LogSeverity.Error));
            Assert.That(entries[0].Category, Is.EqualTo("CycloneGames.Foundation2D.Burst"));
            Assert.That(
                entries[0].Message,
                Is.EqualTo(
                    "SpriteSequenceController 'Foundation2D.Owner.Controller' is already owned by another active " +
                    "SpriteSequenceBurstManager. Manager 'Foundation2D.Owner.Second' will not update it."));
            Assert.That(entries[0].Exception, Is.Null);

            Assert.That(first.UnregisterControllers(new[] { controller }), Is.EqualTo(1));
            Assert.That(first.OwnedControllerCount, Is.Zero);
            Assert.That(second.RegisterController(controller), Is.True);
            Assert.That(second.OwnedControllerCount, Is.EqualTo(1));

            Object.Destroy(firstObject);
            Object.Destroy(secondObject);
            Object.Destroy(controllerObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Disable_ReleasesOwnershipAndPersistentBuffers()
        {
            var managerObject = new GameObject("Foundation2D.Buffer.Manager");
            managerObject.SetActive(false);
            SpriteSequenceBurstManager manager = managerObject.AddComponent<SpriteSequenceBurstManager>();
            var controllerObject = new GameObject("Foundation2D.Buffer.Controller");
            controllerObject.transform.SetParent(managerObject.transform, false);
            controllerObject.SetActive(false);
            controllerObject.AddComponent<SpriteSequenceController>();
            SetPrivateField(manager, "prewarmCapacity", 8);
            managerObject.SetActive(true);
            yield return null;

            Assert.That(manager.BufferCapacity, Is.GreaterThanOrEqualTo(8));
            Assert.That(manager.OwnedControllerCount, Is.EqualTo(1));

            managerObject.SetActive(false);
            Assert.That(manager.BufferCapacity, Is.Zero);
            Assert.That(manager.OwnedControllerCount, Is.Zero);

            manager.RefreshControllers();
            Assert.That(manager.OwnedControllerCount, Is.Zero);

            Object.Destroy(managerObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Snapshot_ReportsBoundedAdmissionAndDisableDisposesBuffers()
        {
            var managerObject = new GameObject("Foundation2D.Memory.Manager");
            managerObject.SetActive(false);
            SpriteSequenceBurstManager manager = managerObject.AddComponent<SpriteSequenceBurstManager>();
            SetPrivateField(manager, "autoCollectChildren", false);
            SetPrivateField(manager, "prewarmCapacity", 1);
            SetPrivateField(manager, "maxControllerCapacity", 1);
            managerObject.SetActive(true);

            var firstObject = new GameObject("Foundation2D.Memory.First");
            firstObject.SetActive(false);
            SpriteSequenceController first = firstObject.AddComponent<SpriteSequenceController>();
            var rejectedObject = new GameObject("Foundation2D.Memory.Rejected");
            rejectedObject.SetActive(false);
            SpriteSequenceController rejected = rejectedObject.AddComponent<SpriteSequenceController>();

            Assert.That(manager.RegisterController(first), Is.True);
            RecordedLogEntry[] entries;
            using (var logs = new RecordingLogWriterScope("CycloneGames.Foundation2D.Burst"))
            {
                Assert.That(manager.RegisterController(rejected), Is.False);
                entries = logs.Snapshot();
            }

            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(LogSeverity.Warning));
            Assert.That(entries[0].Category, Is.EqualTo("CycloneGames.Foundation2D.Burst"));
            Assert.That(
                entries[0].Message,
                Is.EqualTo(
                    "SpriteSequenceBurstManager 'Foundation2D.Memory.Manager' reached maxControllerCapacity=1. " +
                    "Additional controllers remain unclaimed and use their fallback policy."));
            Assert.That(entries[0].Exception, Is.Null);

            SpriteSequenceBurstMemorySnapshot snapshot = manager.GetMemorySnapshot();
            Assert.That(snapshot.OwnedControllerCount, Is.EqualTo(1));
            Assert.That(snapshot.RuntimeRegistrationCount, Is.EqualTo(1));
            Assert.That(snapshot.BufferCapacity, Is.EqualTo(1));
            Assert.That(snapshot.MaximumControllerCapacity, Is.EqualTo(1));
            Assert.That(snapshot.PeakOwnedControllerCount, Is.EqualTo(1));
            Assert.That(snapshot.PeakBufferCapacity, Is.EqualTo(1));
            Assert.That(snapshot.CapacityRejectionCount, Is.EqualTo(1));
            Assert.That(snapshot.IsJobScheduled, Is.False);

            managerObject.SetActive(false);
            snapshot = manager.GetMemorySnapshot();
            Assert.That(snapshot.OwnedControllerCount, Is.Zero);
            Assert.That(snapshot.BufferCapacity, Is.Zero);
            Assert.That(snapshot.IsJobScheduled, Is.False);

            Object.Destroy(managerObject);
            Object.Destroy(firstObject);
            Object.Destroy(rejectedObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScheduledBatch_AdvancesControllerAndCommitsRenderer()
        {
            var managerObject = new GameObject("Foundation2D.Batch.Manager");
            managerObject.SetActive(false);
            SpriteSequenceBurstManager manager = managerObject.AddComponent<SpriteSequenceBurstManager>();
            SetPrivateField(manager, "autoCollectChildren", false);
            SetPrivateField(manager, "prewarmCapacity", 1);
            SetPrivateField(manager, "minParallelJobCount", 1);
            SetPrivateField(manager, "jobBatchSize", 1);
            managerObject.SetActive(true);

            var controllerObject = new GameObject("Foundation2D.Batch.Controller");
            controllerObject.SetActive(false);
            BurstTestSpriteSequenceRenderer renderer = controllerObject.AddComponent<BurstTestSpriteSequenceRenderer>();
            SpriteSequenceController controller = controllerObject.AddComponent<SpriteSequenceController>();
            var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
            Sprite first = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
            Sprite second = Sprite.Create(texture, new Rect(16f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
            var frames = new List<Sprite> { first, second };

            SetPrivateField(controller, "frames", frames);
            SetPrivateField(controller, "rendererComponent", renderer);
            SetPrivateField(controller, "frameRate", 1000f);
            SetPrivateField(controller, "playMode", SpriteSequenceController.PlayMode.Once);
            controller.SetUpdateDriver(SpriteSequenceController.UpdateDriver.BurstManaged);
            controllerObject.SetActive(true);
            yield return null;

            Assert.That(manager.RegisterController(controller), Is.True);
            controller.Play();
            int initialFrame = controller.CurrentFrame;
            int initialApplyCount = renderer.ApplyCount;

            for (int i = 0; i < 60 && controller.CurrentFrame == initialFrame; i++)
            {
                yield return null;
            }

            Assert.That(controller.CurrentFrame, Is.Not.EqualTo(initialFrame));
            Assert.That(renderer.ApplyCount, Is.GreaterThan(initialApplyCount));
            Assert.That(renderer.LastAppliedFrame, Is.EqualTo(controller.CurrentFrame));

            Object.Destroy(managerObject);
            Object.Destroy(controllerObject);
            Object.Destroy(first);
            Object.Destroy(second);
            Object.Destroy(texture);
            yield return null;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test field: {fieldName}");
            field.SetValue(target, value);
        }
    }
}
