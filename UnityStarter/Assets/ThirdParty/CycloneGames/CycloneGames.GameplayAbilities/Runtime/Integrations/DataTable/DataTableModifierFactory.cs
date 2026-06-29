using System;

using CycloneGames.DataTable;

namespace CycloneGames.GameplayAbilities.Runtime.Integrations.DataTable
{
    public static class DataTableModifierFactory
    {
        public static ModifierInfo CreateModifier(
            IGASLevelValueProvider valueProvider,
            int rowId,
            string attributeName,
            EAttributeModifierOperation operation,
            float fallbackValue = 0f,
            bool throwWhenMissing = false,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
        {
            if (string.IsNullOrEmpty(attributeName))
            {
                throw new ArgumentException("Attribute name must be provided.", nameof(attributeName));
            }

            return new ModifierInfo(
                attributeName,
                operation,
                new DataTableMagnitudeCalculation(valueProvider, rowId, fallbackValue, throwWhenMissing),
                snapshotPolicy);
        }

        public static ModifierInfo CreateLinearModifier<TRow>(
            IDataTable<TRow> table,
            int rowId,
            string attributeName,
            EAttributeModifierOperation operation,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> scalingFactorAccessor = null,
            float fallbackValue = 0f,
            bool throwWhenMissing = false,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            where TRow : IDataRow
        {
            var provider = DataTableLevelValueProvider<TRow>.FromTable(
                table,
                baseValueAccessor,
                scalingFactorAccessor);

            return CreateModifier(
                provider,
                rowId,
                attributeName,
                operation,
                fallbackValue,
                throwWhenMissing,
                snapshotPolicy);
        }

        public static ModifierInfo CreateLinearModifier<TRow>(
            GASDataTableTryGetRow<TRow> tryGetRow,
            int rowId,
            string attributeName,
            EAttributeModifierOperation operation,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> scalingFactorAccessor = null,
            float fallbackValue = 0f,
            bool throwWhenMissing = false,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            where TRow : IDataRow
        {
            var provider = DataTableLevelValueProvider<TRow>.FromLookup(
                tryGetRow,
                baseValueAccessor,
                scalingFactorAccessor);

            return CreateModifier(
                provider,
                rowId,
                attributeName,
                operation,
                fallbackValue,
                throwWhenMissing,
                snapshotPolicy);
        }

        public static ModifierInfo CreateEvaluatedModifier<TRow>(
            GASDataTableTryGetRow<TRow> tryGetRow,
            int rowId,
            string attributeName,
            EAttributeModifierOperation operation,
            GASDataTableValueEvaluator<TRow> valueEvaluator,
            float fallbackValue = 0f,
            bool throwWhenMissing = false,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            where TRow : IDataRow
        {
            var provider = DataTableLevelValueProvider<TRow>.FromLookup(
                tryGetRow,
                valueEvaluator);

            return CreateModifier(
                provider,
                rowId,
                attributeName,
                operation,
                fallbackValue,
                throwWhenMissing,
                snapshotPolicy);
        }
    }
}
