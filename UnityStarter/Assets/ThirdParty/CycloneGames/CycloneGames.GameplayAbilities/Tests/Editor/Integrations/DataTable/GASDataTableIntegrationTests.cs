using System.Collections.Generic;

using CycloneGames.DataTable;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayAbilities.Runtime.Integrations.DataTable;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor.Integrations.DataTable
{
    public sealed class GASDataTableIntegrationTests
    {
        [Test]
        public void DataTableMagnitudeCalculation_UsesEffectLevel()
        {
            var table = new DataTable<SkillMagnitudeRow>(new[]
            {
                new SkillMagnitudeRow { Id = 1001, BaseValue = 20f, ScalePerLevel = 5f }
            });

            ModifierInfo modifier = DataTableModifierFactory.CreateLinearModifier(
                table,
                1001,
                "Health",
                EAttributeModifierOperation.Add,
                row => row.BaseValue,
                row => row.ScalePerLevel);

            var effect = new GameplayEffect(
                "GE_Table_Damage",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo> { modifier });

            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, null, 4);

            Assert.That(spec.GetCalculatedMagnitude(0), Is.EqualTo(35f));

            spec.ReturnToPool();
        }

        [Test]
        public void DataTableAttributeInitializer_AppliesRowsToAttributeSet()
        {
            var table = new DataTable<AttributeRow>(new[]
            {
                new AttributeRow { Id = 1, AttributeName = "Health", BaseValue = 100f, CurrentValue = 75f },
                new AttributeRow { Id = 2, AttributeName = "Mana", BaseValue = 50f, CurrentValue = 40f }
            });

            var initializer = DataTableAttributeInitializer<AttributeRow>.FromTable(
                table,
                row => row.AttributeName,
                row => row.BaseValue,
                row => row.CurrentValue);
            var attributeSet = new TestAttributeSet();

            int appliedCount = initializer.ApplyAll(attributeSet);

            Assert.That(appliedCount, Is.EqualTo(2));
            Assert.That(attributeSet.Health.BaseValue, Is.EqualTo(100f));
            Assert.That(attributeSet.Health.CurrentValue, Is.EqualTo(75f));
            Assert.That(attributeSet.Mana.BaseValue, Is.EqualTo(50f));
            Assert.That(attributeSet.Mana.CurrentValue, Is.EqualTo(40f));
        }

        [Test]
        public void DataTableModifierFactory_CanUseCustomLookupAndEvaluator()
        {
            var rows = new Dictionary<int, SkillMagnitudeRow>
            {
                [2001] = new SkillMagnitudeRow { Id = 2001, BaseValue = 8f, ScalePerLevel = 3f }
            };

            ModifierInfo modifier = DataTableModifierFactory.CreateEvaluatedModifier<SkillMagnitudeRow>(
                rows.TryGetValue,
                2001,
                "Damage",
                EAttributeModifierOperation.Add,
                (row, level, _) => row.BaseValue * level + row.ScalePerLevel);

            var effect = new GameplayEffect(
                "GE_Custom_Table_Damage",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo> { modifier });

            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, null, 5);

            Assert.That(spec.GetCalculatedMagnitude(0), Is.EqualTo(43f));

            spec.ReturnToPool();
        }

        private sealed class SkillMagnitudeRow : IDataRow
        {
            public int Id { get; set; }
            public float BaseValue { get; set; }
            public float ScalePerLevel { get; set; }
        }

        private sealed class AttributeRow : IDataRow
        {
            public int Id { get; set; }
            public string AttributeName { get; set; }
            public float BaseValue { get; set; }
            public float CurrentValue { get; set; }
        }

        private sealed class TestAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");
            public GameplayAttribute Mana { get; } = new GameplayAttribute("Mana");
        }
    }
}
