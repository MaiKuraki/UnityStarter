using System.Linq;
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
            HandleTracker.Enabled = true;
            HandleTracker.EnableStackTrace = false;
            HandleTracker.Clear();
            SceneTracker.Enabled = true;
            SceneTracker.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            HandleTracker.Clear();
            SceneTracker.Clear();
            HandleTracker.Enabled = false;
            HandleTracker.EnableStackTrace = false;
            SceneTracker.Enabled = true;
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
        public void HandleTracker_Returns_Disabled_Report_When_Off()
        {
            HandleTracker.Enabled = false;

            Assert.AreEqual(0, HandleTracker.GetActiveHandleCount());
            Assert.AreEqual("Handle tracking is disabled.", HandleTracker.GetActiveHandlesReport());
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

            SceneTracker.Register(7, "default", "TestProvider", "Scenes/Main", "UI.Scene", LoadSceneMode.Additive, handle);
            SceneTracker.MarkUnloadRequested(7);

            var scenes = SceneTracker.GetTrackedScenes();
            Assert.AreEqual(1, scenes.Count);
            var info = scenes.Single();
            Assert.AreEqual(7, info.Id);
            Assert.AreEqual("default", info.PackageName);
            Assert.AreEqual("TestProvider", info.ProviderType);
            Assert.AreEqual("Scenes/Main", info.SceneLocation);
            Assert.AreEqual("UI.Scene", info.Bucket);
            Assert.AreEqual(LoadSceneMode.Additive, info.LoadMode);
            Assert.AreEqual("Assets/Scenes/Main.unity", info.ScenePath);
            Assert.AreEqual(SceneActivationMode.Manual, info.ActivationMode);
            Assert.AreEqual(SceneActivationState.WaitingForActivation, info.ActivationState);
            Assert.IsTrue(info.SupportsManualActivation);
            Assert.IsTrue(info.IsDone);
            Assert.IsTrue(info.UnloadRequested);
            Assert.AreEqual(1f, info.Progress);
            Assert.AreEqual(2, info.RefCount);
        }
    }
}
