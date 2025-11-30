using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

public class BehaviorTreePerformanceTest : MonoBehaviour
{
    public BehaviorTree Tree;
    private BlackBoard _blackBoard;

    private void Start()
    {
        // Create a simple tree programmatically if not assigned
        if (Tree == null)
        {
            Tree = ScriptableObject.CreateInstance<BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            Tree.Root = root;

            var selector = ScriptableObject.CreateInstance<SelectorNode>();
            Tree.AddNode(selector);
            Tree.AddChild(root, selector);

            // Add some dummy nodes
            for (int i = 0; i < 10; i++)
            {
                var action = ScriptableObject.CreateInstance<WaitNode>(); // Assuming WaitNode exists or similar
                action.Duration = 0.1f;
                Tree.AddNode(action);
                Tree.AddChild(selector, action);
            }
        }

        Tree = (BehaviorTree)Tree.Clone(this.gameObject);
        _blackBoard = new BlackBoard();

        // Pre-warm
        Tree.BTUpdate(_blackBoard);
    }

    private void Update()
    {
        if (Tree == null) return;
#if UNITY_EDITOR
        Profiler.BeginSample("BehaviorTree.Update");
#endif
        Tree.BTUpdate(_blackBoard);
#if UNITY_EDITOR
        Profiler.EndSample();
#endif

#if UNITY_EDITOR
        // Test Typed Blackboard Access
        Profiler.BeginSample("BlackBoard.TypedAccess");
#endif
        _blackBoard.SetInt("TestInt", Time.frameCount);
        int val = _blackBoard.GetInt("TestInt");
#if UNITY_EDITOR
        Profiler.EndSample();
#endif
    }
}
