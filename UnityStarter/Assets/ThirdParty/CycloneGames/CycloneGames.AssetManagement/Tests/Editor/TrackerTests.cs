using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using NUnit.Framework;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class TrackerTests
    {
        [SetUp]
        public void SetUp()
        {
            HandleTracker.Reset();
            HandleTracker.Enabled = true;
            HandleTracker.EnableStackTrace = false;
            SceneTracker.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            HandleTracker.Reset();
            SceneTracker.Reset();
        }

        [Test]
        public void HandleTracker_Tracks_Register_And_Unregister()
        {
            HandleTracker.Register(10, "default", "Assets/Icon.png");

            Assert.AreEqual(1, HandleTracker.GetActiveHandleCount());
            var handles = HandleTracker.GetActiveHandles();
            Assert.AreEqual(1, handles.Count);
            Assert.AreEqual(10, handles[0].Id);
            Assert.AreEqual("default", handles[0].PackageName);
            Assert.AreEqual("Assets/Icon.png", handles[0].Description);

            HandleTracker.Unregister(10);

            Assert.AreEqual(0, HandleTracker.GetActiveHandleCount());
        }

        [Test]
        public void HandleTracker_Bounded_Copy_Returns_Exact_Total_And_Capped_Rows()
        {
            HandleTracker.Register(1, "default", "Assets/A.asset");
            HandleTracker.Register(2, "default", "Assets/B.asset");
            HandleTracker.Register(3, "default", "Assets/C.asset");
            var destination = new List<HandleTracker.HandleInfo>();

            int total = HandleTracker.CopyActiveHandlesTo(destination, 2);

            Assert.AreEqual(3, total);
            Assert.AreEqual(2, destination.Count);

            total = HandleTracker.CopyActiveHandlesTo(destination, 0);

            Assert.AreEqual(3, total);
            Assert.IsEmpty(destination);
            Assert.Throws<ArgumentNullException>(() => HandleTracker.CopyActiveHandlesTo(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => HandleTracker.CopyActiveHandlesTo(destination, -1));
        }

        [Test]
        public void HandleTracker_Returns_Disabled_Report_When_Off()
        {
            HandleTracker.Enabled = false;

            Assert.AreEqual(0, HandleTracker.GetActiveHandleCount());
            Assert.AreEqual("Handle tracking is disabled.", HandleTracker.GetActiveHandlesReport());
        }

        [Test]
        public void HandleTracker_Rejects_Registrations_At_Configured_Capacity()
        {
            HandleTracker.Enabled = false;
            HandleTracker.ConfigureCapacity(2);
            HandleTracker.Enabled = true;

            HandleTracker.Register(1, "default", "Assets/A.asset");
            HandleTracker.Register(2, "default", "Assets/B.asset");
            HandleTracker.Register(3, "default", "Assets/C.asset");

            Assert.AreEqual(2, HandleTracker.GetActiveHandleCount());
            Assert.AreEqual(1L, HandleTracker.DroppedRegistrationCount);

            HandleTracker.Unregister(1);
            HandleTracker.Register(3, "default", "Assets/C.asset");

            Assert.AreEqual(2, HandleTracker.GetActiveHandleCount());
            Assert.AreEqual(1L, HandleTracker.DroppedRegistrationCount);
        }

        [Test]
        public void HandleTracker_Capacity_Remains_Bounded_Under_Concurrent_Registration()
        {
            const int capacity = 32;
            const int registrationCount = 256;
            HandleTracker.Enabled = false;
            HandleTracker.ConfigureCapacity(capacity);
            HandleTracker.Enabled = true;

            Parallel.For(
                0,
                registrationCount,
                index => HandleTracker.Register(index + 1L, "default", "Concurrent"));

            Assert.AreEqual(capacity, HandleTracker.GetActiveHandleCount());
            Assert.AreEqual(registrationCount - capacity, HandleTracker.DroppedRegistrationCount);
        }

        [Test]
        public void HandleTracker_Reset_Disables_Tracking_And_Restores_Defaults()
        {
            HandleTracker.Enabled = false;
            HandleTracker.ConfigureCapacity(1);
            HandleTracker.Enabled = true;
            HandleTracker.EnableStackTrace = true;
            HandleTracker.Register(1, "default", "Assets/A.asset");
            HandleTracker.MarkPersistent(1L);
            HandleTracker.Register(2, "default", "Assets/B.asset");

            HandleTracker.Reset();

            Assert.IsFalse(HandleTracker.Enabled);
            Assert.IsFalse(HandleTracker.EnableStackTrace);
            Assert.AreEqual(HandleTracker.DEFAULT_CAPACITY, HandleTracker.Capacity);
            Assert.AreEqual(0, HandleTracker.GetActiveHandleCount());
            Assert.AreEqual(0L, HandleTracker.DroppedRegistrationCount);
            Assert.IsFalse(HandleTracker.HasPersistentEntries);
            Assert.IsFalse(HandleTracker.ObservationIncomplete);
        }

        [Test]
        public void HandleTracker_Persistent_Marks_Are_Exact_Bounded_And_Released_With_Handle()
        {
            HandleTracker.MarkPersistent(999L);
            Assert.IsFalse(HandleTracker.HasPersistentEntries);

            HandleTracker.Register(1L, "package-a", "Assets/Shared.asset");
            HandleTracker.Register(2L, "package-b", "Assets/Shared.asset");
            HandleTracker.MarkPersistent(1L);

            Assert.IsTrue(HandleTracker.HasPersistentEntries);
            Assert.IsTrue(HandleTracker.IsPersistent(1L));
            Assert.IsFalse(HandleTracker.IsPersistent(2L));

            HandleTracker.Unregister(1L);

            Assert.IsFalse(HandleTracker.IsPersistent(1L));
            Assert.IsFalse(HandleTracker.HasPersistentEntries);
        }

        [Test]
        public void HandleTracker_Clear_And_Disable_Remove_Persistent_Marks()
        {
            HandleTracker.Register(1L, "default", "Assets/A.asset");
            HandleTracker.MarkPersistent(1L);

            HandleTracker.Clear();

            Assert.IsFalse(HandleTracker.HasPersistentEntries);
            Assert.IsTrue(HandleTracker.ObservationIncomplete);

            HandleTracker.Register(2L, "default", "Assets/B.asset");
            HandleTracker.MarkPersistent(2L);
            HandleTracker.Enabled = false;

            Assert.IsFalse(HandleTracker.HasPersistentEntries);
            Assert.IsTrue(HandleTracker.ObservationIncomplete);
        }

        [Test]
        [Timeout(5_000)]
        public void HandleTracker_Concurrent_Persistent_Lifecycle_Leaves_No_Stale_Marks()
        {
            Parallel.For(
                0,
                256,
                index =>
                {
                    long id = index + 1L;
                    HandleTracker.Register(id, "default", "Concurrent");
                    HandleTracker.MarkPersistent(id);
                    HandleTracker.Unregister(id);
                });

            Assert.AreEqual(0, HandleTracker.GetActiveHandleCount());
            Assert.IsFalse(HandleTracker.HasPersistentEntries);
        }

        [Test]
        public void HandleTracker_Reports_Incomplete_Observation_Epoch()
        {
            Assert.IsFalse(HandleTracker.ObservationIncomplete);

            HandleTracker.Clear();

            Assert.IsTrue(HandleTracker.ObservationIncomplete);
            Assert.AreEqual(
                "No tracked handles; the current observation epoch is incomplete.",
                HandleTracker.GetActiveHandlesReport());

            HandleTracker.Reset();
            HandleTracker.NotifyHandleCreated();
            HandleTracker.Enabled = true;

            Assert.IsTrue(HandleTracker.ObservationIncomplete);
        }

        [Test]
        public void AssetRuntimeGuard_Preserves_Handle_Identity_Across_Subsystem_Reset()
        {
            long first = AssetRuntimeGuard.NextHandleId();

            AssetRuntimeGuard.ResetStatics();
            long second = AssetRuntimeGuard.NextHandleId();

            Assert.Greater(second, first);
            Assert.IsTrue(HandleTracker.ObservationIncomplete);
        }

        [Test]
        public void HandleTracker_Rejects_Invalid_Or_Live_Capacity_Changes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HandleTracker.ConfigureCapacity(0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                HandleTracker.ConfigureCapacity(HandleTracker.MAX_CAPACITY + 1));
            Assert.Throws<InvalidOperationException>(() => HandleTracker.ConfigureCapacity(1));
        }

        [Test]
        public void HandleTracker_StackCaptureFailure_DoesNotBreakAssetRegistration()
        {
            HandleTracker.EnableStackTrace = true;
            HandleTracker.StackTraceCapture = () => throw new InvalidOperationException("Injected diagnostics failure.");

            Assert.DoesNotThrow(() =>
                HandleTracker.Register(1, "default", "Assets/A.asset"));

            var handles = HandleTracker.GetActiveHandles();
            Assert.AreEqual(1, handles.Count);
            Assert.IsNull(handles[0].StackTrace);
        }

        [Test]
        public void HandleTracker_NonRecoverableStackCaptureFailure_ReleasesCapacityReservation()
        {
            HandleTracker.Enabled = false;
            HandleTracker.ConfigureCapacity(1);
            HandleTracker.EnableStackTrace = true;
            HandleTracker.Enabled = true;
            HandleTracker.StackTraceCapture = () => throw new OutOfMemoryException("Injected diagnostics failure.");

            Assert.Throws<OutOfMemoryException>(() =>
                HandleTracker.Register(1, "default", "Assets/A.asset"));

            HandleTracker.StackTraceCapture = () => "captured";
            HandleTracker.Register(2, "default", "Assets/B.asset");

            Assert.AreEqual(1, HandleTracker.GetActiveHandleCount());
            Assert.AreEqual(0L, HandleTracker.DroppedRegistrationCount);
        }

        [Test]
        public void SceneTracker_Captures_Handle_Snapshot_And_Unload_State()
        {
            var handle = new TestSceneHandle
            {
                ScenePathValue = "Assets/Scenes/Main.unity",
                ActivationModeValue = SceneActivationMode.Manual,
                ActivationStateValue = SceneActivationState.WaitingForActivation,
                SupportsManualActivationValue = true,
                IsDoneValue = true,
                ProgressValue = 1f,
                RefCountValue = 2
            };

            var loadParameters = new LoadSceneParameters(LoadSceneMode.Additive)
            {
                localPhysicsMode = LocalPhysicsMode.Physics2D | LocalPhysicsMode.Physics3D
            };
            SceneTracker.Register(7, "default", "TestProvider", "Scenes/Main", "UI.Scene", loadParameters, handle);
            SceneTracker.MarkUnloadRequested(7);

            var scenes = new List<SceneTracker.SceneInfo>();
            int total = SceneTracker.CopyTrackedScenesTo(scenes, SceneTracker.Capacity);
            Assert.AreEqual(1, total);
            Assert.AreEqual(1, scenes.Count);
            var info = scenes.Single();
            Assert.AreEqual(7, info.Id);
            Assert.AreEqual("default", info.PackageName);
            Assert.AreEqual("TestProvider", info.ProviderType);
            Assert.AreEqual("Scenes/Main", info.SceneLocation);
            Assert.AreEqual("UI.Scene", info.Bucket);
            Assert.AreEqual(LoadSceneMode.Additive, info.LoadMode);
            Assert.AreEqual(
                LocalPhysicsMode.Physics2D | LocalPhysicsMode.Physics3D,
                info.LocalPhysicsMode);
            Assert.AreEqual("Assets/Scenes/Main.unity", info.ScenePath);
            Assert.AreEqual(SceneActivationMode.Manual, info.ActivationMode);
            Assert.AreEqual(SceneActivationState.WaitingForActivation, info.ActivationState);
            Assert.IsTrue(info.SupportsManualActivation);
            Assert.IsTrue(info.IsDone);
            Assert.IsTrue(info.UnloadRequested);
            Assert.IsTrue(info.UnloadRequestedTimeUtc.HasValue);
            Assert.AreEqual(1f, info.Progress);
            Assert.AreEqual(2, info.RefCount);
        }

        [Test]
        public void SceneTracker_Removes_Released_Handle_Before_Reading_Snapshot()
        {
            var handle = new TestSceneHandle
            {
                ShouldRemoveFromSceneTrackerValue = true
            };

            SceneTracker.Register(8, "default", "TestProvider", "Scenes/Released", null, LoadSceneMode.Single, handle);

            var scenes = new List<SceneTracker.SceneInfo>();
            int total = SceneTracker.CopyTrackedScenesTo(scenes, SceneTracker.Capacity);

            Assert.AreEqual(0, total);
            Assert.IsEmpty(scenes);
            Assert.AreEqual(0, SceneTracker.GetTrackedSceneCount());
        }

        [Test]
        public void SceneTracker_Rejects_Registrations_At_Configured_Capacity()
        {
            SceneTracker.Enabled = false;
            SceneTracker.ConfigureCapacity(1);
            SceneTracker.Enabled = true;
            var firstHandle = new TestSceneHandle();
            var secondHandle = new TestSceneHandle();

            SceneTracker.Register(
                1,
                "default",
                "TestProvider",
                "Scenes/A",
                null,
                LoadSceneMode.Additive,
                firstHandle);
            SceneTracker.Register(
                2,
                "default",
                "TestProvider",
                "Scenes/B",
                null,
                LoadSceneMode.Additive,
                secondHandle);

            Assert.AreEqual(1, SceneTracker.GetTrackedSceneCount());
            Assert.AreEqual(1L, SceneTracker.DroppedRegistrationCount);
        }

        [Test]
        public void SceneTracker_Reset_Restores_Diagnostics_Bounds()
        {
            SceneTracker.Enabled = false;
            SceneTracker.ConfigureCapacity(1);
            SceneTracker.Enabled = true;
            var firstHandle = new TestSceneHandle();
            var secondHandle = new TestSceneHandle();
            SceneTracker.Register(
                1,
                "default",
                "TestProvider",
                "Scenes/A",
                null,
                LoadSceneMode.Additive,
                firstHandle);
            SceneTracker.Register(
                2,
                "default",
                "TestProvider",
                "Scenes/B",
                null,
                LoadSceneMode.Additive,
                secondHandle);

            SceneTracker.Reset();

            Assert.IsTrue(SceneTracker.Enabled);
            Assert.AreEqual(SceneTracker.DEFAULT_CAPACITY, SceneTracker.Capacity);
            Assert.AreEqual(0L, SceneTracker.DroppedRegistrationCount);
            Assert.AreEqual(0, SceneTracker.GetTrackedSceneCount());
        }

        [Test]
        public void SceneTracker_Clear_And_Disable_Mark_Observation_Incomplete()
        {
            SceneTracker.Clear();
            Assert.IsTrue(SceneTracker.ObservationIncomplete);

            SceneTracker.Reset();
            SceneTracker.Enabled = false;
            SceneTracker.Register(
                1L,
                "default",
                "TestProvider",
                "Scenes/A",
                null,
                LoadSceneMode.Additive,
                new TestSceneHandle());

            Assert.IsTrue(SceneTracker.ObservationIncomplete);
        }

        [Test]
        public void SceneTracker_CapacityDrop_Marks_Observation_Incomplete()
        {
            SceneTracker.Enabled = false;
            SceneTracker.ConfigureCapacity(1);
            SceneTracker.Enabled = true;
            var firstHandle = new TestSceneHandle();
            var secondHandle = new TestSceneHandle();
            SceneTracker.Register(
                81L,
                "default",
                "TestProvider",
                "Scenes/A",
                null,
                LoadSceneMode.Additive,
                firstHandle);
            SceneTracker.Register(
                82L,
                "default",
                "TestProvider",
                "Scenes/B",
                null,
                LoadSceneMode.Additive,
                secondHandle);

            Assert.IsTrue(SceneTracker.ObservationIncomplete);
            Assert.AreEqual(1L, SceneTracker.DroppedRegistrationCount);
            GC.KeepAlive(firstHandle);
            GC.KeepAlive(secondHandle);
        }

        [Test]
        public void Subsystem_Reset_Preserves_Previous_Incomplete_Epoch()
        {
            SceneTracker.Clear();

            AssetRuntimeGuard.ResetStatics();

            Assert.IsTrue(SceneTracker.ObservationIncomplete);
        }

        [Test]
        public void Subsystem_Reset_With_Tracked_Scene_Marks_New_Epoch_Incomplete()
        {
            var handle = new TestSceneHandle();
            SceneTracker.Register(
                91L,
                "default",
                "TestProvider",
                "Scenes/Survivor",
                null,
                LoadSceneMode.Additive,
                handle);

            AssetRuntimeGuard.ResetStatics();

            Assert.IsTrue(SceneTracker.ObservationIncomplete);
            Assert.AreEqual(0, SceneTracker.GetTrackedSceneCount());
            GC.KeepAlive(handle);
        }

        [Test]
        public void SceneTracker_Age_Uses_Monotonic_Time()
        {
            long timestamp = System.Diagnostics.Stopwatch.Frequency;
            SceneTracker.Reset();
            SceneTracker.MonotonicTimestampProvider = () => timestamp;
            var handle = new TestSceneHandle();
            SceneTracker.Register(
                92L,
                "default",
                "TestProvider",
                "Scenes/Timed",
                null,
                LoadSceneMode.Additive,
                handle);
            SceneTracker.MarkUnloadRequested(92L);

            timestamp += System.Diagnostics.Stopwatch.Frequency * 90L;
            var scenes = new List<SceneTracker.SceneInfo>();
            SceneTracker.CopyTrackedScenesTo(scenes, SceneTracker.Capacity);
            SceneTracker.SceneInfo info = scenes.Single();

            Assert.AreEqual(90d, SceneTracker.GetAgeSeconds(info.RegistrationTimestamp, timestamp), 0.001d);
            Assert.AreEqual(90d, SceneTracker.GetAgeSeconds(info.UnloadRequestedTimestamp, timestamp), 0.001d);
            GC.KeepAlive(handle);
        }

        [Test]
        public void SceneTracker_Bounded_Copy_Returns_Exact_Total_And_CallerOwned_Rows()
        {
            var handles = new[]
            {
                new TestSceneHandle(),
                new TestSceneHandle(),
                new TestSceneHandle()
            };
            for (int i = 0; i < handles.Length; i++)
            {
                SceneTracker.Register(
                    i + 1L,
                    "default",
                    "TestProvider",
                    $"Scenes/{i}",
                    null,
                    LoadSceneMode.Additive,
                    handles[i]);
            }

            var destination = new List<SceneTracker.SceneInfo>();
            int total = SceneTracker.CopyTrackedScenesTo(destination, 2);

            Assert.AreEqual(3, total);
            Assert.AreEqual(2, destination.Count);

            destination.Clear();
            Assert.AreEqual(3, SceneTracker.GetTrackedSceneCount());
            Assert.Throws<ArgumentNullException>(() => SceneTracker.CopyTrackedScenesTo(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SceneTracker.CopyTrackedScenesTo(destination, -1));
            GC.KeepAlive(handles);
        }

        [Test]
        public void SceneTracker_Entry_Uses_Weak_Handle_Observation()
        {
            var handle = new TestSceneHandle();
            SceneTracker.Register(
                94L,
                "default",
                "TestProvider",
                "Scenes/Observed",
                null,
                LoadSceneMode.Additive,
                handle);

            WeakReference<ISceneHandle> reference = GetTrackedSceneHandleReference(94L, out Type entryType);
            FieldInfo[] entryFields = entryType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsFalse(
                entryFields.Any(field => typeof(ISceneHandle).IsAssignableFrom(field.FieldType)),
                "SceneTracker entries must not contain a direct strong ISceneHandle field.");
            Assert.IsTrue(reference.TryGetTarget(out ISceneHandle observedHandle));
            Assert.AreSame(handle, observedHandle);
            GC.KeepAlive(handle);
        }

        [Test]
        public void SceneTracker_Prunes_Expired_Weak_Observation()
        {
            var handle = new TestSceneHandle();
            SceneTracker.Register(
                96L,
                "default",
                "TestProvider",
                "Scenes/ExpiredObservation",
                null,
                LoadSceneMode.Additive,
                handle);
            WeakReference<ISceneHandle> reference = GetTrackedSceneHandleReference(96L, out _);

            reference.SetTarget(null);

            var destination = new List<SceneTracker.SceneInfo>();
            int total = SceneTracker.CopyTrackedScenesTo(destination, SceneTracker.Capacity);

            Assert.IsFalse(reference.TryGetTarget(out _));
            Assert.AreEqual(0, total);
            Assert.IsEmpty(destination);
            Assert.AreEqual(0, SceneTracker.GetTrackedSceneCount());
            Assert.IsTrue(SceneTracker.ObservationIncomplete);
            GC.KeepAlive(handle);
        }

        [Test]
        public void SceneTracker_Unload_Failure_Ends_Pending_State_And_Reports_Error()
        {
            var handle = new TestSceneHandle();
            SceneTracker.Register(
                93L,
                "default",
                "TestProvider",
                "Scenes/Retry",
                null,
                LoadSceneMode.Additive,
                handle);
            SceneTracker.MarkUnloadRequested(93L);
            SceneTracker.MarkUnloadFailed(93L, "Injected unload failure.");

            var destination = new List<SceneTracker.SceneInfo>();
            SceneTracker.CopyTrackedScenesTo(destination, SceneTracker.Capacity);
            SceneTracker.SceneInfo info = destination.Single();

            Assert.IsFalse(info.UnloadRequested);
            Assert.IsFalse(info.UnloadRequestedTimeUtc.HasValue);
            Assert.AreEqual("Injected unload failure.", info.Error);

            SceneTracker.MarkUnloadRequested(93L);
            SceneTracker.CopyTrackedScenesTo(destination, SceneTracker.Capacity);
            info = destination.Single();
            Assert.IsTrue(info.UnloadRequested);
            Assert.IsEmpty(info.Error);
        }

        [Test]
        public void SceneTracker_Preserves_Unload_State_When_Provider_Getter_Fails()
        {
            var handle = new ThrowingSceneHandle();
            SceneTracker.Register(
                95L,
                "default",
                "TestProvider",
                "Scenes/Throwing",
                null,
                LoadSceneMode.Additive,
                handle);
            SceneTracker.MarkUnloadRequested(95L);

            var destination = new List<SceneTracker.SceneInfo>();
            SceneTracker.CopyTrackedScenesTo(destination, SceneTracker.Capacity);
            SceneTracker.SceneInfo info = destination.Single();

            Assert.IsTrue(info.UnloadRequested);
            Assert.IsTrue(info.UnloadRequestedTimeUtc.HasValue);
            Assert.AreEqual("Synthetic provider getter failure.", info.Error);
            GC.KeepAlive(handle);
        }

        private static WeakReference<ISceneHandle> GetTrackedSceneHandleReference(
            long id,
            out Type entryType)
        {
            FieldInfo entriesField = typeof(SceneTracker).GetField(
                "_trackedScenes",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(entriesField, "SceneTracker tracked-scene storage was not found.");

            var entries = entriesField.GetValue(null) as IDictionary;
            Assert.NotNull(entries, "SceneTracker tracked-scene storage must implement IDictionary.");
            Assert.IsTrue(entries.Contains(id), $"SceneTracker entry {id} was not registered.");

            object entry = entries[id];
            Assert.NotNull(entry, $"SceneTracker entry {id} was null.");
            entryType = entry.GetType();

            FieldInfo handleField = entryType.GetField(
                "Handle",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(handleField, "SceneTracker entry weak-handle field was not found.");
            Assert.AreEqual(
                typeof(WeakReference<ISceneHandle>),
                handleField.FieldType,
                "SceneTracker entries must store scene handles through WeakReference<ISceneHandle>.");

            var reference = handleField.GetValue(entry) as WeakReference<ISceneHandle>;
            Assert.NotNull(reference, "SceneTracker entry weak-handle reference was null.");
            return reference;
        }

        private sealed class ThrowingSceneHandle : ISceneHandle
        {
            public bool IsDone => false;
            public float Progress => 0f;
            public string Error => string.Empty;
            public Cysharp.Threading.Tasks.UniTask Task => Cysharp.Threading.Tasks.UniTask.CompletedTask;
            public string ScenePath => throw new InvalidOperationException("Synthetic provider getter failure.");
            public Scene Scene => default;
            public SceneActivationMode ActivationMode => SceneActivationMode.ActivateOnLoad;
            public SceneActivationState ActivationState => SceneActivationState.Loading;
            public bool SupportsManualActivation => false;

            public Cysharp.Threading.Tasks.UniTask ActivateAsync(
                System.Threading.CancellationToken cancellationToken = default) =>
                Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public void WaitForAsyncComplete()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
