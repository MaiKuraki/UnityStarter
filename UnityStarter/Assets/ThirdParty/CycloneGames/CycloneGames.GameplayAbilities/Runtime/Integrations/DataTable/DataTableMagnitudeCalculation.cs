using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime.Integrations.DataTable
{
    public sealed class DataTableMagnitudeCalculation : GameplayModMagnitudeCalculation
    {
        private readonly IGASLevelValueProvider _valueProvider;
        private readonly int _rowId;
        private readonly float _fallbackValue;
        private readonly bool _throwWhenMissing;

        public DataTableMagnitudeCalculation(
            IGASLevelValueProvider valueProvider,
            int rowId,
            float fallbackValue = 0f,
            bool throwWhenMissing = false)
        {
            _valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
            _rowId = rowId;
            _fallbackValue = fallbackValue;
            _throwWhenMissing = throwWhenMissing;
        }

        public override float CalculateMagnitude(GameplayEffectSpec spec)
        {
            int level = spec != null ? spec.Level : 1;
            if (_valueProvider.TryGetValue(_rowId, level, spec, out float value))
            {
                return value;
            }

            if (_throwWhenMissing)
            {
                throw new KeyNotFoundException($"GameplayAbilities DataTable row not found: {_rowId}");
            }

            return _fallbackValue;
        }
    }
}
