using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("BlackBoard", "Decorator that provides a blackboard context for child nodes.")]
    public class BlackBoardNode : DecoratorNode
    {
        public BlackBoard BlackBoard => _blackBoard;
        [SerializeField] private BlackBoard _blackBoard;
        protected override void OnStart(BlackBoard blackBoard)
        {
            _blackBoard = new BlackBoard();
        }

        protected override BTState OnRun(BlackBoard blackBoard)
        {
            _blackBoard.Parent = blackBoard;
            var result = Child.Run(_blackBoard);
            return result;
        }
        protected override void OnStop(BlackBoard blackBoard)
        {
            base.OnStop(blackBoard);
            _blackBoard.Clear();
        }
    }
}