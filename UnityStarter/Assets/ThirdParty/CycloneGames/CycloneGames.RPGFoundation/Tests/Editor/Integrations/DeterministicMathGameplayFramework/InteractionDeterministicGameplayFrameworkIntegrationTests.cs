using CycloneGames.DeterministicMath;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using CycloneGames.RPGFoundation.Runtime.Interaction.Integrations.DeterministicMath;
using CycloneGames.RPGFoundation.Runtime.Interaction.Integrations.DeterministicMath.GameplayFramework;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Tests.Editor.Integrations.DeterministicMathGameplayFramework
{
    public sealed class InteractionDeterministicGameplayFrameworkIntegrationTests
    {
        private const int WORLD_ID = 17;
        private const ulong TARGET_ID = 3001UL;
        private const ulong INSTIGATOR_ID = 4001UL;

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
        public void TryCreateDeterministicTargetSnapshot_UsesExplicitProvider()
        {
            Actor actor = CreateActor();
            var provider = _gameObject.AddComponent<TestDeterministicPositionProvider>();
            provider.Position = new FPVector3(FPInt64.FromInt(2), FPInt64.Zero, FPInt64.FromInt(5));

            bool result = actor.TryCreateDeterministicInteractionTargetSnapshot(
                provider,
                WORLD_ID,
                TARGET_ID,
                FPInt64.FromInt(4),
                out InteractionDeterministicTargetSnapshot snapshot);

            Assert.That(result, Is.True);
            Assert.That(snapshot.WorldId, Is.EqualTo(WORLD_ID));
            Assert.That(snapshot.TargetStableId, Is.EqualTo(TARGET_ID));
            Assert.That(snapshot.Position, Is.EqualTo(provider.Position));
        }

        [Test]
        public void TryCreateDeterministicTargetSnapshot_RejectsMissingProvider()
        {
            Actor actor = CreateActor();

            bool result = actor.TryCreateDeterministicInteractionTargetSnapshot(
                null,
                WORLD_ID,
                TARGET_ID,
                FPInt64.FromInt(4),
                out InteractionDeterministicTargetSnapshot snapshot);

            Assert.That(result, Is.False);
            Assert.That(snapshot.IsValid, Is.False);
        }

        [Test]
        public void TryCreateDeterministicRequestPayload_UsesRawFixedProvider()
        {
            Actor actor = CreateActor();
            var provider = _gameObject.AddComponent<TestDeterministicPositionProvider>();
            provider.Position = new FPVector3(FPInt64.FromString("1.5"), FPInt64.Zero, FPInt64.FromString("-3.25"));

            bool result = actor.TryCreateDeterministicInteractionRequestPayload(
                provider,
                9,
                INSTIGATOR_ID,
                TARGET_ID,
                "open",
                120,
                WORLD_ID,
                out InteractionDeterministicRequestPayload payload);

            Assert.That(result, Is.True);
            Assert.That(payload.IsValid, Is.True);
            Assert.That(payload.InstigatorPosition.ToFPVector3(), Is.EqualTo(provider.Position));
        }

        private Actor CreateActor()
        {
            _gameObject = new GameObject("InteractionDeterministicGameplayFrameworkTests_Actor");
            _gameObject.SetActive(false);
            return _gameObject.AddComponent<Actor>();
        }

        private sealed class TestDeterministicPositionProvider : MonoBehaviour, IInteractionDeterministicPositionProvider
        {
            public FPVector3 Position { get; set; }

            public bool TryGetDeterministicInteractionPosition(out FPVector3 position)
            {
                position = Position;
                return true;
            }
        }
    }
}
