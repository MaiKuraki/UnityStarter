using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class GameplayWorldHostTests
    {
        [Test]
        public void Host_StartAndStop_OwnsOneGameInstanceAndWorld()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            AssignWorldSettings(host, testWorld.Settings);

            try
            {
                World world = host.StartWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                Assert.AreSame(world, host.CurrentWorld);
                Assert.IsNotNull(host.GameInstance);
                Assert.Greater(world.OwnedActorCount, 0);

                world.Dispose();
                Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                Assert.IsNull(host.CurrentWorld);

                World replacementWorld = host.StartWorldAsync().GetAwaiter().GetResult();
                Assert.AreNotSame(world, replacementWorld);
                Assert.AreEqual(GameplayWorldHostState.Running, host.State);

                host.StopWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                Assert.IsNull(host.CurrentWorld);
                Assert.IsNull(host.GameInstance);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void WorldActorRegistration_IsAllocationFreeIndexedReadModel()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            World world = testWorld.World;
            int ownedCount = 0;

            for (int i = 0; i < world.ActorCount; i++)
            {
                Assert.IsTrue(world.TryGetActorRegistration(i, out WorldActorRegistration registration));
                Assert.IsNotNull(registration.Actor);
                if (registration.IsWorldOwned)
                {
                    ownedCount++;
                }
            }

            Assert.AreEqual(world.OwnedActorCount, ownedCount);
            Assert.IsFalse(world.TryGetActorRegistration(-1, out _));
            Assert.IsFalse(world.TryGetActorRegistration(world.ActorCount, out _));
        }

        [Test]
        public void EditorTools_ExposeHostInspectorAndConfigurationErrors()
        {
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            WorldSettings settings = ScriptableObject.CreateInstance<WorldSettings>();
            UnityEditor.Editor hostEditor = null;
            try
            {
                hostEditor = UnityEditor.Editor.CreateEditor(host);
                Assert.IsInstanceOf<GameplayWorldHostEditor>(hostEditor);

                var issues = new List<GameplayFrameworkValidationIssue>();
                GameplayFrameworkProjectValidator.ValidateWorldSettings(settings, issues);

                Assert.AreEqual(4, issues.Count);
                Assert.IsTrue(issues.TrueForAll(
                    issue => issue.Severity == GameplayFrameworkValidationSeverity.Error));
            }
            finally
            {
                Object.DestroyImmediate(hostEditor);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(hostObject);
            }
        }

        private static void AssignWorldSettings(GameplayWorldHost host, WorldSettings settings)
        {
            var serializedHost = new SerializedObject(host);
            serializedHost.FindProperty("worldSettings").objectReferenceValue = settings;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
