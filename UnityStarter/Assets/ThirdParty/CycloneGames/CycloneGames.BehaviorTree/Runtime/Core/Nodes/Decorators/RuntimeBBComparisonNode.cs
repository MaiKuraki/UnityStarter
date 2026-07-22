namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Comparison operator for blackboard condition checks.
    /// </summary>
    public enum BBComparisonOp : byte
    {
        Equal = 0,
        NotEqual = 1,
        GreaterThan = 2,
        GreaterOrEqual = 3,
        LessThan = 4,
        LessOrEqual = 5,
        IsSet = 6,      // key exists (any type)
        IsNotSet = 7    // key does not exist
    }

    /// <summary>
    /// Value type stored in the blackboard key being compared.
    /// </summary>
    public enum BBValueType : byte
    {
        Int = 0,
        Float = 1,
        Bool = 2,
        Object = 3  // IsSet/IsNotSet only
    }

    /// <summary>
    /// Typed blackboard condition decorator with comparison operators.
    /// Supports int/float/bool comparisons and existence checks.
    ///
    /// Can be used with BB Observer for reactive abort:
    ///   - Set AbortMode to trigger re-evaluation when tracked key changes
    ///   - Integrates with AIPerception by observing perception result keys
    ///
    /// Example: "If BB[TargetDistance] < 10.0 → execute child, else Failure"
    /// </summary>
    public class RuntimeBBComparisonNode : RuntimeDecoratorNode
    {
        private int _keyHash;
        private BBComparisonOp _operator = BBComparisonOp.IsSet;
        private BBValueType _valueType = BBValueType.Int;
        private int _refInt;
        private float _refFloat;
        private bool _refBool;
        private int _refKeyHash;
        private bool _useRefKey;
        private float _floatEpsilon = 0.0001f;

        public int KeyHash
        {
            get => _keyHash;
            set => SetSetupValue(ref _keyHash, value);
        }

        public BBComparisonOp Operator
        {
            get => _operator;
            set
            {
                ThrowIfSetupFrozen();
                if ((uint)(int)value > (uint)BBComparisonOp.IsNotSet)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(Operator), value, "Unsupported comparison operator.");
                }
                _operator = value;
            }
        }

        public BBValueType ValueType
        {
            get => _valueType;
            set
            {
                ThrowIfSetupFrozen();
                if ((uint)(int)value > (uint)BBValueType.Object)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(ValueType), value, "Unsupported blackboard value type.");
                }
                _valueType = value;
            }
        }

        // Typed reference values for comparison (set the one matching ValueType)
        public int RefInt
        {
            get => _refInt;
            set => SetSetupValue(ref _refInt, value);
        }

        public float RefFloat
        {
            get => _refFloat;
            set
            {
                ThrowIfSetupFrozen();
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new System.ArgumentOutOfRangeException(nameof(RefFloat), value, "Value must be finite.");
                }
                _refFloat = value;
            }
        }

        public bool RefBool
        {
            get => _refBool;
            set => SetSetupValue(ref _refBool, value);
        }

        /// <summary>
        /// Optional key used when UseRefKey is true. Hash values may be negative.
        /// </summary>
        public int RefKeyHash
        {
            get => _refKeyHash;
            set => SetSetupValue(ref _refKeyHash, value);
        }

        public bool UseRefKey
        {
            get => _useRefKey;
            set => SetSetupValue(ref _useRefKey, value);
        }

        /// <summary>
        /// Float comparison epsilon for network-safe float comparisons.
        /// </summary>
        public float FloatEpsilon
        {
            get => _floatEpsilon;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegativeSetupValue(value, nameof(FloatEpsilon));
                _floatEpsilon = value;
            }
        }

        public override bool CanEvaluate => true;

        public override void OnAwake()
        {
            base.OnAwake();
            ValidateSetup();
        }

        protected override void ValidateSetup()
        {
            if (ValueType == BBValueType.Bool
                && Operator != BBComparisonOp.Equal
                && Operator != BBComparisonOp.NotEqual
                && Operator != BBComparisonOp.IsSet
                && Operator != BBComparisonOp.IsNotSet)
            {
                throw new System.InvalidOperationException(
                    "Bool comparisons only support Equal, NotEqual, IsSet, or IsNotSet.");
            }

            if (ValueType == BBValueType.Object
                && Operator != BBComparisonOp.IsSet
                && Operator != BBComparisonOp.IsNotSet)
            {
                throw new System.InvalidOperationException(
                    "Object comparisons only support IsSet or IsNotSet.");
            }
        }

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            return EvaluateCondition(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;
            if (!EvaluateCondition(blackboard)) return RuntimeState.Failure;
            return Child.Run(blackboard);
        }

        private bool EvaluateCondition(RuntimeBlackboard bb)
        {
            // Existence checks don't need typed comparison
            if (Operator == BBComparisonOp.IsSet)
                return bb.HasKey(KeyHash);
            if (Operator == BBComparisonOp.IsNotSet)
                return !bb.HasKey(KeyHash);

            switch (ValueType)
            {
                case BBValueType.Int:
                    return CompareInt(bb);
                case BBValueType.Float:
                    return CompareFloat(bb);
                case BBValueType.Bool:
                    return CompareBool(bb);
                case BBValueType.Object:
                    return bb.HasKey(KeyHash);
                default:
                    return false;
            }
        }

        private bool CompareInt(RuntimeBlackboard bb)
        {
            int lhs = bb.GetInt(KeyHash);
            int rhs = UseRefKey ? bb.GetInt(RefKeyHash) : RefInt;

            switch (Operator)
            {
                case BBComparisonOp.Equal:          return lhs == rhs;
                case BBComparisonOp.NotEqual:       return lhs != rhs;
                case BBComparisonOp.GreaterThan:    return lhs > rhs;
                case BBComparisonOp.GreaterOrEqual: return lhs >= rhs;
                case BBComparisonOp.LessThan:       return lhs < rhs;
                case BBComparisonOp.LessOrEqual:    return lhs <= rhs;
                default: return false;
            }
        }

        private bool CompareFloat(RuntimeBlackboard bb)
        {
            float lhs = bb.GetFloat(KeyHash);
            float rhs = UseRefKey ? bb.GetFloat(RefKeyHash) : RefFloat;

            switch (Operator)
            {
                case BBComparisonOp.Equal:
                    return System.Math.Abs(lhs - rhs) < FloatEpsilon;
                case BBComparisonOp.NotEqual:
                    return System.Math.Abs(lhs - rhs) >= FloatEpsilon;
                case BBComparisonOp.GreaterThan:    return lhs > rhs;
                case BBComparisonOp.GreaterOrEqual: return lhs >= rhs;
                case BBComparisonOp.LessThan:       return lhs < rhs;
                case BBComparisonOp.LessOrEqual:    return lhs <= rhs;
                default: return false;
            }
        }

        private bool CompareBool(RuntimeBlackboard bb)
        {
            bool lhs = bb.GetBool(KeyHash);
            bool rhs = UseRefKey ? bb.GetBool(RefKeyHash) : RefBool;

            switch (Operator)
            {
                case BBComparisonOp.Equal:    return lhs == rhs;
                case BBComparisonOp.NotEqual: return lhs != rhs;
                default: return false; // >, <, >=, <= not meaningful for bool
            }
        }
    }
}
