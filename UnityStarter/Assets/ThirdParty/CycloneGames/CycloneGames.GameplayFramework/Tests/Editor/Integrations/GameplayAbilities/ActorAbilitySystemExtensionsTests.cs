using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor
{
    public sealed class ActorAbilitySystemExtensionsTests
    {
        private GameObject actorObject;
        private AbilitySystemComponent abilitySystem;

        [TearDown]
        public void TearDown()
        {
            abilitySystem?.Dispose();
            abilitySystem = null;

            if (actorObject != null)
            {
                Object.DestroyImmediate(actorObject);
                actorObject = null;
            }
        }

        [Test]
        public void ComponentProvider_IsResolvedAndInitializesActorInfo()
        {
            actorObject = new GameObject("AbilityActor");
            Actor actor = actorObject.AddComponent<Actor>();
            AbilitySystemProvider provider = actorObject.AddComponent<AbilitySystemProvider>();
            abilitySystem = new AbilitySystemComponent();
            provider.Initialize(abilitySystem);

            Assert.IsTrue(actor.TryGetAbilitySystem(out AbilitySystemComponent resolved));
            Assert.AreSame(abilitySystem, resolved);
            Assert.IsTrue(actor.InitializeAbilityActorInfo());
            Assert.AreSame(actor, abilitySystem.OwnerActor);
            Assert.AreSame(actor, abilitySystem.AvatarActor);
        }

        [Test]
        public void MissingProvider_ReturnsFalseWithoutCreatingState()
        {
            actorObject = new GameObject("ActorWithoutAbilitySystem");
            Actor actor = actorObject.AddComponent<Actor>();

            Assert.IsFalse(actor.TryGetAbilitySystem(out AbilitySystemComponent resolved));
            Assert.IsNull(resolved);
            Assert.IsFalse(actor.InitializeAbilityActorInfo());
        }

        private sealed class AbilitySystemProvider : MonoBehaviour, IAbilitySystemProvider
        {
            public AbilitySystemComponent AbilitySystem { get; private set; }

            public void Initialize(AbilitySystemComponent value)
            {
                AbilitySystem = value;
            }
        }
    }
}
