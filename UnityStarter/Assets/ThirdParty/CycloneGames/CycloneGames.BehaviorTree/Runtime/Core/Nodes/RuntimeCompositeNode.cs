using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public abstract class RuntimeCompositeNode : RuntimeNode
    {
        public List<RuntimeNode> Children { get; private set; } = new List<RuntimeNode>();

        public void AddChild(RuntimeNode child)
        {
            if (child != null)
            {
                Children.Add(child);
            }
        }
        
        public void SetChildren(List<RuntimeNode> children)
        {
            Children = children;
        }

        public override void OnAwake()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].OnAwake();
            }
        }
    }
}
