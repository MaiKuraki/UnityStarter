using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
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

    }
}
