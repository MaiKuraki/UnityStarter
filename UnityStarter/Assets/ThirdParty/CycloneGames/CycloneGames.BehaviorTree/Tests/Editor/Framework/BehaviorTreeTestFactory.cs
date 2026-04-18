using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;

namespace CycloneGames.BehaviorTree.Tests.Editor.Framework
{
    internal static class BehaviorTreeTestFactory
    {
        public static RuntimeBehaviorTree CreateRuntimeTree(RuntimeNode child, RuntimeBTContext context = null)
        {
            context ??= new RuntimeBTContext();

            var root = new RuntimeRootNode
            {
                Child = child
            };

            root.OnAwake();

            var blackboard = new RuntimeBlackboard
            {
                Context = context
            };

            return new RuntimeBehaviorTree(root, blackboard, context);
        }

        public static RuntimeSequencer CreateSequence(params RuntimeNode[] children)
        {
            var node = new RuntimeSequencer();
            AddChildren(node, children);
            return node;
        }

        public static RuntimeSelector CreateSelector(params RuntimeNode[] children)
        {
            var node = new RuntimeSelector();
            AddChildren(node, children);
            return node;
        }

        public static RuntimeParallelNode CreateParallel(params RuntimeNode[] children)
        {
            var node = new RuntimeParallelNode();
            AddChildren(node, children);
            return node;
        }

        private static void AddChildren(RuntimeCompositeNode composite, RuntimeNode[] children)
        {
            if (children == null)
            {
                composite.Seal();
                return;
            }

            for (int i = 0; i < children.Length; i++)
            {
                composite.AddChild(children[i]);
            }

            composite.Seal();
        }
    }

    internal sealed class FixedStateNode : RuntimeNode
    {
        private readonly RuntimeState _state;

        public FixedStateNode(RuntimeState state)
        {
            _state = state;
        }

        public int RunCount { get; private set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RunCount++;
            return _state;
        }
    }

    internal sealed class RecordingStatefulActionNode : RuntimeStatefulActionNode
    {
        private readonly RuntimeState _startResult;
        private readonly RuntimeState _runningResult;

        public RecordingStatefulActionNode(RuntimeState startResult, RuntimeState runningResult)
        {
            _startResult = startResult;
            _runningResult = runningResult;
        }

        public int StartCount { get; private set; }
        public int RunningCount { get; private set; }
        public int HaltCount { get; private set; }

        protected override RuntimeState OnActionStart(RuntimeBlackboard blackboard)
        {
            StartCount++;
            return _startResult;
        }

        protected override RuntimeState OnActionRunning(RuntimeBlackboard blackboard)
        {
            RunningCount++;
            return _runningResult;
        }

        protected override void OnActionHalted(RuntimeBlackboard blackboard)
        {
            HaltCount++;
        }
    }

    internal sealed class ConditionalRunningNode : RuntimeNode
    {
        private readonly int _key;
        private readonly bool _expectedValue;
        private readonly RuntimeState _stateWhenPassing;

        public ConditionalRunningNode(int key, bool expectedValue, RuntimeState stateWhenPassing = RuntimeState.Running)
        {
            _key = key;
            _expectedValue = expectedValue;
            _stateWhenPassing = stateWhenPassing;
        }

        public override bool CanEvaluate => true;

        public int RunCount { get; private set; }

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            return blackboard.GetBool(_key) == _expectedValue;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RunCount++;
            return Evaluate(blackboard) ? _stateWhenPassing : RuntimeState.Failure;
        }
    }

    internal sealed class UnsupportedDecoratorNode : RuntimeDecoratorNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return Child != null ? Child.Run(blackboard) : RuntimeState.Success;
        }
    }
}
