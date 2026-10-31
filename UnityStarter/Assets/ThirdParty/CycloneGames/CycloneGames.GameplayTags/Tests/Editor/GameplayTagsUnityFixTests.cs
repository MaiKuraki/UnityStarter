using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    public sealed class GameplayTagsUnityFixTests
    {
        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagManager.RegisterDynamicTags(new[]
            {
                "UnityFix.Persistent.Root.Child",
                "UnityFix.DI.Root.Child"
            });
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
        }

        [Test]
        public void GameObjectPersistentTags_LoadFromSerializedDataBeforeAwake()
        {
            var gameObject = new GameObject("UnityFix.Persistent");
            try
            {
                gameObject.SetActive(false);
                var component = gameObject.AddComponent<GameObjectGameplayTagContainer>();
                using (var serialized = new SerializedObject(component))
                {
                    SerializedProperty names = serialized
                        .FindProperty("m_PersistentTags")
                        .FindPropertyRelative("explicitTagNames");
                    names.arraySize = 1;
                    names.GetArrayElementAtIndex(0).stringValue = "UnityFix.Persistent.Root.Child";
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                gameObject.SetActive(true);
                GameplayTag tag = GameplayTagManager.Request("UnityFix.Persistent.Root.Child");
                Assert.That(component.GameplayTagContainer.GetExplicitTagCount(tag), Is.EqualTo(1));
                Assert.That(component.GameplayTagContainer.GetTagCount(tag.ParentTag), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SerializableContainer_DirectReadsAndQuery_ReresolveAfterIndexChange()
        {
            var bridge = new SerializableGameplayTagContainer();
            bridge.LoadPersisted(new[] { "UnityFix.Persistent.Root.Child" });
            GameplayTagManager.RegisterDynamicTag("Aaa.UnityFix");
            GameplayTagManager.Reload(false);
            GameplayTag current = GameplayTagManager.Request("UnityFix.Persistent.Root.Child");

            Assert.That(bridge.ContainsRuntimeIndex(current.RuntimeIndex, true), Is.True);
            Assert.That(bridge.GetExplicitTag(0).RuntimeIndex, Is.EqualTo(current.RuntimeIndex));
            Assert.That(bridge.GetTags().MoveNext(), Is.True);

            var required = new GameplayTagContainer();
            required.AddTag(current);
            var query = new GameplayTagQuery
            {
                RootExpression = GameplayTagQueryExpression.MatchAll(required)
            };
            Assert.That(query.Matches(bridge), Is.True);
        }

        [Test]
        public void SerializableCountContainer_BindAndReload_UseOwningRegistry()
        {
            var registry = new GameplayTagRegistryBuilder().Build();
            registry.RegisterDynamicTag("UnityFix.DI.Root.Child");
            var bridge = new SerializableGameplayTagCountContainer();
            bridge.LoadPersisted(
                new[] { "UnityFix.DI.Root.Child" },
                new[] { 3 });
            bridge.Bind(registry);

            GameplayTag tag = registry.Request("UnityFix.DI.Root.Child");
            GameplayTag parent = registry.GetParentTag(tag);
            Assert.That(bridge.Container.GetExplicitTagCount(tag), Is.EqualTo(3));
            Assert.That(bridge.Container.GetTagCount(parent), Is.EqualTo(3));

            registry.RegisterDynamicTag("Aaa.UnityFix.DI");
            registry.Reload(false);
            tag = registry.Request("UnityFix.DI.Root.Child");
            Assert.That(bridge.Container.GetExplicitTagCount(tag), Is.EqualTo(3));
            Assert.That(bridge.ExplicitTagCount, Is.EqualTo(1));
        }

        [Test]
        public void Container_CopyAndQuery_PreserveSourceDiRegistry()
        {
            var registry = new GameplayTagRegistryBuilder().Build();
            registry.RegisterDynamicTag("DIOnly.Root.Child");
            GameplayTag child = registry.Request("DIOnly.Root.Child");
            GameplayTag parent = registry.GetParentTag(child);
            GameplayTag root = registry.GetParentTag(parent);
            var source = new GameplayTagContainer(registry);
            source.AddTag(child);

            var copy = new GameplayTagContainer(source);
            var parents = new List<GameplayTag>();
            copy.GetParentTags(child, parents);
            CollectionAssert.AreEquivalent(new[] { parent, root }, parents);

            var required = new GameplayTagContainer(registry);
            required.AddTag(parent);
            var query = new GameplayTagQuery
            {
                RootExpression = GameplayTagQueryExpression.MatchAll(required)
            };
            Assert.That(query.Matches(copy), Is.True);
        }

        [Test]
        public void Container_CrossThreadMutation_IsRejectedInDevelopment()
        {
            GameplayTag tag = GameplayTagManager.Request("UnityFix.Persistent.Root.Child");
            var container = new GameplayTagContainer();
            container.AddTag(tag);
            GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = true;
            try
            {
                Exception error = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        container.AddTag(tag);
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                });
                thread.Start();
                thread.Join();
                Assert.That(error, Is.TypeOf<InvalidOperationException>());
                Assert.That(container.ExplicitTagCount, Is.EqualTo(1));
            }
            finally
            {
                GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = false;
            }
        }
    }
}
