namespace CycloneGames.BehaviorTree.Core.Nodes.Decorators
{
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

    public enum BehaviorTreeComparisonValueType : byte
    {
        Int = 0,
        Float = 1,
        Bool = 2,
        Object = 3  // IsSet/IsNotSet only
    }

    /// <summary>
    /// Typed blackboard condition decorator. A non-None AbortMode subscribes to tracked keys
    /// and requests tree reevaluation whenever one changes.
    /// </summary>
    public class RuntimeBBComparisonNode : RuntimeDecoratorNode, IRuntimeBTObserverNode
    {
        private RuntimeAbortType _abortMode = RuntimeAbortType.None;
        private BlackboardObserverCallback _observerCallback;
        private RuntimeBlackboard _boundBlackboard;
        private int _boundPrimaryKey;
        private int _boundReferenceKey;
        private bool _boundUsesReferenceKey;

        private int _keyHash;
        private BBComparisonOp _operator = BBComparisonOp.IsSet;
        private BehaviorTreeComparisonValueType _valueType = BehaviorTreeComparisonValueType.Int;
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

        public BehaviorTreeComparisonValueType ValueType
        {
            get => _valueType;
            set
            {
                ThrowIfSetupFrozen();
                if ((uint)(int)value > (uint)BehaviorTreeComparisonValueType.Object)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(ValueType), value, "Unsupported blackboard value type.");
                }
                _valueType = value;
            }
        }

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

        /// <summary>Reference key used when UseRefKey is true; hash values may be negative.</summary>
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

        /// <summary>
        /// Selects whether this node registers blackboard observers. A non-None value enables
        /// event-driven reevaluation; composites decide the abort outcome on the next tick.
        /// </summary>
        public RuntimeAbortType AbortMode
        {
            get => _abortMode;
            set
            {
                ThrowIfSetupFrozen();
                if ((uint)(int)value > (uint)RuntimeAbortType.Both)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(AbortMode), value, "Unsupported abort mode.");
                }
                _abortMode = value;
            }
        }

        public override bool CanEvaluate => true;

        public override void OnAwake()
        {
            base.OnAwake();
            ValidateSetup();
        }

        void IRuntimeBTObserverNode.BindObservers(RuntimeBlackboard blackboard, RuntimeBehaviorTree tree)
        {
            if (_abortMode == RuntimeAbortType.None || _boundBlackboard != null || blackboard == null)
            {
                return;
            }

            _boundBlackboard = blackboard;
            _boundPrimaryKey = _keyHash;
            _boundReferenceKey = _refKeyHash;
            _boundUsesReferenceKey = _useRefKey;
            _observerCallback = OnObservedKeyChanged;
            blackboard.AddObserver(_boundPrimaryKey, _observerCallback);
            if (_boundUsesReferenceKey && _boundReferenceKey != _boundPrimaryKey)
            {
                blackboard.AddObserver(_boundReferenceKey, _observerCallback);
            }
        }

        void IRuntimeBTObserverNode.UnbindObservers()
        {
            if (_boundBlackboard == null)
            {
                return;
            }

            _boundBlackboard.RemoveObserver(_boundPrimaryKey, _observerCallback);
            if (_boundUsesReferenceKey && _boundReferenceKey != _boundPrimaryKey)
            {
                _boundBlackboard.RemoveObserver(_boundReferenceKey, _observerCallback);
            }

            _boundBlackboard = null;
            _observerCallback = null;
        }

        private void OnObservedKeyChanged(int keyHash, RuntimeBlackboard blackboard)
        {
            RuntimeBehaviorTree tree = OwnerTree;
            if (tree != null)
            {
                tree.RequestReevaluation();
            }
        }

        protected override void OnDispose(RuntimeBlackboard blackboard)
        {
            ((IRuntimeBTObserverNode)this).UnbindObservers();
            base.OnDispose(blackboard);
        }

        protected override void ValidateSetup()
        {
            if (ValueType == BehaviorTreeComparisonValueType.Bool
                && Operator != BBComparisonOp.Equal
                && Operator != BBComparisonOp.NotEqual
                && Operator != BBComparisonOp.IsSet
                && Operator != BBComparisonOp.IsNotSet)
            {
                throw new System.InvalidOperationException(
                    "Bool comparisons only support Equal, NotEqual, IsSet, or IsNotSet.");
            }

            if (ValueType == BehaviorTreeComparisonValueType.Object
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
            if (Operator == BBComparisonOp.IsSet)
                return bb.HasKey(KeyHash);
            if (Operator == BBComparisonOp.IsNotSet)
                return !bb.HasKey(KeyHash);

            switch (ValueType)
            {
                case BehaviorTreeComparisonValueType.Int:
                    return CompareInt(bb);
                case BehaviorTreeComparisonValueType.Float:
                    return CompareFloat(bb);
                case BehaviorTreeComparisonValueType.Bool:
                    return CompareBool(bb);
                case BehaviorTreeComparisonValueType.Object:
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
