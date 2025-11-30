using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("BlackBoard", "Decorator that provides a blackboard context for child nodes.")]
    public class BlackBoardNode : DecoratorNode
    {
        public BlackBoard BlackBoard => _blackBoard;
        [SerializeField] private BlackBoard _blackBoard;
        protected override void OnStart(IBlackBoard blackBoard)
        {
            _blackBoard = new BlackBoard();
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            _blackBoard.Parent = blackBoard as BlackBoard;
            var result = Child.Run(_blackBoard);
            return result;
        }
        protected override void OnStop(IBlackBoard blackBoard)
        {
            base.OnStop(blackBoard);
            _blackBoard.Clear();
        }
    }
}