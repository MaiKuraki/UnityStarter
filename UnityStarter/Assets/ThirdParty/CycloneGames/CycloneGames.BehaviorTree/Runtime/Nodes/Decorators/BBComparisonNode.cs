using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("BB Compare", "Typed blackboard comparison decorator. Supports int/float/bool with operators (==, !=, >, >=, <, <=) and existence checks (IsSet/IsNotSet). Can compare against a constant or another BB key.")]
    public class BBComparisonNode : DecoratorNode
    {
        [Header("Key to Check")]
        [SerializeField] private string _key;

        [Header("Comparison")]
        [SerializeField] private BBComparisonOp _operator = BBComparisonOp.IsSet;
        [SerializeField] private BBValueType _valueType = BBValueType.Int;

        [Header("Reference Value (used when Reference Key is empty)")]
        [SerializeField] private int _refInt;
        [SerializeField] private float _refFloat;
        [SerializeField] private bool _refBool;

        [Header("Reference Key (compare against another BB key)")]
        [Tooltip("Leave empty to compare against the constant above")]
        [SerializeField] private string _refKey;

        [Header("Float Comparison")]
        [SerializeField] private float _floatEpsilon = 0.0001f;

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            // Legacy SO path — basic fallback
            if (_operator == BBComparisonOp.IsNotSet)
                return !blackBoard.Contains(_key) ? Child.Run(blackBoard) : BTState.FAILURE;

            if (!blackBoard.Contains(_key))
                return BTState.FAILURE;

            return Child.Run(blackBoard);
        }

        public override BTNode Clone()
        {
            var clone = (BBComparisonNode)base.Clone();
            clone._key = _key;
            clone._operator = _operator;
            clone._valueType = _valueType;
            clone._refInt = _refInt;
            clone._refFloat = _refFloat;
            clone._refBool = _refBool;
            clone._refKey = _refKey;
            clone._floatEpsilon = _floatEpsilon;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new RuntimeBBComparisonNode();
            node.GUID = GUID;
            node.KeyHash = Animator.StringToHash(_key);
            node.Operator = _operator;
            node.ValueType = _valueType;
            node.RefInt = _refInt;
            node.RefFloat = _refFloat;
            node.RefBool = _refBool;
            node.RefKeyHash = string.IsNullOrEmpty(_refKey) ? 0 : Animator.StringToHash(_refKey);
            node.FloatEpsilon = _floatEpsilon;

            if (Child != null)
            {
                node.Child = Child.CreateRuntimeNode();
            }
            return node;
        }
    }
}
