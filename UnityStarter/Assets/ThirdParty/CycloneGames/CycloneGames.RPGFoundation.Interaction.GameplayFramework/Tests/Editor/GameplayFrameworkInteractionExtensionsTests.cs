using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework;
using CycloneGames.RPGFoundation.Interaction.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Interaction.GameplayFramework.Tests.Editor
{
    public sealed class GameplayFrameworkInteractionExtensionsTests
    {
        private const ulong StableId = 42UL;

        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        [Test]
        public void TryGetInteractionPosition_UsesActorLocation()
        {
            Actor actor = CreateActor(new Vector3(2f, -3f, 5f));

            bool result = actor.TryGetInteractionPosition(out InteractionVector3 position);

            Assert.That(result, Is.True);
            Assert.That(position, Is.EqualTo(new InteractionVector3(2f, -3f, 5f)));
        }

        [Test]
        public void CreateInteractionInstigator_PreservesGameObjectAndStableIdentity()
        {
            Actor actor = CreateActor(Vector3.zero);

            GameObjectInstigator instigator = actor.CreateInteractionInstigator(StableId);

            Assert.That(instigator.GameObject, Is.SameAs(_gameObject));
            Assert.That(instigator.StableId, Is.EqualTo(StableId));
        }

        [Test]
        public void TryCreateInteractionTargetSnapshot_CopiesAuthorityValues()
        {
            Actor actor = CreateActor(new Vector3(7f, 1f, -4f));
            string[] enabledActions = { "open", "inspect" };

            bool result = actor.TryCreateInteractionTargetSnapshot(
                worldId: 8,
                targetStableId: StableId,
                interactionRange: 3.5f,
                snapshot: out InteractionTargetSnapshot snapshot,
                isAvailable: true,
                allowDefaultAction: false,
                enabledActionIds: enabledActions,
                version: 6);

            Assert.That(result, Is.True);
            Assert.That(snapshot.WorldId, Is.EqualTo(8));
            Assert.That(snapshot.TargetStableId, Is.EqualTo(StableId));
            Assert.That(snapshot.Position, Is.EqualTo(new InteractionVector3(7f, 1f, -4f)));
            Assert.That(snapshot.InteractionRange, Is.EqualTo(3.5f));
            Assert.That(snapshot.IsAvailable, Is.True);
            Assert.That(snapshot.AllowDefaultAction, Is.False);
            Assert.That(snapshot.EnabledActionIds, Is.SameAs(enabledActions));
            Assert.That(snapshot.Version, Is.EqualTo(6));
        }

        [Test]
        public void TryOperations_RejectDestroyedActor()
        {
            Actor actor = CreateActor(Vector3.zero);
            Object.DestroyImmediate(_gameObject);
            _gameObject = null;

            Assert.That(actor.TryGetInteractionPosition(out InteractionVector3 position), Is.False);
            Assert.That(position, Is.EqualTo(InteractionVector3.Zero));
            Assert.That(actor.TryCreateInteractionTargetSnapshot(
                worldId: 1,
                targetStableId: StableId,
                interactionRange: 1f,
                snapshot: out InteractionTargetSnapshot snapshot), Is.False);
            Assert.That(snapshot.IsValid, Is.False);
        }

        private Actor CreateActor(Vector3 position)
        {
            _gameObject = new GameObject("InteractionGameplayFrameworkTests_Actor");
            _gameObject.SetActive(false);
            _gameObject.transform.position = position;
            return _gameObject.AddComponent<Actor>();
        }
    }
}
