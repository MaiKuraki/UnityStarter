using System;
using System.Collections.Generic;

using CycloneGames.DataTable;

namespace CycloneGames.GameplayAbilities.Runtime.Integrations.DataTable
{
    public sealed class DataTableAttributeInitializer<TRow> where TRow : IDataRow
    {
        private readonly GASDataTableTryGetRow<TRow> _tryGetRow;
        private readonly IReadOnlyList<TRow> _allRows;
        private readonly Func<TRow, string> _attributeNameAccessor;
        private readonly Func<TRow, float> _baseValueAccessor;
        private readonly Func<TRow, float> _currentValueAccessor;

        private DataTableAttributeInitializer(
            GASDataTableTryGetRow<TRow> tryGetRow,
            IReadOnlyList<TRow> allRows,
            Func<TRow, string> attributeNameAccessor,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> currentValueAccessor)
        {
            _tryGetRow = tryGetRow ?? throw new ArgumentNullException(nameof(tryGetRow));
            _allRows = allRows;
            _attributeNameAccessor = attributeNameAccessor ?? throw new ArgumentNullException(nameof(attributeNameAccessor));
            _baseValueAccessor = baseValueAccessor ?? throw new ArgumentNullException(nameof(baseValueAccessor));
            _currentValueAccessor = currentValueAccessor;
        }

        public static DataTableAttributeInitializer<TRow> FromTable(
            IDataTable<TRow> table,
            Func<TRow, string> attributeNameAccessor,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> currentValueAccessor = null)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            return new DataTableAttributeInitializer<TRow>(
                table.TryGet,
                table.All,
                attributeNameAccessor,
                baseValueAccessor,
                currentValueAccessor);
        }

        public static DataTableAttributeInitializer<TRow> FromLookup(
            GASDataTableTryGetRow<TRow> tryGetRow,
            Func<TRow, string> attributeNameAccessor,
            Func<TRow, float> baseValueAccessor,
            Func<TRow, float> currentValueAccessor = null)
        {
            return new DataTableAttributeInitializer<TRow>(
                tryGetRow,
                null,
                attributeNameAccessor,
                baseValueAccessor,
                currentValueAccessor);
        }

        public int ApplyAll(AttributeSet attributeSet)
        {
            if (attributeSet == null)
            {
                throw new ArgumentNullException(nameof(attributeSet));
            }

            if (_allRows == null)
            {
                throw new InvalidOperationException("ApplyAll requires an IDataTable-backed initializer.");
            }

            int appliedCount = 0;
            for (int i = 0; i < _allRows.Count; i++)
            {
                if (TryApplyRow(attributeSet, _allRows[i]))
                {
                    appliedCount++;
                }
            }

            return appliedCount;
        }

        public bool TryApply(AttributeSet attributeSet, int rowId)
        {
            if (attributeSet == null)
            {
                throw new ArgumentNullException(nameof(attributeSet));
            }

            if (!_tryGetRow(rowId, out TRow row))
            {
                return false;
            }

            return TryApplyRow(attributeSet, row);
        }

        public bool TryApplyRow(AttributeSet attributeSet, TRow row)
        {
            if (attributeSet == null)
            {
                throw new ArgumentNullException(nameof(attributeSet));
            }

            if (row == null)
            {
                return false;
            }

            string attributeName = _attributeNameAccessor(row);
            if (string.IsNullOrEmpty(attributeName))
            {
                return false;
            }

            GameplayAttribute attribute = attributeSet.GetAttribute(attributeName);
            if (attribute == null)
            {
                return false;
            }

            float baseValue = _baseValueAccessor(row);
            float currentValue = _currentValueAccessor != null ? _currentValueAccessor(row) : baseValue;
            attributeSet.SetBaseValue(attribute, baseValue);
            attributeSet.SetCurrentValue(attribute, currentValue);
            return true;
        }
    }
}
