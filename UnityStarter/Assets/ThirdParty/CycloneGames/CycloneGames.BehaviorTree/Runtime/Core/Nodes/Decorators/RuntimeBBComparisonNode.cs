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
    /// Example: "If BB[TargetDistance] &lt; 10.0 → execute child, else Failure"
    /// </summary>
    public class RuntimeBBComparisonNode : RuntimeDecoratorNode
    {
        public int KeyHash { get; set; }
        public BBComparisonOp Operator { get; set; } = BBComparisonOp.IsSet;
        public BBValueType ValueType { get; set; } = BBValueType.Int;

        // Typed reference values for comparison (set the one matching ValueType)
        public int RefInt { get; set; }
        public float RefFloat { get; set; }
        public bool RefBool { get; set; }

        /// <summary>
        /// Optional: compare against another BB key instead of a constant.
        /// When > 0, the reference value comes from this BB key.
        /// </summary>
        public int RefKeyHash { get; set; }

        /// <summary>
        /// Float comparison epsilon for network-safe float comparisons.
        /// </summary>
        public float FloatEpsilon { get; set; } = 0.0001f;

        public override bool CanEvaluate => true;

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
            int rhs = RefKeyHash > 0 ? bb.GetInt(RefKeyHash) : RefInt;

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
            float rhs = RefKeyHash > 0 ? bb.GetFloat(RefKeyHash) : RefFloat;

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
            bool rhs = RefKeyHash > 0 ? bb.GetBool(RefKeyHash) : RefBool;

            switch (Operator)
            {
                case BBComparisonOp.Equal:    return lhs == rhs;
                case BBComparisonOp.NotEqual: return lhs != rhs;
                default: return false; // >, <, >=, <= not meaningful for bool
            }
        }
    }
}
