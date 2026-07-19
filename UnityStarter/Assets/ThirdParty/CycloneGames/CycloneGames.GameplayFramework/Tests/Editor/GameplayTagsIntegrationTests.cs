using System;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.GameplayTags;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class GameplayTagsIntegrationTests
    {
        private static int tagSeed;
        private GameObject actorObject;

        [SetUp]
        public void SetUp()
        {
            actorObject = new GameObject("GameplayTagsIntegrationActor");
        }

        [TearDown]
        public void TearDown()
        {
            if (actorObject != null)
            {
                UnityEngine.Object.DestroyImmediate(actorObject);
            }
        }

        [Test]
        public void Helpers_ReturnFalse_WhenActorHasNoGameplayTagComponent()
        {
            Actor actor = actorObject.AddComponent<Actor>();

            Assert.IsFalse(actor.TryGetGameplayTagContainer(out GameplayTagCountContainer container));
            Assert.IsNull(container);
            Assert.IsFalse(actor.ActorHasGameplayTag(GameplayTag.None));
            Assert.IsFalse(actor.AddGameplayTag(GameplayTag.None));
            Assert.IsFalse(actor.RemoveGameplayTag(GameplayTag.None));
        }

        [Test]
        public void Helpers_AddRemoveAndExposeExactCountContainer()
        {
            GameplayTag tag = RegisterTag();
            Actor actor = actorObject.AddComponent<Actor>();
            GameObjectGameplayTagContainer component = actorObject.AddComponent<GameObjectGameplayTagContainer>();

            Assert.IsTrue(actor.TryGetGameplayTagContainer(out GameplayTagCountContainer container));
            Assert.AreSame(component.GameplayTagContainer, container);
            Assert.IsTrue(actor.AddGameplayTag(tag));
            Assert.IsTrue(actor.AddGameplayTag(tag));
            Assert.IsTrue(actor.ActorHasGameplayTag(tag));
            Assert.AreEqual(2, container.GetExplicitTagCount(tag));

            Assert.IsTrue(actor.RemoveGameplayTag(tag));
            Assert.AreEqual(1, container.GetExplicitTagCount(tag));
            Assert.IsTrue(actor.RemoveGameplayTag(tag));
            Assert.AreEqual(0, container.GetExplicitTagCount(tag));
            Assert.IsFalse(actor.ActorHasGameplayTag(tag));
        }

        [Test]
        public void Helpers_RejectInvalidTag_WhenContainerExists()
        {
            Actor actor = actorObject.AddComponent<Actor>();
            actorObject.AddComponent<GameObjectGameplayTagContainer>();

            Assert.Throws<ArgumentException>(() => actor.AddGameplayTag(GameplayTag.None));
            Assert.Throws<ArgumentException>(() => actor.RemoveGameplayTag(GameplayTag.None));
            Assert.IsFalse(actor.ActorHasGameplayTag(GameplayTag.None));
        }

        private static GameplayTag RegisterTag()
        {
            string prefix = "GameplayFrameworkIntegration" + Interlocked.Increment(ref tagSeed);
            string name = prefix + ".State.Active";
            GameplayTagManager.RegisterDynamicTag(name);
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }
    }
}
