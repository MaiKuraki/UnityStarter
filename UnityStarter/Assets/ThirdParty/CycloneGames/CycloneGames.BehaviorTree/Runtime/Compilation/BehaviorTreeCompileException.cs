using System;

namespace CycloneGames.BehaviorTree.Runtime.Compilation
{
    public sealed class BehaviorTreeCompileException : Exception
    {
        public BehaviorTreeCompileException(string message) : base(message)
        {
        }

        public BehaviorTreeCompileException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
