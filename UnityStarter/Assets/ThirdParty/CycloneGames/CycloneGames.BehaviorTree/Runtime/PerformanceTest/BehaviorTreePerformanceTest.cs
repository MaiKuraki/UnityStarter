using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

public class BehaviorTreePerformanceTest : MonoBehaviour
{
#if UNITY_EDITOR

        [SerializeField] private BehaviorTree Tree;
        private RuntimeBehaviorTree _runtimeTree;

        private void Start()
        {
                if (Tree == null)
                {
                        Tree = ScriptableObject.CreateInstance<BehaviorTree>();
                        var root = ScriptableObject.CreateInstance<BTRootNode>();
                        Tree.Root = root;

                        var selector = ScriptableObject.CreateInstance<SelectorNode>();
                        Tree.AddNode(selector);
                        Tree.AddChild(root, selector);

                        for (int i = 0; i < 10; i++)
                        {
                                var action = ScriptableObject.CreateInstance<WaitNode>();
                                action.Duration = 0.1f;
                                Tree.AddNode(action);
                                Tree.AddChild(selector, action);
                        }
                }

                _runtimeTree = Tree.Compile(gameObject);

                _runtimeTree?.Tick();
        }

        private void Update()
        {
                if (_runtimeTree == null) return;

                Profiler.BeginSample("BehaviorTree.RuntimeTick");
                _runtimeTree.Tick();
                Profiler.EndSample();

                Profiler.BeginSample("RuntimeBlackboard.TypedAccess");
                int testKey = Animator.StringToHash("TestInt");
                _runtimeTree.Blackboard.SetInt(testKey, Time.frameCount);
                int val = _runtimeTree.Blackboard.GetInt(testKey);
                Profiler.EndSample();
        }
#endif
}
