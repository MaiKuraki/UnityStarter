using System;

using CycloneGames.DataTable;

namespace CycloneGames.GameplayAbilities.Runtime.Integrations.DataTable
{
    public delegate bool GASDataTableTryGetRow<TRow>(int rowId, out TRow row);

    public delegate float GASDataTableValueEvaluator<TRow>(TRow row, int level, GameplayEffectSpec spec);

    public interface IGASLevelValueProvider
    {
        bool TryGetValue(int rowId, int level, GameplayEffectSpec spec, out float value);
    }

    public sealed class DataTableLevelValueProvider<TRow> : IGASLevelValueProvider where TRow : IDataRow
    {
        private readonly GASDataTableTryGetRow<TRow> _tryGetRow;
        private readonly Func<TRow, float> _baseValueAccessor;
        private readonly Func<TRow, float> _scalingFactorAccessor;
        private readonly GASDataTableValueEvaluator<TRow> _valueEvaluator;

        private DataTableLevelValueProvider(
            GASDataTableTryGetRow<TRow> tryGetRow,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> scalingFactorAccessor,
            GASDataTableValueEvaluator<TRow> valueEvaluator)
        {
            _tryGetRow = tryGetRow ?? throw new ArgumentNullException(nameof(tryGetRow));
            _baseValueAccessor = baseValueAccessor;
            _scalingFactorAccessor = scalingFactorAccessor;
            _valueEvaluator = valueEvaluator;

            if (_valueEvaluator == null && _baseValueAccessor == null)
            {
                throw new ArgumentNullException(nameof(baseValueAccessor));
            }
        }

        public static DataTableLevelValueProvider<TRow> FromTable(
            IDataTable<TRow> table,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> scalingFactorAccessor = null)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            return FromLookup(table.TryGet, baseValueAccessor, scalingFactorAccessor);
        }

        public static DataTableLevelValueProvider<TRow> FromTable(
            IDataTable<TRow> table,
            GASDataTableValueEvaluator<TRow> valueEvaluator)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            return FromLookup(table.TryGet, valueEvaluator);
        }

        public static DataTableLevelValueProvider<TRow> FromLookup(
            GASDataTableTryGetRow<TRow> tryGetRow,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> scalingFactorAccessor = null)
        {
            return new DataTableLevelValueProvider<TRow>(
                tryGetRow,
                baseValueAccessor,
                scalingFactorAccessor,
                null);
        }

        public static DataTableLevelValueProvider<TRow> FromLookup(
            GASDataTableTryGetRow<TRow> tryGetRow,
            GASDataTableValueEvaluator<TRow> valueEvaluator)
        {
            if (valueEvaluator == null)
            {
                throw new ArgumentNullException(nameof(valueEvaluator));
            }

            return new DataTableLevelValueProvider<TRow>(
                tryGetRow,
                null,
                null,
                valueEvaluator);
        }

        public bool TryGetValue(int rowId, int level, GameplayEffectSpec spec, out float value)
        {
            if (!_tryGetRow(rowId, out TRow row) || row == null)
            {
                value = 0f;
                return false;
            }

            value = _valueEvaluator != null
                ? _valueEvaluator(row, level, spec)
                : EvaluateLinear(row, level);
            return true;
        }

        private float EvaluateLinear(TRow row, int level)
        {
            float baseValue = _baseValueAccessor(row);
            float scalingFactor = _scalingFactorAccessor != null ? _scalingFactorAccessor(row) : 0f;
            return baseValue + scalingFactor * (level > 0 ? level - 1 : 0);
        }
    }
}
