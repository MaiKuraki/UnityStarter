using System.Linq;

using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class AttributeSetRegistrationTests
    {
        [Test]
        public void GetAttributes_UsesExplicitRegistration()
        {
            var attributeSet = new ExplicitAttributeSet();

            GameplayAttribute[] attributes = attributeSet.GetAttributes().ToArray();

            Assert.AreEqual(2, attributes.Length);
            Assert.AreSame(attributeSet.Health, attributeSet.GetAttribute("Health"));
            Assert.AreSame(attributeSet.Mana, attributeSet.GetAttribute("Mana"));
            Assert.AreSame(attributeSet, attributeSet.Health.OwningSet);
            Assert.AreEqual(1, attributeSet.RegisterCallCount);
        }

        [Test]
        public void GetAttributes_DuplicateStableName_FailsWithoutPartialOwnership()
        {
            var attributeSet = new DuplicateAttributeSet();

            Assert.Throws<System.InvalidOperationException>(() => attributeSet.GetAttributes());
            Assert.IsNull(attributeSet.First.OwningSet);
            Assert.IsNull(attributeSet.Second.OwningSet);
        }

        private sealed class ExplicitAttributeSet : AttributeSet
        {
            public readonly GameplayAttribute Health = new GameplayAttribute("Health");
            public readonly GameplayAttribute Mana = new GameplayAttribute("Mana");

            public int RegisterCallCount { get; private set; }

            protected override void RegisterAttributes()
            {
                RegisterCallCount++;
                RegisterAttribute(Health);
                RegisterAttribute(Mana);
            }
        }

        private sealed class DuplicateAttributeSet : AttributeSet
        {
            public readonly GameplayAttribute First = new GameplayAttribute("Shared");
            public readonly GameplayAttribute Second = new GameplayAttribute("Shared");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(First);
                RegisterAttribute(Second);
            }
        }
    }
}
