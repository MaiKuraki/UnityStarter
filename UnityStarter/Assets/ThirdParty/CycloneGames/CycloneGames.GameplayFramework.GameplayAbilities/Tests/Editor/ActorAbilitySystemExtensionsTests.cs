using System.Reflection;
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
            Actor actor = AddInitializedActor(actorObject);
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
            Actor actor = AddInitializedActor(actorObject);

            Assert.IsFalse(actor.TryGetAbilitySystem(out AbilitySystemComponent resolved));
            Assert.IsNull(resolved);
            Assert.IsFalse(actor.InitializeAbilityActorInfo());
        }

        private static Actor AddInitializedActor(GameObject gameObject)
        {
            Actor actor = gameObject.AddComponent<Actor>();
            MethodInfo awake = typeof(Actor).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(awake);
            awake.Invoke(actor, null);
            return actor;
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
