using Unity.Mathematics;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public abstract class MovementStateBase2D
    {
        public abstract MovementStateType StateType { get; }

        public virtual void OnEnter(ref MovementContext2D context) { }

        public virtual void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            displacement = float2.zero;
        }

        public virtual void OnFixedUpdate(ref MovementContext2D context) { }

        public virtual void OnExit(ref MovementContext2D context) { }

        public virtual MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            return null;
        }
    }
}
