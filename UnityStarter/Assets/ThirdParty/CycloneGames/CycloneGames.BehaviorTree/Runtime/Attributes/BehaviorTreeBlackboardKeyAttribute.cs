using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Attributes
{
    /// <summary>
    /// Editor-only authoring metadata for a serialized blackboard key string. Runtime execution
    /// continues to use precomputed integer hashes and does not reflect over this attribute.
    /// </summary>
    public sealed class BehaviorTreeBlackboardKeyAttribute : PropertyAttribute
    {
        public BehaviorTreeBlackboardKeyAttribute(bool allowEmpty = false)
        {
            AllowEmpty = allowEmpty;
        }

        public BehaviorTreeBlackboardKeyAttribute(
            RuntimeBlackboardValueType expectedType,
            bool allowEmpty = false)
        {
            ExpectedType = expectedType;
            HasExpectedType = true;
            AllowEmpty = allowEmpty;
        }

        public RuntimeBlackboardValueType ExpectedType { get; }
        public bool HasExpectedType { get; }
        public bool AllowEmpty { get; }
    }
}
