using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public sealed class RuntimeBehaviorTreeBuilder
    {
        private readonly List<ParentFrame> _frames = new List<ParentFrame>(8);
        private RuntimeBTContext _context;
        private RuntimeBlackboard _blackboard;
        private RuntimeBlackboardSchema _blackboardSchema;
        private bool _applySchemaDefaults = true;
        private RuntimeNode _rootChild;
        private int _tickInterval = 1;
        private bool _built;

        public RuntimeBehaviorTreeBuilder()
            : this(new RuntimeBTContext())
        {
        }

        public RuntimeBehaviorTreeBuilder(object owner)
            : this(new RuntimeBTContext(owner))
        {
        }

        public RuntimeBehaviorTreeBuilder(RuntimeBTContext context)
        {
            _context = context ?? new RuntimeBTContext();
        }

        public RuntimeBehaviorTreeBuilder WithContext(RuntimeBTContext context)
        {
            EnsureNotBuilt();
            _context = context ?? new RuntimeBTContext();
            return this;
        }

        public RuntimeBehaviorTreeBuilder WithOwner(object owner)
        {
            EnsureNotBuilt();
            _context ??= new RuntimeBTContext();
            _context.Owner = owner;
            return this;
        }

        public RuntimeBehaviorTreeBuilder WithServiceResolver(IRuntimeBTServiceResolver resolver)
        {
            EnsureNotBuilt();
            _context ??= new RuntimeBTContext();
            _context.ServiceResolver = resolver;
            return this;
        }

        public RuntimeBehaviorTreeBuilder WithBlackboard(RuntimeBlackboard blackboard)
        {
            EnsureNotBuilt();
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            return this;
        }

        public RuntimeBehaviorTreeBuilder WithBlackboardSchema(RuntimeBlackboardSchema schema, bool applyDefaults = true)
        {
            EnsureNotBuilt();
            _blackboardSchema = schema ?? throw new ArgumentNullException(nameof(schema));
            _applySchemaDefaults = applyDefaults;
            return this;
        }

        public RuntimeBehaviorTreeBuilder WithTickInterval(int tickInterval)
        {
            EnsureNotBuilt();
            _tickInterval = tickInterval < 1 ? 1 : tickInterval;
            return this;
        }

        public RuntimeBehaviorTreeBuilder Node(RuntimeNode node)
        {
            AddNode(node);
            return this;
        }

        public RuntimeBehaviorTreeBuilder Node<TNode>(TNode node, Action<TNode> configure) where TNode : RuntimeNode
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            configure?.Invoke(node);
            AddNode(node);
            return this;
        }

        public RuntimeBehaviorTreeBuilder Command(IRuntimeBTCommand command, string name = null)
        {
            return Node(new RuntimeCommandActionNode(command)
            {
                Name = name
            });
        }

        public RuntimeBehaviorTreeBuilder Action(Func<RuntimeBlackboard, RuntimeState> run, string name = null)
        {
            return Node(new RuntimeLambdaActionNode(run)
            {
                Name = name
            });
        }

        public RuntimeBehaviorTreeBuilder Action(System.Action<RuntimeBlackboard> run, string name = null)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            return Action(
                blackboard =>
                {
                    run(blackboard);
                    return RuntimeState.Success;
                },
                name);
        }

        public RuntimeBehaviorTreeBuilder Condition(Func<RuntimeBlackboard, bool> predicate, string name = null)
        {
            return Node(new RuntimeLambdaConditionNode(predicate)
            {
                Name = name
            });
        }

        public RuntimeBehaviorTreeBuilder Condition(IRuntimeBTConditionStrategy strategy, string name = null)
        {
            return Node(new RuntimeStrategyConditionNode(strategy)
            {
                Name = name
            });
        }

        public RuntimeBehaviorTreeBuilder RandomChance(float chance, float outOf = 1f, uint seed = 0u, string name = null)
        {
            return Node(new RuntimeRandomChanceNode(chance, outOf, seed)
            {
                Name = name
            });
        }

        public RuntimeBehaviorTreeBuilder Wait(float duration, bool useUnscaledTime = false)
        {
            return Node(new RuntimeWaitNode
            {
                Duration = duration,
                UseUnscaledTime = useUnscaledTime
            });
        }

        public RuntimeBehaviorTreeBuilder WaitRandom(float minDuration, float maxDuration, bool useUnscaledTime = false)
        {
            return Node(new RuntimeWaitNode
            {
                UseRandomRange = true,
                RangeMin = minDuration,
                RangeMax = maxDuration,
                UseUnscaledTime = useUnscaledTime
            });
        }

        public RuntimeBehaviorTreeBuilder Sequence(Action<RuntimeSequencer> configure = null)
        {
            return Composite(new RuntimeSequencer(), configure);
        }

        public RuntimeBehaviorTreeBuilder SequenceWithMemory(Action<RuntimeSequenceWithMemory> configure = null)
        {
            return Composite(new RuntimeSequenceWithMemory(), configure);
        }

        public RuntimeBehaviorTreeBuilder Selector(Action<RuntimeSelector> configure = null)
        {
            return Composite(new RuntimeSelector(), configure);
        }

        public RuntimeBehaviorTreeBuilder SelectorRandom(uint seed = 0u, Action<RuntimeSelectorRandom> configure = null)
        {
            return Composite(new RuntimeSelectorRandom(seed), configure);
        }

        public RuntimeBehaviorTreeBuilder ReactiveSequence(Action<RuntimeReactiveSequence> configure = null)
        {
            return Composite(new RuntimeReactiveSequence(), configure);
        }

        public RuntimeBehaviorTreeBuilder ReactiveFallback(Action<RuntimeReactiveFallback> configure = null)
        {
            return Composite(new RuntimeReactiveFallback(), configure);
        }

        public RuntimeBehaviorTreeBuilder Parallel(RuntimeParallelMode mode = RuntimeParallelMode.Default, Action<RuntimeParallelNode> configure = null)
        {
            return Composite(
                new RuntimeParallelNode
                {
                    Mode = mode
                },
                configure);
        }

        public RuntimeBehaviorTreeBuilder ParallelAll(Action<RuntimeParallelAllNode> configure = null)
        {
            return Composite(new RuntimeParallelAllNode(), configure);
        }

        public RuntimeBehaviorTreeBuilder IfThenElse(Action<RuntimeIfThenElseNode> configure = null)
        {
            return Composite(new RuntimeIfThenElseNode(), configure);
        }

        public RuntimeBehaviorTreeBuilder WhileDoElse(Action<RuntimeWhileDoElseNode> configure = null)
        {
            return Composite(new RuntimeWhileDoElseNode(), configure);
        }

        public RuntimeBehaviorTreeBuilder Switch(int blackboardKey, Action<RuntimeSwitchNode> configure = null)
        {
            return Composite(
                new RuntimeSwitchNode
                {
                    VariableKeyHash = blackboardKey
                },
                configure);
        }

        public RuntimeBehaviorTreeBuilder ProbabilityBranch(float[] weights, Action<RuntimeProbabilityBranch> configure = null)
        {
            var node = new RuntimeProbabilityBranch();
            node.SetWeights(weights);
            return Composite(node, configure);
        }

        public RuntimeBehaviorTreeBuilder UtilitySelector(int[] scoreKeys, Action<RuntimeUtilitySelector> configure = null)
        {
            var node = new RuntimeUtilitySelector();
            node.SetScoreKeys(scoreKeys);
            return Composite(node, configure);
        }

        public RuntimeBehaviorTreeBuilder Decorator(RuntimeDecoratorNode node)
        {
            EnsureNotBuilt();
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            AddNode(node);
            _frames.Add(new DecoratorFrame(node));
            return this;
        }

        public RuntimeBehaviorTreeBuilder Decorator<TNode>(TNode node, Action<TNode> configure) where TNode : RuntimeDecoratorNode
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            configure?.Invoke(node);
            return Decorator(node);
        }

        public RuntimeBehaviorTreeBuilder Decorator(Func<RuntimeBlackboard, RuntimeNode, RuntimeState> run, string name = null)
        {
            return Decorator(new RuntimeLambdaDecoratorNode(run)
            {
                Name = name
            });
        }

        public RuntimeBehaviorTreeBuilder Inverter()
        {
            return Decorator(new RuntimeInverterNode());
        }

        public RuntimeBehaviorTreeBuilder Succeeder()
        {
            return Decorator(new RuntimeSucceederNode());
        }

        public RuntimeBehaviorTreeBuilder ForceFailure()
        {
            return Decorator(new RuntimeForceFailureNode());
        }

        public RuntimeBehaviorTreeBuilder Repeat(int count)
        {
            return Decorator(new RuntimeRepeatNode
            {
                RepeatForever = false,
                RepeatCount = count
            });
        }

        public RuntimeBehaviorTreeBuilder RepeatForever()
        {
            return Decorator(new RuntimeRepeatNode
            {
                RepeatForever = true
            });
        }

        public RuntimeBehaviorTreeBuilder Retry(int maxAttempts)
        {
            return Decorator(new RuntimeRetryNode
            {
                MaxAttempts = maxAttempts
            });
        }

        public RuntimeBehaviorTreeBuilder Timeout(float duration)
        {
            return Decorator(new RuntimeTimeoutNode
            {
                TimeoutSeconds = duration
            });
        }

        public RuntimeBehaviorTreeBuilder Delay(float duration)
        {
            return Decorator(new RuntimeDelayNode
            {
                DelaySeconds = duration
            });
        }

        public RuntimeBehaviorTreeBuilder CoolDown(float duration)
        {
            return Decorator(new RuntimeCoolDownNode
            {
                CoolDown = duration
            });
        }

        public RuntimeBehaviorTreeBuilder RunOnce()
        {
            return Decorator(new RuntimeRunOnceNode());
        }

        public RuntimeBehaviorTreeBuilder KeepRunningUntilFailure()
        {
            return Decorator(new RuntimeKeepRunningUntilFailureNode());
        }

        public RuntimeBehaviorTreeBuilder WaitSuccess(float timeout)
        {
            return Decorator(new RuntimeWaitSuccessNode
            {
                WaitTime = timeout
            });
        }

        public RuntimeBehaviorTreeBuilder End()
        {
            EnsureNotBuilt();
            if (_frames.Count == 0)
            {
                throw new InvalidOperationException("No open behavior tree builder scope exists.");
            }

            CloseLastFrame();
            return this;
        }

        public RuntimeBehaviorTree Build()
        {
            EnsureNotBuilt();
            while (_frames.Count > 0)
            {
                CloseLastFrame();
            }

            if (_rootChild == null)
            {
                throw new InvalidOperationException("Cannot build a behavior tree without a root child node.");
            }

            var root = new RuntimeRootNode
            {
                Child = _rootChild
            };

            bool ownsBlackboard = _blackboard == null;
            RuntimeBlackboard blackboard = _blackboard ??
                new RuntimeBlackboard(schema: _blackboardSchema, applySchemaDefaults: _applySchemaDefaults);

            RuntimeBehaviorTree tree = null;
            try
            {
                if (_blackboard != null &&
                    _blackboardSchema != null &&
                    !ReferenceEquals(_blackboard.Schema, _blackboardSchema))
                {
                    blackboard.BindSchema(_blackboardSchema, _applySchemaDefaults);
                }

                tree = new RuntimeBehaviorTree(root, blackboard, _context);
                tree.TickInterval = _tickInterval;
                _built = true;
                return tree;
            }
            catch (Exception buildException)
            {
                try
                {
                    if (tree != null && !tree.IsDisposed)
                    {
                        tree.Dispose();
                    }
                    else if (ownsBlackboard && !blackboard.IsDisposed)
                    {
                        blackboard.Dispose();
                    }
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(buildException, cleanupException);
                }

                throw;
            }
        }

        private RuntimeBehaviorTreeBuilder Composite<TNode>(TNode node, Action<TNode> configure) where TNode : RuntimeCompositeNode
        {
            configure?.Invoke(node);
            AddNode(node);
            _frames.Add(new CompositeFrame(node));
            return this;
        }

        private void AddNode(RuntimeNode node)
        {
            EnsureNotBuilt();
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (_frames.Count > 0)
            {
                _frames[_frames.Count - 1].AddChild(node);
                return;
            }

            if (_rootChild != null)
            {
                throw new InvalidOperationException("A behavior tree can only have one root child. Wrap multiple root nodes in a composite.");
            }

            _rootChild = node;
        }

        private void CloseLastFrame()
        {
            int lastIndex = _frames.Count - 1;
            ParentFrame frame = _frames[lastIndex];
            _frames.RemoveAt(lastIndex);
            frame.Close();
        }

        private void EnsureNotBuilt()
        {
            if (_built)
            {
                throw new InvalidOperationException("A behavior tree builder cannot be reused after Build().");
            }
        }

        private abstract class ParentFrame
        {
            public abstract void AddChild(RuntimeNode child);
            public virtual void Close()
            {
            }
        }

        private sealed class CompositeFrame : ParentFrame
        {
            private readonly RuntimeCompositeNode _node;

            public CompositeFrame(RuntimeCompositeNode node)
            {
                _node = node;
            }

            public override void AddChild(RuntimeNode child)
            {
                _node.AddChild(child);
            }

            public override void Close()
            {
                _node.Seal();
            }
        }

        private sealed class DecoratorFrame : ParentFrame
        {
            private readonly RuntimeDecoratorNode _node;

            public DecoratorFrame(RuntimeDecoratorNode node)
            {
                _node = node;
            }

            public override void AddChild(RuntimeNode child)
            {
                if (_node.Child != null)
                {
                    throw new InvalidOperationException($"{_node.GetType().Name} already has a child.");
                }

                _node.Child = child;
            }

            public override void Close()
            {
                if (_node.Child == null)
                {
                    throw new InvalidOperationException($"{_node.GetType().Name} requires exactly one child.");
                }
            }
        }
    }
}
